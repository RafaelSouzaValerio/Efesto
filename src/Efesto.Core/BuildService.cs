using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Efesto.Core;

public sealed record BuildResult(string Name, bool Success, bool Cancelled, string? OutputPath, TimeSpan Duration, string? Error)
{
    public string? CleanupWarning { get; init; }
}

public enum BuildStage { CopyingSources, PreparingConfiguration, Compiling, ExecutingBatch, Publishing, CleaningWorkspace }

public sealed class BuildService
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "node_modules", "obj" };
    private static readonly string[] PublishExcluded = ["App_Data", "BuildProcessTemplates", "Documentação", "Documentacao", "SP", "SP_Instalacao", "Stimul", "Temp"];

    public async Task<BuildResult> RunAsync(CompilerConfiguration config, CompilationJob job, Action<string> log, CancellationToken cancellationToken, Action<BuildStage>? progress = null)
    {
        var watch = Stopwatch.StartNew();
        string? workspace = null;
        BuildResult result;
        var cleanupWarnings = new List<string>();
        try
        {
            if (!job.IsSelected) throw new InvalidOperationException("Aplicação não selecionada para este processamento.");
            Validation.Validate(config);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(config.DestinationPath);
            cleanupWarnings.AddRange(await WorkspaceCleanup.RecoverAsync(config.DestinationPath, log));
            cancellationToken.ThrowIfCancellationRequested();
            workspace = Path.Combine(config.DestinationPath, ".compiler-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            var source = Path.Combine(workspace, "source");
            var output = Path.Combine(workspace, "output");
            progress?.Invoke(BuildStage.CopyingSources);
            log("Preparando uma cópia isolada dos fontes...");
            await Task.Run(() => CopySource(job.SourcePath, source, cancellationToken), cancellationToken);
            progress?.Invoke(BuildStage.PreparingConfiguration);
            WebConfiguration.Apply(job, source, log);
            if (job.Mode == CompilationMode.AspNet)
            {
                foreach (var file in Directory.GetFiles(source, "*.config"))
                    if (!Path.GetFileName(file).Equals("web.config", StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            }
            else Directory.CreateDirectory(output);

            var script = Path.Combine(workspace, "compile.cmd");
            await File.WriteAllTextAsync(script, CreateScript(config, job), new UTF8Encoding(false), cancellationToken);
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                Arguments = $"/d /s /c \"\"{script}\"\"", WorkingDirectory = source,
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.Environment["AC_SOURCE"] = source;
            start.Environment["AC_OUTPUT"] = output;
            start.Environment["AC_NAME"] = job.Name;
            start.Environment["AC_DEV_CMD"] = config.DeveloperCommandPath;
            start.Environment["AC_BATCH"] = job.BatchPath;
            log(job.Mode == CompilationMode.AspNet ? "Compilando site ASP.NET..." : "Executando BAT personalizado...");
            using var process = new Process { StartInfo = start };
            progress?.Invoke(job.Mode == CompilationMode.AspNet ? BuildStage.Compiling : BuildStage.ExecutingBatch);
            process.Start();
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
            var stdout = ReadLinesAsync(process.StandardOutput, log);
            var stderr = ReadLinesAsync(process.StandardError, log);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidOperationException($"O processo retornou código {process.ExitCode}. Consulte o log.");
            progress?.Invoke(BuildStage.Publishing);
            if (!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any())
                throw new InvalidOperationException("O processo terminou sem gerar arquivos na pasta de saída. No BAT, grave os resultados em %AC_OUTPUT%.");
            RejectTreeLinks(output);
            if (job.Mode == CompilationMode.AspNet)
            {
                if (!Directory.Exists(Path.Combine(output, "bin")) || !Directory.EnumerateFiles(Path.Combine(output, "bin")).Any())
                    throw new InvalidOperationException("A compilação ASP.NET não gerou arquivos na pasta bin.");
                CleanPublication(output);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(config.DestinationPath, job.Name);
            Promote(output, destination, log);
            result = new(job.Name, true, false, destination, watch.Elapsed, null);
        }
        catch (OperationCanceledException)
        {
            log("Compilação cancelada.");
            result = new(job.Name, false, true, null, watch.Elapsed, null);
        }
        catch (Exception ex)
        {
            log("Falha: " + ex.Message);
            result = new(job.Name, false, false, null, watch.Elapsed, ex.Message);
        }
        finally
        {
            if (workspace is not null)
            {
                try { progress?.Invoke(BuildStage.CleaningWorkspace); } catch { }
                try { await WorkspaceCleanup.DeleteAsync(config.DestinationPath, workspace, log); }
                catch (Exception ex)
                {
                    var retry = File.Exists(workspace + ".cleanup") ? " A exclusão foi registrada para nova tentativa no próximo processamento neste destino." : " Não foi possível registrar a limpeza automática; verifique o acesso a essa pasta.";
                    var warning = $"Não foi possível excluir a cópia temporária em {workspace}: {ex.Message}." + retry;
                    cleanupWarnings.Add(warning);
                    try { log(warning); } catch { }
                }
            }
        }
        if (result.Success) log($"Concluído em {watch.Elapsed:hh\\:mm\\:ss}. Saída: {result.OutputPath}");
        return result with { Duration = watch.Elapsed, CleanupWarning = cleanupWarnings.Count == 0 ? null : string.Join(Environment.NewLine, cleanupWarnings) };
    }

    private static string CreateScript(CompilerConfiguration config, CompilationJob job)
    {
        var script = new StringBuilder("@echo off\r\nsetlocal DisableDelayedExpansion\r\nchcp 65001 >nul\r\n");
        if (!string.IsNullOrWhiteSpace(config.DeveloperCommandPath))
        {
            // Preserve the original CMD.txt environment. Omitting host_arch selects Framework (x86)
            // on this installation, which cannot load the legacy site's Foxit x64 assembly.
            var arguments = job.Mode == CompilationMode.AspNet ? "-arch=arm -host_arch=amd64" : "-no_logo";
            script.Append($"echo Inicializando Dev CMD: {arguments}\r\ncall \"%AC_DEV_CMD%\" {arguments}\r\nif errorlevel 1 exit /b %errorlevel%\r\nsetlocal DisableDelayedExpansion\r\nchcp 65001 >nul\r\n");
        }
        script.Append("cd /d \"%AC_SOURCE%\"\r\n");
        if (job.Mode == CompilationMode.AspNet)
            script.Append("echo Compilador ASP.NET localizado no PATH:\r\nwhere aspnet_compiler\r\nif errorlevel 1 exit /b %errorlevel%\r\necho Parametros: -nologo -p fontes -fixednames -f -v /nome saida\r\naspnet_compiler -nologo -p \"%AC_SOURCE%\" -fixednames -f -v \"/%AC_NAME%\" \"%AC_OUTPUT%\"\r\nexit /b %errorlevel%\r\n");
        else
            script.Append("set \"appPath=%AC_SOURCE%\"\r\nset \"destinyPath=%AC_OUTPUT%\"\r\nset \"appName=%AC_NAME%\"\r\ncall \"%AC_BATCH%\" \"%AC_SOURCE%\" \"%AC_OUTPUT%\" \"%AC_NAME%\"\r\nexit /b %errorlevel%\r\n");
        return script.ToString();
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> log)
    {
        while (await reader.ReadLineAsync() is { } line) log(line);
    }

    private static void CopySource(string source, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Validation.RejectLinks(source);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            token.ThrowIfCancellationRequested();
            Validation.RejectLinks(file);
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            if (!Excluded.Contains(Path.GetFileName(directory))) CopySource(directory, Path.Combine(destination, Path.GetFileName(directory)), token);
    }

    private static void RejectTreeLinks(string path)
    {
        Validation.RejectLinks(path);
        foreach (var file in Directory.EnumerateFiles(path)) Validation.RejectLinks(file);
        foreach (var directory in Directory.EnumerateDirectories(path)) RejectTreeLinks(directory);
    }

    private static void CleanPublication(string output)
    {
        foreach (var name in PublishExcluded)
        {
            var path = Path.Combine(output, name);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        foreach (var file in Directory.GetFiles(output))
            if (Path.GetExtension(file).Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
                new[] { "app_online.htm", "app_offline.htm", "Controle de Atividades.txt", "Controle de Atividades .txt" }.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)) File.Delete(file);
    }

    private static void Promote(string staged, string destination, Action<string> log)
    {
        Validation.RejectLinks(destination);
        string? backup = null;
        if (Directory.Exists(destination))
        {
            backup = destination + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
            Directory.Move(destination, backup);
        }
        try { Directory.Move(staged, destination); }
        catch
        {
            if (backup is not null) Directory.Move(backup, destination);
            throw;
        }
        if (backup is not null) log("Publicação anterior preservada em: " + backup);
    }

    public static async Task<IReadOnlyList<string>> CreateArchivesAsync(CompilerConfiguration config, IReadOnlyList<BuildResult> results, CancellationToken token)
    {
        if (!Enum.IsDefined(config.ZipMode)) throw new InvalidOperationException("Modo de compactação inválido.");
        var paths = new List<string>();
        if (config.CreateZip && config.ZipMode == ZipMode.Unified && results.Count > 0 && results.All(r => r.Success))
            paths.Add(await CreateZipAsync(config.DestinationPath, results, token));
        var selected = config.Jobs.Where(j => j.IsSelected && j.CreateZip).Select(j => j.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Retain compatibility for callers holding the earlier in-memory configuration shape.
        var legacyIndividual = config.CreateZip && config.ZipMode == ZipMode.PerApplication;
        foreach (var result in results.Where(r => r.Success && (legacyIndividual || selected.Contains(r.Name))))
        {
            token.ThrowIfCancellationRequested();
            paths.Add(await CreateArchiveAsync(config.DestinationPath, [result], result.Name, includeApplicationFolder: false, token));
        }
        return paths;
    }

    public static Task<string> CreateZipAsync(string destination, IReadOnlyList<BuildResult> results, CancellationToken token)
        => CreateArchiveAsync(destination, results, "Efesto", includeApplicationFolder: true, token);

    private static async Task<string> CreateArchiveAsync(string destination, IReadOnlyList<BuildResult> results, string name, bool includeApplicationFolder, CancellationToken token)
    {
        if (results.Count == 0 || results.Any(r => !r.Success || r.OutputPath is null))
            throw new InvalidOperationException("O ZIP é gerado somente quando todas as compilações têm sucesso.");
        Validation.ValidateName(name);
        var path = Path.Combine(destination, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        try
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                foreach (var result in results)
                {
                    RejectTreeLinks(result.OutputPath!);
                    foreach (var file in Directory.EnumerateFiles(result.OutputPath!, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        var entryName = (includeApplicationFolder ? result.Name + "/" : "") + Path.GetRelativePath(result.OutputPath!, file).Replace('\\', '/');
                        archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                    }
                }
                token.ThrowIfCancellationRequested();
            }, token);
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
}
