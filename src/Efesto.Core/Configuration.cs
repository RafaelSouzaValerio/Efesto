using System.Text.Json;

namespace Efesto.Core;

public enum CompilationMode { AspNet, CustomBatch }
public enum WebConfigMode { ProjectDefault, Copy, Transform, KeepOriginal }
public enum ZipMode { Unified, PerApplication }

public sealed record CompilationJob
{
    public string Name { get; set; } = "Aplicacao";
    public string SourcePath { get; set; } = "";
    public CompilationMode Mode { get; set; }
    public string BatchPath { get; set; } = "";
    public WebConfigMode WebConfigMode { get; set; }
    public string WebConfigPath { get; set; } = "";
    public bool IsExpanded { get; set; } = true;
    public bool CreateZip { get; set; }
    public bool IsSelected { get; set; } = true;
}

public sealed record CompilerConfiguration
{
    public int Version { get; set; } = 1;
    public string DeveloperCommandPath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public bool CreateZip { get; set; }
    public ZipMode ZipMode { get; set; }
    public List<CompilationJob> Jobs { get; set; } = [new()];
}

public sealed class ConfigurationStore(string? path = null)
{
    public string FilePath { get; } = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApplicationCompiler.WinUI", "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CompilerConfiguration Load()
    {
        if (!File.Exists(FilePath)) return new();
        var result = JsonSerializer.Deserialize<CompilerConfiguration>(File.ReadAllText(FilePath), JsonOptions)
            ?? throw new InvalidDataException("A configuração salva está vazia.");
        if (result.Version != 1 || result.DeveloperCommandPath is null || result.DestinationPath is null || !Enum.IsDefined(result.ZipMode) || result.Jobs is null ||
            result.Jobs.Any(j => j is null || j.Name is null || j.SourcePath is null || j.BatchPath is null || j.WebConfigPath is null || !Enum.IsDefined(j.Mode) || !Enum.IsDefined(j.WebConfigMode)))
            throw new InvalidDataException("Formato de configuração não suportado.");
        if (result.Jobs.Count == 0) result.Jobs.Add(new());
        // Convert the previous global "one ZIP per application" setting into individual choices.
        if (result.ZipMode == ZipMode.PerApplication)
        {
            if (result.CreateZip) foreach (var job in result.Jobs) job.CreateZip = true;
            result.CreateZip = false;
            result.ZipMode = ZipMode.Unified;
        }
        return result;
    }

    public void Save(CompilerConfiguration configuration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(configuration, JsonOptions));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
