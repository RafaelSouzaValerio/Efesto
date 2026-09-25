using System.Text;

namespace Efesto.Core;

internal static class WorkspaceCleanup
{
    private const string TicketSuffix = ".cleanup";
    private const string TicketContents = "Efesto completed workspace v1";

    // A ticket is created only after the compiler has stopped. Never sweep directories
    // by prefix alone: another application/instance may still be using its workspace.
    public static async Task DeleteAsync(string root, string workspace, Action<string> log)
    {
        ValidateWorkspace(root, workspace);
        var ticket = workspace + TicketSuffix;
        Validation.RejectLinks(ticket);
        var cleanupStarted = false;
        var ticketCreated = false;
        try
        {
            using var lease = new FileStream(ticket, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            ticketCreated = true;
            lease.Write(Encoding.UTF8.GetBytes(TicketContents)); lease.Flush(flushToDisk: true);
            cleanupStarted = true;
            await DeleteWithRetriesAsync(root, workspace, log);
        }
        catch (Exception ex) when (!cleanupStarted && ex is IOException or UnauthorizedAccessException)
        {
            // A recovery record is useful, but inability to write one must never
            // prevent us from attempting to remove the actual source copy now.
            await DeleteWithRetriesAsync(root, workspace, log);
        }
        if (ticketCreated) File.Delete(ticket);
    }

    public static async Task<IReadOnlyList<string>> RecoverAsync(string root, Action<string> log)
    {
        var warnings = new List<string>();
        Validation.RejectLinks(root);
        if (!Directory.Exists(root)) return warnings;
        foreach (var ticket in Directory.GetFiles(root, ".compiler-*" + TicketSuffix))
        {
            var workspace = ticket[..^TicketSuffix.Length];
            try
            {
                ValidateWorkspace(root, workspace);
                Validation.RejectLinks(ticket);
                FileStream lease;
                try { lease = new FileStream(ticket, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 2 or 3 or 32 or 33) { continue; }
                using (lease)
                {
                    using var reader = new StreamReader(lease, Encoding.UTF8, leaveOpen: true);
                    if (lease.Length != Encoding.UTF8.GetByteCount(TicketContents) || await reader.ReadToEndAsync() != TicketContents)
                        continue;
                    log("Retomando limpeza pendente: " + workspace);
                    await DeleteWithRetriesAsync(root, workspace, log);
                }
                File.Delete(ticket);
            }
            catch (Exception ex)
            {
                var warning = $"Limpeza pendente em {workspace}: {ex.Message}";
                warnings.Add(warning); log(warning);
            }
        }
        return warnings;
    }

    private static void ValidateWorkspace(string root, string workspace)
    {
        var name = Path.GetFileName(workspace);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(workspace)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(".compiler-", StringComparison.Ordinal) || !Guid.TryParseExact(name[10..], "N", out _))
            throw new IOException("Pasta temporária fora do destino esperado.");
        Validation.RejectLinks(workspace);
    }

    private static async Task DeleteWithRetriesAsync(string root, string workspace, Action<string> log)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                ValidateWorkspace(root, workspace);
                if (Directory.Exists(workspace)) DeleteTree(workspace);
                return;
            }
            catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
            {
                // Cleanup deliberately ignores build cancellation.
                try { log($"Aguardando liberação dos temporários para excluir (tentativa {attempt + 2}/5)..."); }
                catch { /* A broken log sink must not prevent cleanup. */ }
                await Task.Delay(200 * (1 << attempt));
            }
        }
    }

    private static void DeleteTree(string directory)
    {
        Validation.RejectLinks(directory);
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            Validation.RejectLinks(file);
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }
        foreach (var child in Directory.EnumerateDirectories(directory)) DeleteTree(child);
        File.SetAttributes(directory, FileAttributes.Normal);
        Directory.Delete(directory);
    }
}
