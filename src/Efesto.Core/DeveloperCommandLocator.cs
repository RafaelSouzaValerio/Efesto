using System.Diagnostics;

namespace Efesto.Core;

public static class DeveloperCommandLocator
{
    public static string? FindInInstallations(IEnumerable<string> installations) => installations
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => Path.Combine(p.Trim(), "Common7", "Tools", "VsDevCmd.bat"))
        .FirstOrDefault(File.Exists);

    public static async Task<string?> FindAsync()
    {
        var programFiles = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct().ToArray();
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vswhere))
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(vswhere, "-products * -sort -property installationPath")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                var output = await stdout; await stderr;
                if (process.ExitCode == 0 && FindInInstallations(output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) is { } found) return found;
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        // The environment supports portable/custom installations; standard folders cover missing vswhere.
        if (FindInInstallations([Environment.GetEnvironmentVariable("VSINSTALLDIR") ?? ""]) is { } environment) return environment;
        foreach (var root in programFiles)
        {
            var visualStudio = Path.Combine(root, "Microsoft Visual Studio");
            if (!Directory.Exists(visualStudio)) continue;
            try
            {
                foreach (var version in Directory.GetDirectories(visualStudio).OrderDescending(StringComparer.OrdinalIgnoreCase))
                    if (FindInInstallations(Directory.GetDirectories(version)) is { } found) return found;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }
}
