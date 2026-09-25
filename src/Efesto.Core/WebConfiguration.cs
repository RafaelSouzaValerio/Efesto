using System.Xml;
using System.Xml.Linq;
using Microsoft.Web.XmlTransform;

namespace Efesto.Core;

public static class WebConfiguration
{
    private const string XdtNamespace = "http://schemas.microsoft.com/XML-Document-Transform";

    public static void Validate(CompilationJob job)
    {
        if (!Enum.IsDefined(job.WebConfigMode)) throw new InvalidOperationException("Tratamento de web.config inválido.");
        if (job.WebConfigMode is not (WebConfigMode.Copy or WebConfigMode.Transform)) return;
        Validation.ValidatePath(job.WebConfigPath, $"Configuração de {job.Name}");
        if (!File.Exists(job.WebConfigPath)) throw new FileNotFoundException($"Arquivo de configuração não encontrado: {job.WebConfigPath}");
        Validation.RejectLinks(job.WebConfigPath);
        using var reader = OpenXml(job.WebConfigPath);
        var document = XDocument.Load(reader);
        if (document.Root?.Name != "configuration") throw new InvalidDataException("O arquivo deve ter <configuration> como elemento raiz.");
        var hasTransform = document.Descendants().Attributes().Any(a => a.Name.NamespaceName == XdtNamespace);
        if (job.WebConfigMode == WebConfigMode.Copy && hasTransform)
            throw new InvalidDataException("O arquivo contém instruções XDT. Selecione Aplicar transformação, ou informe um web.config completo para copiar.");
        if (job.WebConfigMode == WebConfigMode.Transform)
        {
            if (!document.Descendants().Attributes().Any(a => a.Name == XName.Get("Transform", XdtNamespace)))
                throw new InvalidDataException("O arquivo de transformação deve conter pelo menos uma instrução xdt:Transform.");
            if (!File.Exists(Path.Combine(job.SourcePath, "web.config")))
                throw new FileNotFoundException("Para aplicar uma transformação, a pasta dos fontes precisa conter um web.config base.");
        }
    }

    public static void Apply(CompilationJob job, string stagedSource, Action<string> log)
    {
        Validate(job);
        var target = Path.Combine(stagedSource, "web.config");
        switch (job.WebConfigMode)
        {
            case WebConfigMode.Copy:
                log("Copiando o web.config completo informado para os fontes temporários...");
                if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                File.Copy(job.WebConfigPath, target, overwrite: true);
                return;
            case WebConfigMode.Transform:
                ApplyTransform(job.WebConfigPath, target, log);
                return;
            case WebConfigMode.KeepOriginal:
                log("Mantendo o web.config original, sem transformação.");
                return;
            default:
                var automatic = Path.Combine(stagedSource, "web.publish.config");
                if (job.Mode == CompilationMode.AspNet && File.Exists(automatic)) ApplyTransform(automatic, target, log);
                return;
        }
    }

    private static XmlReader OpenXml(string path) => XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

    private static void ApplyTransform(string transformPath, string target, Action<string> log)
    {
        log("Aplicando transformação XDT ao web.config temporário: " + Path.GetFileName(transformPath));
        using var document = new XmlTransformableDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = OpenXml(target)) document.Load(reader);
        using var transformReader = OpenXml(transformPath);
        var transformDocument = XDocument.Load(transformReader);
        using var transformation = new XmlTransformation(transformDocument.ToString(), isTransformAFile: false, logger: null);
        if (!transformation.Apply(document)) throw new InvalidOperationException("Falha ao aplicar a transformação do web.config.");
        File.SetAttributes(target, FileAttributes.Normal);
        document.Save(target);
    }
}
