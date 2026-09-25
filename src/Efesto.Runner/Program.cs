using System.Text;
using Efesto.Core;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length != 2 || args[0] != "--config") { Console.Error.WriteLine("Uso: Efesto.Runner --config arquivo.json"); return 2; }
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var config = new ConfigurationStore(Path.GetFullPath(args[1])).Load();
    Validation.Validate(config);
    var results = new List<BuildResult>();
    foreach (var job in config.Jobs.Where(job => job.IsSelected))
    {
        if (cancellation.IsCancellationRequested) return 130;
        var logs = Path.Combine(config.DestinationPath, "_logs"); Validation.RejectLinks(logs); Directory.CreateDirectory(logs);
        using var writer = new StreamWriter(Path.Combine(logs, $"{job.Name}-{Guid.NewGuid():N}.log"), false, Encoding.UTF8) { AutoFlush = true };
        var gate = new object();
        results.Add(await new BuildService().RunAsync(config, job, line => { lock (gate) { Console.WriteLine($"[{job.Name}] {line}"); writer.WriteLine(line); } }, cancellation.Token));
    }
    if (cancellation.IsCancellationRequested) return 130;
    foreach (var path in await BuildService.CreateArchivesAsync(config, results, cancellation.Token)) Console.WriteLine("ZIP: " + path);
    return results.Any(r => !r.Success) ? 1 : 0;
}
catch (OperationCanceledException) { return 130; }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
