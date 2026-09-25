using System.IO.Compression;
using System.Text.Json;
using Efesto.Core;
using Xunit;

namespace Efesto.Tests;

public sealed class ConfigurationFeatureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Forja-tests-" + Guid.NewGuid().ToString("N"));
    private const string Original = "<configuration><appSettings><add key=\"Environment\" value=\"Original\"/></appSettings></configuration>";
    private const string Replacement = "<configuration><appSettings><add key=\"NewSetting\" value=\"FullCopy\"/></appSettings></configuration>";
    private static string Transform(string value) => $"<configuration xmlns:xdt=\"http://schemas.microsoft.com/XML-Document-Transform\"><appSettings><add key=\"Environment\" value=\"{value}\" xdt:Transform=\"SetAttributes\" xdt:Locator=\"Match(key)\"/></appSettings></configuration>";

    private CompilerConfiguration MakeConfig(CompilationMode mode, WebConfigMode treatment)
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "web.config"), Original);
        File.WriteAllText(Path.Combine(source, "web.publish.config"), Transform("Automatic"));
        var file = Path.Combine(root, "selected.config");
        File.WriteAllText(file, treatment == WebConfigMode.Copy ? Replacement : Transform("External"));
        // Capture the prepared config as the result, in both a BAT and a mock ASP.NET compiler.
        var batch = Path.Combine(root, "build.cmd");
        File.WriteAllText(batch, "@echo off\r\nif not exist \"%AC_OUTPUT%\\bin\" mkdir \"%AC_OUTPUT%\\bin\"\r\necho compiled>\"%AC_OUTPUT%\\bin\\app.dll\"\r\ncopy \"%AC_SOURCE%\\web.config\" \"%AC_OUTPUT%\\web.config\" >nul\r\nexit /b %errorlevel%\r\n");
        var config = new CompilerConfiguration { DestinationPath = Path.Combine(root, "output"), Jobs = [new() { Name = "Demo", SourcePath = source, Mode = mode, BatchPath = batch, WebConfigMode = treatment, WebConfigPath = file }] };
        if (mode == CompilationMode.AspNet)
        {
            File.Copy(batch, Path.Combine(root, "aspnet_compiler.cmd"), true);
            config.DeveloperCommandPath = Path.Combine(root, "dev.cmd");
            File.WriteAllText(config.DeveloperCommandPath, $"@set \"PATH={root};%PATH%\"\r\n@exit /b 0\r\n");
        }
        return config;
    }

    [Theory]
    [InlineData(CompilationMode.AspNet, WebConfigMode.Copy, "FullCopy")]
    [InlineData(CompilationMode.CustomBatch, WebConfigMode.Copy, "FullCopy")]
    [InlineData(CompilationMode.AspNet, WebConfigMode.Transform, "External")]
    [InlineData(CompilationMode.CustomBatch, WebConfigMode.Transform, "External")]
    [InlineData(CompilationMode.AspNet, WebConfigMode.KeepOriginal, "Original")]
    [InlineData(CompilationMode.CustomBatch, WebConfigMode.KeepOriginal, "Original")]
    [InlineData(CompilationMode.AspNet, WebConfigMode.ProjectDefault, "Automatic")]
    [InlineData(CompilationMode.CustomBatch, WebConfigMode.ProjectDefault, "Original")]
    public async Task ConfigIsPreparedBeforeBuildWithoutChangingInputs(CompilationMode mode, WebConfigMode treatment, string expected)
    {
        var config = MakeConfig(mode, treatment); var job = config.Jobs[0];
        var selectedBytes = File.ReadAllBytes(job.WebConfigPath);
        var result = await new BuildService().RunAsync(config, job, _ => { }, CancellationToken.None);
        Assert.True(result.Success, result.Error);
        var published = File.ReadAllText(Path.Combine(result.OutputPath!, "web.config"));
        Assert.Contains(expected, published);
        if (treatment != WebConfigMode.ProjectDefault) Assert.DoesNotContain("Automatic", published);
        Assert.Equal(Original, File.ReadAllText(Path.Combine(job.SourcePath, "web.config")));
        Assert.Equal(selectedBytes, File.ReadAllBytes(job.WebConfigPath));
        if (treatment == WebConfigMode.Copy) Assert.Equal(selectedBytes, File.ReadAllBytes(Path.Combine(result.OutputPath!, "web.config")));
    }

    [Fact] public async Task FullCopyDoesNotRequireExistingWebConfig()
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.Copy);
        File.Delete(Path.Combine(config.Jobs[0].SourcePath, "web.config"));
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.True(result.Success, result.Error);
    }

    [Fact] public void TransformRequiresBaseConfig()
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.Transform);
        File.Delete(Path.Combine(config.Jobs[0].SourcePath, "web.config"));
        Assert.Throws<FileNotFoundException>(() => Validation.Validate(config));
    }

    [Theory]
    [InlineData(WebConfigMode.Copy, "<broken>")]
    [InlineData(WebConfigMode.Transform, "<configuration />")]
    [InlineData(WebConfigMode.Copy, "<wrongRoot />")]
    [InlineData(WebConfigMode.Copy, "<!DOCTYPE configuration [<!ENTITY test SYSTEM 'file:///C:/missing'>]><configuration>&test;</configuration>")]
    public async Task InvalidFileFailsWithoutChangingPreviousPublication(WebConfigMode treatment, string xml)
    {
        var config = MakeConfig(CompilationMode.CustomBatch, treatment);
        File.WriteAllText(config.Jobs[0].WebConfigPath, xml);
        var previous = Path.Combine(config.DestinationPath, "Demo"); Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(previous, "web.config"), "previous publication");
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("previous publication", File.ReadAllText(Path.Combine(previous, "web.config")));
        Assert.Empty(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
    }

    [Fact] public void CopyRejectsTransformAndMissingFile()
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.Copy);
        File.WriteAllText(config.Jobs[0].WebConfigPath, Transform("WrongMode"));
        Assert.Throws<InvalidDataException>(() => Validation.Validate(config));
        File.Delete(config.Jobs[0].WebConfigPath);
        Assert.Throws<FileNotFoundException>(() => Validation.Validate(config));
    }

    [Fact] public async Task InvalidXdtOperationFailsWithoutPublishing()
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.Transform);
        File.WriteAllText(config.Jobs[0].WebConfigPath, Transform("Invalid").Replace("SetAttributes", "UnknownOperation"));
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.False(result.Success); Assert.False(Directory.Exists(Path.Combine(config.DestinationPath, "Demo")));
        Assert.Null(result.CleanupWarning); Assert.Empty(Directory.GetDirectories(config.DestinationPath, ".compiler-*"));
    }

    [Fact] public void LegacySettingsUseProjectDefaultAndSavedDevCmd()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{\"Version\":1,\"DeveloperCommandPath\":\"C:\\\\custom\\\\dev.cmd\",\"Jobs\":[{\"Name\":\"Legacy\",\"SourcePath\":\"C:\\\\site\",\"Mode\":0,\"BatchPath\":\"\"}]}");
        var loaded = new ConfigurationStore(path).Load();
        Assert.Equal(WebConfigMode.ProjectDefault, loaded.Jobs[0].WebConfigMode);
        Assert.True(loaded.Jobs[0].IsSelected);
        Assert.Equal("", loaded.Jobs[0].WebConfigPath);
        Assert.Contains("custom", loaded.DeveloperCommandPath);
        new ConfigurationStore(path).Save(loaded);
        Assert.Equal(loaded.Jobs[0], new ConfigurationStore(path).Load().Jobs[0]);
    }

    [Fact] public void DiscoverySkipsInstallationsWithoutDevCmd()
    {
        var ssms = Path.Combine(root, "SSMS"); Directory.CreateDirectory(ssms);
        var visualStudio = Path.Combine(root, "Visual Studio", "Professional");
        Directory.CreateDirectory(Path.Combine(visualStudio, "Common7", "Tools"));
        var command = Path.Combine(visualStudio, "Common7", "Tools", "VsDevCmd.bat"); File.WriteAllText(command, "@exit /b 0");
        Assert.Equal(command, DeveloperCommandLocator.FindInInstallations(["", ssms, visualStudio]));
        Assert.Null(DeveloperCommandLocator.FindInInstallations([ssms]));
    }

    [Fact] public async Task UnifiedZipHasOneFolderPerApplication()
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.Copy);
        config.Jobs.Add(config.Jobs[0] with { Name = "Second", WebConfigMode = WebConfigMode.KeepOriginal });
        var results = new List<BuildResult>();
        foreach (var job in config.Jobs) results.Add(await new BuildService().RunAsync(config, job, _ => { }, CancellationToken.None));
        Assert.All(results, r => Assert.True(r.Success, r.Error));
        var zip = await BuildService.CreateZipAsync(config.DestinationPath, results, CancellationToken.None);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "Demo", "Second" }, archive.Entries.Select(e => e.FullName.Split('/')[0]).Distinct().Order().ToArray());
        Assert.Single(Directory.GetFiles(config.DestinationPath, "*.zip"));
    }

    [Fact] public async Task LegacyDevCmdArgumentsArePreservedForAspNet()
    {
        var config = MakeConfig(CompilationMode.AspNet, WebConfigMode.ProjectDefault);
        File.WriteAllText(config.DeveloperCommandPath, $"@echo off\r\nif not \"%*\"==\"-arch=arm -host_arch=amd64\" exit /b 41\r\nset \"PATH={root};%PATH%\"\r\nexit /b 0\r\n");
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var result = await new BuildService().RunAsync(config, config.Jobs[0], log.Enqueue, CancellationToken.None);
        Assert.True(result.Success, result.Error);
        Assert.Contains(log, l => l.Contains("-arch=arm -host_arch=amd64"));
        Assert.Contains(log, l => l.Contains("aspnet_compiler.cmd", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ZipMode.Unified, 1)]
    [InlineData(ZipMode.PerApplication, 2)]
    public async Task ArchiveModeProducesCorrectNumberAndLayout(ZipMode mode, int count)
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.KeepOriginal);
        config.CreateZip = true; config.ZipMode = mode;
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        Assert.True(result.Success, result.Error);
        var results = new[] { result, result with { Name = "Second" } };
        var archives = await BuildService.CreateArchivesAsync(config, results, CancellationToken.None);
        Assert.Equal(count, archives.Count);
        foreach (var path in archives)
        {
            using var archive = ZipFile.OpenRead(path);
            if (mode == ZipMode.PerApplication)
            {
                Assert.Contains(archive.Entries, e => e.FullName == "web.config");
                Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("Demo/") || e.FullName.StartsWith("Second/"));
            }
            else Assert.Contains(archive.Entries, e => e.FullName == "Second/web.config");
        }
    }

    [Fact] public async Task IndividualZipIncludesOnlySuccessfulApplications()
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.KeepOriginal);
        config.CreateZip = true; config.ZipMode = ZipMode.PerApplication;
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        var results = new[] { result, result with { Name = "Failed", Success = false, OutputPath = null }, result with { Name = "Cancelled", Success = false, Cancelled = true, OutputPath = null } };
        var archive = Assert.Single(await BuildService.CreateArchivesAsync(config, results, CancellationToken.None));
        Assert.StartsWith("Demo-", Path.GetFileName(archive));
        config.ZipMode = ZipMode.Unified;
        Assert.Empty(await BuildService.CreateArchivesAsync(config, results, CancellationToken.None));
        config.CreateZip = false;
        Assert.Empty(await BuildService.CreateArchivesAsync(config, [result], CancellationToken.None));
    }

    [Fact] public async Task CancelledCompressionDoesNotLeaveAnIncompleteZip()
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.KeepOriginal);
        config.CreateZip = true; config.ZipMode = ZipMode.PerApplication;
        var result = await new BuildService().RunAsync(config, config.Jobs[0], _ => { }, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildService.CreateArchivesAsync(config, [result], cancellation.Token));
        Assert.Empty(Directory.GetFiles(config.DestinationPath, "*.zip"));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    public async Task IndividualSelectionIsIndependentOfUnifiedZip(bool unified, bool individual, int count)
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.KeepOriginal);
        config.CreateZip = unified; config.Jobs[0].CreateZip = individual;
        config.Jobs.Add(config.Jobs[0] with { Name = "Second", CreateZip = false });
        var results = new List<BuildResult>();
        foreach (var job in config.Jobs)
        {
            var output = Path.Combine(config.DestinationPath, job.Name); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "result.txt"), job.Name);
            results.Add(new(job.Name, true, false, output, TimeSpan.Zero, null));
        }
        var archives = await BuildService.CreateArchivesAsync(config, results, CancellationToken.None);
        Assert.Equal(count, archives.Count);
        foreach (var path in archives)
        {
            using var archive = ZipFile.OpenRead(path);
            if (Path.GetFileName(path).StartsWith("Demo-"))
            {
                Assert.True(individual);
                var entry = Assert.Single(archive.Entries); Assert.Equal("result.txt", entry.FullName);
                using var reader = new StreamReader(entry.Open()); Assert.Equal("Demo", reader.ReadToEnd());
            }
            else
            {
                Assert.True(unified);
                Assert.Equal(new[] { "Demo/result.txt", "Second/result.txt" }, archive.Entries.Select(e => e.FullName).Order().ToArray());
            }
        }
        Assert.DoesNotContain(archives, p => Path.GetFileName(p).StartsWith("Second-"));
    }

    [Fact] public async Task SelectedSuccessIsZippedEvenWhenAnotherSelectedJobFails()
    {
        var config = MakeConfig(CompilationMode.CustomBatch, WebConfigMode.KeepOriginal);
        config.CreateZip = true; config.Jobs[0].CreateZip = true;
        config.Jobs.Add(config.Jobs[0] with { Name = "Failed" });
        var output = Path.Combine(config.DestinationPath, "Demo"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "result.txt"), "Demo");
        BuildResult[] results = [new("Demo", true, false, output, TimeSpan.Zero, null), new("Failed", false, false, null, TimeSpan.Zero, "Error")];
        var archive = Assert.Single(await BuildService.CreateArchivesAsync(config, results, CancellationToken.None));
        Assert.StartsWith("Demo-", Path.GetFileName(archive));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyIndividualModeMigratesToEachJob(bool enabled)
    {
        Directory.CreateDirectory(root);
        var store = new ConfigurationStore(Path.Combine(root, "settings.json"));
        store.Save(new() { CreateZip = enabled, ZipMode = ZipMode.PerApplication, Jobs = [new() { Name = "One" }, new() { Name = "Two" }] });
        var loaded = store.Load();
        Assert.False(loaded.CreateZip); Assert.Equal(ZipMode.Unified, loaded.ZipMode);
        Assert.All(loaded.Jobs, job => Assert.Equal(enabled, job.CreateZip));
        store.Save(loaded);
        var reopened = store.Load();
        Assert.Equal(loaded.Jobs, reopened.Jobs); Assert.False(reopened.CreateZip);
    }

    public void Dispose()
    {
        if (Directory.Exists(root) && Validation.Contains(Path.GetTempPath(), root) && Path.GetFileName(root).StartsWith("Forja-tests-")) Directory.Delete(root, true);
    }
}
