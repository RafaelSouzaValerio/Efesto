using System.Text;

namespace Efesto.Core;

public static class BatchExporter
{
    public static string Export(CompilerConfiguration config, string directory, string runner)
    {
        Validation.Validate(config); Validation.ValidatePath(directory, "Pasta do BAT"); Validation.ValidatePath(runner, "Executor");
        if (!File.Exists(runner)) throw new FileNotFoundException("O executor não está publicado. Execute build.ps1 para gerar o aplicativo completo.", runner);
        Directory.CreateDirectory(directory);
        var name = "Compilar-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        new ConfigurationStore(Path.Combine(directory, name + ".json")).Save(config);
        var path = Path.Combine(directory, name + ".bat");
        File.WriteAllText(path, $"@echo off\r\nsetlocal DisableDelayedExpansion\r\nchcp 65001 >nul\r\n\"{runner}\" --config \"%~dp0{name}.json\"\r\nexit /b %errorlevel%\r\n", new UTF8Encoding(false));
        return path;
    }
}
