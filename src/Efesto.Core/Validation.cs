namespace Efesto.Core;

public static class Validation
{
    public static void Validate(CompilerConfiguration config)
    {
        var selectedJobs = config.Jobs.Where(job => job.IsSelected).ToList();
        if (selectedJobs.Count == 0) throw new InvalidOperationException("Marque pelo menos uma aplicação para compilar.");
        if (!Enum.IsDefined(config.ZipMode)) throw new InvalidOperationException("Modo de compactação inválido.");
        ValidatePath(config.DestinationPath, "Pasta de saída");
        RejectLinks(config.DestinationPath);
        if (selectedJobs.Select(j => j.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedJobs.Count)
            throw new InvalidOperationException("Use um nome diferente para cada compilação.");
        if (!string.IsNullOrWhiteSpace(config.DeveloperCommandPath)) ValidateBatch(config.DeveloperCommandPath, "Developer Command Prompt");
        if (selectedJobs.Any(j => j.Mode == CompilationMode.AspNet) && string.IsNullOrWhiteSpace(config.DeveloperCommandPath))
            throw new InvalidOperationException("Informe o caminho do VsDevCmd.bat para compilar sites ASP.NET.");
        foreach (var job in selectedJobs)
        {
            ValidateName(job.Name);
            ValidatePath(job.SourcePath, $"Origem de {job.Name}");
            if (!Directory.Exists(job.SourcePath)) throw new DirectoryNotFoundException($"Origem não encontrada: {job.SourcePath}");
            RejectLinks(job.SourcePath);
            if (Contains(job.SourcePath, config.DestinationPath) || Contains(config.DestinationPath, job.SourcePath))
                throw new InvalidOperationException("As pastas de origem e saída devem ser independentes, sem uma estar dentro da outra.");
            if (job.Mode == CompilationMode.CustomBatch) ValidateBatch(job.BatchPath, $"BAT de {job.Name}");
            else if (job.Mode != CompilationMode.AspNet) throw new InvalidOperationException("Tipo de compilação inválido.");
            WebConfiguration.Validate(job);
            RejectLinks(Path.Combine(config.DestinationPath, job.Name));
        }
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith('.') || name.StartsWith('.') || name.Equals("_logs", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['%', '"', '\r', '\n']) >= 0 ||
            System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidOperationException($"Nome de aplicação inválido: {name}");
    }

    public static void ValidatePath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.IndexOfAny(['%', '"', '\r', '\n']) >= 0)
            throw new InvalidOperationException($"{label}: informe um caminho absoluto sem aspas, % ou quebras de linha.");
        if (Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) == Path.GetPathRoot(Path.GetFullPath(path))?.TrimEnd(Path.DirectorySeparatorChar))
            throw new InvalidOperationException($"{label}: selecione uma subpasta, não a raiz do disco.");
    }

    private static void ValidateBatch(string path, string label)
    {
        ValidatePath(path, label);
        if (!File.Exists(path) || !(path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException($"{label}: selecione um arquivo .bat ou .cmd existente.", path);
    }

    public static bool Contains(string parent, string child)
    {
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Links e junções não são aceitos neste caminho: {current}");
    }
}
