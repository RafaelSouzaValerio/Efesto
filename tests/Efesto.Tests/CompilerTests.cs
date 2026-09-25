using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using Efesto.Core;
using Xunit;

namespace Efesto.Tests;

public sealed class CompilerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Compiler tests ação & (paths) " + Guid.NewGuid().ToString("N"));
    private CompilerConfiguration Config(string script = "@echo off\r\necho compiled>\"%AC_OUTPUT%\\result.txt\"\r\nexit /b 0\r\n")
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "original.txt"), "unchanged");
        var batch = Path.Combine(root, "custom build.bat"); File.WriteAllText(batch, script);
        return new() { DestinationPath = Path.Combine(root, "destination"), Jobs = [new() { Name = "Test App", SourcePath = source, Mode = CompilationMode.CustomBatch, BatchPath = batch }] };
    }

    [Fact] public void SettingsRoundTripPreservesAllFields()
    {
        var config = Config(); config.DeveloperCommandPath = "C:\\dev\\VsDevCmd.bat"; config.CreateZip = true;
        config.ZipMode = ZipMode.Unified; config.Jobs[0].IsExpanded = false; config.Jobs[0].CreateZip = true;
        config.Jobs[0].WebConfigMode = WebConfigMode.Copy; config.Jobs[0].WebConfigPath = "C:\\configs\\full.config";
        config.Jobs.Add(new() { Name = "Second", IsSelected = false, Mode = CompilationMode.AspNet, SourcePath = "C:\\source2", WebConfigMode = WebConfigMode.Transform, WebConfigPath = "C:\\configs\\production.config" });
        var store = new ConfigurationStore(Path.Combine(root, "settings.json")); store.Save(config); var saved = store.Load();
        Assert.Equal(config.DeveloperCommandPath, saved.DeveloperCommandPath); Assert.Equal(config.DestinationPath, saved.DestinationPath);
        Assert.True(saved.CreateZip); Assert.Equal(ZipMode.Unified, saved.ZipMode); Assert.False(saved.Jobs[0].IsExpanded); Assert.Equal(config.Jobs, saved.Jobs); Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        saved.Jobs.RemoveAt(0); store.Save(saved); Assert.Single(store.Load().Jobs);
    }
    [Fact] public void InvalidSettingsAreNotOverwritten()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "settings.json"); File.WriteAllText(path, "broken json");
        Assert.ThrowsAny<Exception>(() => new ConfigurationStore(path).Load()); Assert.Equal("broken json", File.ReadAllText(path));
    }
    [Fact] public async Task UnselectedApplicationDoesNotBlockBuildOrEnterArchives()
    {
        var config = Config(); config.CreateZip = true; config.Jobs[0].CreateZip = true;
        config.Jobs.Add(new() { Name = "Offline", IsSelected = false, CreateZip = true, Mode = CompilationMode.AspNet, SourcePath = "Z:\\missing-site", WebConfigMode = WebConfigMode.Copy, WebConfigPath = "Z:\\missing.config" });
        var oldOutput = Path.Combine(config.DestinationPath, "Offline"); Directory.CreateDirectory(oldOutput);
        File.WriteAllText(Path.Combine(oldOutput, "previous.txt"), "previous");
        Validation.Validate(config);
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.True(result.Success, result.Error);
        var archives = await BuildService.CreateArchivesAsync(config, [result], CancellationToken.None);
        Assert.Equal(2, archives.Count);
        foreach (var path in archives)
        {
            using var archive = ZipFile.OpenRead(path);
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("Offline") || entry.FullName.Contains("previous.txt"));
        }
        Assert.Equal("previous", File.ReadAllText(Path.Combine(oldOutput, "previous.txt")));
    }
    [Fact] public async Task NothingSelectedFailsBeforeCreatingOutput()
    {
        var config = Config(); config.Jobs[0].IsSelected = false;
        var error = Assert.Throws<InvalidOperationException>(() => Validation.Validate(config));
        Assert.Contains("Marque pelo menos uma", error.Message);
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.False(result.Success); Assert.False(Directory.Exists(config.DestinationPath));
    }
    [Fact] public async Task CustomBatSupportsSpacesUnicodeAndShellCharacters()
    {
        var config = Config("@echo off\r\necho changed>\"%AC_SOURCE%\\original.txt\"\r\necho %~3>\"%~2\\result.txt\"\r\necho log-output\r\necho log-error 1>&2\r\nexit /b 0\r\n");
        var logs = new ConcurrentQueue<string>();
        var stages = new List<BuildStage>();
        var result = await new BuildService().RunAsync(config, config.Jobs[0], line =>
        {
            if (line == "log-output") Assert.Equal(BuildStage.ExecutingBatch, stages.Last());
            logs.Enqueue(line);
        }, CancellationToken.None, stages.Add);
        Assert.True(result.Success, string.Join("\n", logs));
        Assert.Equal(new[] { BuildStage.CopyingSources, BuildStage.PreparingConfiguration, BuildStage.ExecutingBatch, BuildStage.Publishing, BuildStage.CleaningWorkspace }, stages);
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(config.Jobs[0].SourcePath, "original.txt")));
        Assert.Contains("Test App", File.ReadAllText(Path.Combine(result.OutputPath!, "result.txt")));
        Assert.Contains("log-output", logs); Assert.Contains("log-error ", logs);
        Assert.Empty(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
    }
    [Fact] public async Task NonzeroExitPreservesPreviousPublication()
    {
        var config = Config("@echo off\r\necho Concluido\r\nexit /b 7\r\n");
        var destination = Path.Combine(config.DestinationPath, config.Jobs[0].Name); Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "old.txt"), "previous");
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.False(result.Success); Assert.Contains("7", result.Error); Assert.Equal("previous", File.ReadAllText(Path.Combine(destination, "old.txt")));
        Assert.Null(result.CleanupWarning); Assert.Empty(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
    }
    [Fact] public async Task CleanupRemovesReadOnlyCopiesButPreservesOriginalAttributes()
    {
        var config = Config();
        var original = Path.Combine(config.Jobs[0].SourcePath, "original.txt");
        File.SetAttributes(original, FileAttributes.ReadOnly);
        try
        {
            var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
            Assert.True(result.Success, result.Error); Assert.Null(result.CleanupWarning);
            Assert.Empty(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
            Assert.Empty(Directory.GetFiles(config.DestinationPath, "*.cleanup"));
            Assert.True(File.GetAttributes(original).HasFlag(FileAttributes.ReadOnly));
        }
        finally { File.SetAttributes(original, FileAttributes.Normal); }
    }
    [Fact] public async Task CleanupStillRunsWhenRecoveryTicketCannotBeCreated()
    {
        var config = Config(); string? workspace = null;
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None, stage =>
        {
            if (stage != BuildStage.PreparingConfiguration) return;
            workspace = Assert.Single(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
            Directory.CreateDirectory(workspace + ".cleanup");
        });
        Assert.True(result.Success, result.Error); Assert.Null(result.CleanupWarning);
        Assert.False(Directory.Exists(workspace));
        Assert.True(Directory.Exists(workspace + ".cleanup"));
    }
    [Fact] public async Task CleanupRetriesTemporaryLocksEvenAfterCancellation()
    {
        var config = Config(); using var cancellation = new CancellationTokenSource();
        FileStream? locked = null; var retries = 0;
        try
        {
            var result = await new BuildService().RunAsync(config, config.Jobs[0], line =>
            {
                if (line.Contains("Aguardando liberação")) { retries++; locked?.Dispose(); }
            }, cancellation.Token, stage =>
            {
                if (stage != BuildStage.PreparingConfiguration) return;
                var workspace = Assert.Single(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
                locked = new FileStream(Path.Combine(workspace, "source", "original.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
                cancellation.Cancel();
            });
            Assert.True(result.Cancelled); Assert.True(retries > 0); Assert.Null(result.CleanupWarning);
            Assert.Empty(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
            Assert.Empty(Directory.GetFiles(config.DestinationPath, "*.cleanup"));
        }
        finally { locked?.Dispose(); }
    }
    [Fact] public async Task PersistentLockIsReportedAndRecoveredByNextBuildWithoutDeletingUntrackedFolders()
    {
        var config = Config(); FileStream? locked = null; string? workspace = null;
        try
        {
            var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None, stage =>
            {
                if (stage != BuildStage.PreparingConfiguration) return;
                workspace = Assert.Single(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
                locked = new FileStream(Path.Combine(workspace, "source", "original.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
            });
            Assert.True(result.Success, result.Error); Assert.Contains(workspace!, result.CleanupWarning);
            Assert.True(Directory.Exists(workspace)); Assert.True(File.Exists(workspace + ".cleanup"));
            locked!.Dispose();
            var untracked = Path.Combine(config.DestinationPath, ".compiler-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(untracked); File.WriteAllText(Path.Combine(untracked, "keep.txt"), "keep");
            File.WriteAllText(untracked + ".cleanup", "not an Efesto cleanup ticket");
            using (var activeCleanup = new FileStream(workspace + ".cleanup", FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var concurrent = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
                Assert.True(concurrent.Success, concurrent.Error); Assert.True(Directory.Exists(workspace));
            }
            var next = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
            Assert.True(next.Success, next.Error); Assert.Null(next.CleanupWarning);
            Assert.False(Directory.Exists(workspace)); Assert.False(File.Exists(workspace + ".cleanup"));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(untracked, "keep.txt")));
            Assert.Equal("unchanged", File.ReadAllText(Path.Combine(config.Jobs[0].SourcePath, "original.txt")));
        }
        finally { locked?.Dispose(); }
    }
    [Fact] public async Task SuccessKeepsBackupAndReplacesPublication()
    {
        var config = Config(); var target = Path.Combine(config.DestinationPath, config.Jobs[0].Name); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "old.txt"), "old");
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.True(result.Success, result.Error); Assert.False(File.Exists(Path.Combine(target, "old.txt")));
        var backup = Assert.Single(Directory.GetDirectories(config.DestinationPath, "*.backup-*")); Assert.True(File.Exists(Path.Combine(backup, "old.txt")));
    }
    [Fact] public async Task EmptySuccessfulBatchIsReportedAsFailure()
    {
        var config = Config("@echo off\r\nexit /b 0\r\n");
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.False(result.Success); Assert.Contains("sem gerar", result.Error);
    }
    [Fact] public async Task CancellationKillsChildProcessAndKeepsOutput()
    {
        var config = Config("@echo off\r\necho ready\r\nping 127.0.0.1 -n 45 >nul\r\necho bad>\"%AC_OUTPUT%\\late.txt\"\r\n");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stages = new List<BuildStage>();
        var task = new BuildService().RunAsync(config, config.Jobs[0], line => { if (line == "ready") started.TrySetResult(); }, cts.Token, stages.Add);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); var stopwatch = Stopwatch.StartNew(); cts.Cancel();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.True(result.Cancelled); Assert.False(result.Success); Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8));
        Assert.Contains(BuildStage.ExecutingBatch, stages); Assert.DoesNotContain(BuildStage.Publishing, stages);
        Assert.Equal(BuildStage.CleaningWorkspace, stages.Last());
        Assert.Empty(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
    }
    [Theory]
    [InlineData("../escape")][InlineData("CON")][InlineData("same.")][InlineData("bad%PATH%")][InlineData("..")][InlineData("COM1.txt")][InlineData("_logs")]
    public void UnsafeNamesRejected(string name) => Assert.Throws<InvalidOperationException>(() => Validation.ValidateName(name));
    [Fact] public void DuplicateNamesAndOverlappingPathsRejected()
    {
        var config = Config(); config.Jobs.Add(config.Jobs[0] with { Name = "test app" }); Assert.Throws<InvalidOperationException>(() => Validation.Validate(config));
        config.Jobs.RemoveAt(1); config.DestinationPath = Path.Combine(config.Jobs[0].SourcePath, "output"); Assert.Throws<InvalidOperationException>(() => Validation.Validate(config));
    }
    [Fact] public async Task ZipContainsOnlySuccessfulPublication()
    {
        var config = Config(); var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.True(result.Success, result.Error);
        var zip = await BuildService.CreateZipAsync(config.DestinationPath, [result], CancellationToken.None);
        using var archive = ZipFile.OpenRead(zip); Assert.Equal("Test App/result.txt", Assert.Single(archive.Entries).FullName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => BuildService.CreateZipAsync(config.DestinationPath, [result with { Success = false }], CancellationToken.None));
    }
    [Fact] public async Task DeveloperCommandFailureDoesNotExecuteCustomBatch()
    {
        var config = Config(); config.DeveloperCommandPath = Path.Combine(root, "dev.bat"); File.WriteAllText(config.DeveloperCommandPath, "@exit /b 9\r\n");
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.False(result.Success); Assert.Contains("9", result.Error);
    }
    [Fact] public async Task AspNetFlowTransformsAndCleansOnlyStagingCopy()
    {
        var config = Config(); var source = config.Jobs[0].SourcePath; config.Jobs[0].Mode = CompilationMode.AspNet;
        File.WriteAllText(Path.Combine(source, "web.config"), "<configuration><appSettings><add key=\"environment\" value=\"dev\"/></appSettings></configuration>");
        File.WriteAllText(Path.Combine(source, "web.publish.config"), "<configuration xmlns:xdt=\"http://schemas.microsoft.com/XML-Document-Transform\"><appSettings><add key=\"environment\" value=\"prod\" xdt:Transform=\"SetAttributes\" xdt:Locator=\"Match(key)\"/></appSettings></configuration>");
        File.WriteAllText(Path.Combine(source, "other.config"), "remove only from copy");
        Directory.CreateDirectory(Path.Combine(source, ".git")); File.WriteAllText(Path.Combine(source, ".git", "secret"), "excluded");
        var mockTools = Path.Combine(root, "mock-tools"); Directory.CreateDirectory(mockTools);
        // A fake compiler validates the orchestration without needing an actual production site.
        File.WriteAllText(Path.Combine(mockTools, "aspnet_compiler.cmd"), "@echo off\r\nmkdir \"%AC_OUTPUT%\\bin\"\r\necho assembly>\"%AC_OUTPUT%\\bin\\site.dll\"\r\ncopy \"%AC_SOURCE%\\web.config\" \"%AC_OUTPUT%\\web.config\" >nul\r\nif exist \"%AC_SOURCE%\\other.config\" exit /b 12\r\nif exist \"%AC_SOURCE%\\.git\" exit /b 13\r\nmkdir \"%AC_OUTPUT%\\App_Data\"\r\necho data>\"%AC_OUTPUT%\\App_Data\\data.txt\"\r\nexit /b 0\r\n");
        config.DeveloperCommandPath = Path.Combine(root, "dev.bat"); File.WriteAllText(config.DeveloperCommandPath, $"@set \"PATH={mockTools};%PATH%\"\r\n@exit /b 0\r\n");
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.True(result.Success, result.Error); Assert.Contains("prod", File.ReadAllText(Path.Combine(result.OutputPath!, "web.config")));
        Assert.Contains("dev", File.ReadAllText(Path.Combine(source, "web.config"))); Assert.True(File.Exists(Path.Combine(source, "other.config")));
        Assert.False(Directory.Exists(Path.Combine(result.OutputPath!, "App_Data")));
    }
    public void Dispose()
    {
        if (Directory.Exists(root) && Path.GetFileName(root).StartsWith("Compiler tests ") && Validation.Contains(Path.GetTempPath(), root)) Directory.Delete(root, true);
    }
}
