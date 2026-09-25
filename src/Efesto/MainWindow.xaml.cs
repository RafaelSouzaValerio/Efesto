using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Efesto.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Efesto;

public sealed partial class MainWindow : Window
{
    private readonly List<JobCard> cards = [];
    private readonly ConfigurationStore store;
    private readonly DispatcherQueueTimer saveTimer;
    private readonly DispatcherQueueTimer logTimer;
    private readonly ConcurrentQueue<(JobCard Card, string Line)> pendingLogs = new();
    private readonly ConcurrentQueue<(JobCard Card, BuildStage Stage)> pendingStages = new();
    private CancellationTokenSource? runCancellation;
    private Task? runningTask;
    private bool loaded;
    private bool saveAllowed = true;
    private bool closing;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "efesto.ico"));
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(1180, workArea.Width - 32);
        var height = Math.Min(900, workArea.Height - 32);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height));
        // A separate configuration path allows UI checks without changing a user's settings.
        store = new ConfigurationStore(Environment.GetEnvironmentVariable("APPLICATION_COMPILER_SETTINGS"));
        saveTimer = DispatcherQueue.CreateTimer(); saveTimer.Interval = TimeSpan.FromMilliseconds(650); saveTimer.IsRepeating = false;
        saveTimer.Tick += (_, _) => SaveConfiguration();
        logTimer = DispatcherQueue.CreateTimer(); logTimer.Interval = TimeSpan.FromMilliseconds(150);
        logTimer.Tick += (_, _) => { FlushLogs(); foreach (var card in cards) card.RefreshElapsed(); };
        CompilerConfiguration config;
        try { config = store.Load(); }
        catch (Exception ex)
        {
            config = new(); saveAllowed = false;
            ShowNotice("Não foi possível carregar a configuração", ex.Message + " O arquivo original foi preservado. Corrija-o e reabra o aplicativo: " + store.FilePath, InfoBarSeverity.Error);
        }
        var detectOnLoad = saveAllowed && !File.Exists(store.FilePath);
        DevCmdBox.Text = config.DeveloperCommandPath; DestinationBox.Text = config.DestinationPath; ZipCheck.IsChecked = config.CreateZip;
        foreach (var job in config.Jobs) AddCard(job);
        loaded = true; UpdateCount();
        SaveStatus.Text = File.Exists(store.FilePath) ? "Última configuração carregada" : "Alterações salvas automaticamente";
        Root.Loaded += async (_, _) => { if (detectOnLoad) { detectOnLoad = false; await DetectDeveloperCommandAsync(automatic: true); } };
        AppWindow.Closing += async (_, args) =>
        {
            if (closing) return;
            if (runningTask is { IsCompleted: false })
            {
                args.Cancel = true; runCancellation?.Cancel();
                await runningTask;
                SaveConfiguration(); closing = true; Close();
            }
            else SaveConfiguration();
        };
        Closed += (_, _) => { closing = true; saveTimer.Stop(); logTimer.Stop(); };
    }

    private async void DetectDevCmd(object sender, RoutedEventArgs args) => await DetectDeveloperCommandAsync(automatic: false);

    private async Task DetectDeveloperCommandAsync(bool automatic)
    {
        DetectButton.IsEnabled = false; DevCmdStatus.Visibility = Visibility.Visible; DevCmdStatus.Text = "Localizando o Developer Command Prompt...";
        try
        {
            var path = await DeveloperCommandLocator.FindAsync();
            if (closing) return;
            if (path is not null && runCancellation is null && (!automatic || string.IsNullOrWhiteSpace(DevCmdBox.Text)))
            {
                DevCmdBox.Text = path;
                DevCmdStatus.Text = "Developer Command Prompt encontrado. Você pode alterar o caminho, se necessário.";
            }
            else DevCmdStatus.Text = path is null ? "Não foi encontrado um VsDevCmd.bat. Use Procurar para informar o caminho." : "O caminho informado foi mantido.";
        }
        catch (Exception ex) { if (!closing) DevCmdStatus.Text = "Não foi possível detectar: " + ex.Message; }
        finally { if (!closing) DetectButton.IsEnabled = true; }
    }

    private CompilerConfiguration Snapshot() => new() { DeveloperCommandPath = DevCmdBox.Text.Trim(), DestinationPath = DestinationBox.Text.Trim(), CreateZip = ZipCheck.IsChecked == true, Jobs = cards.Select(c => c.Snapshot()).ToList() };
    private void ConfigurationChanged(object sender, RoutedEventArgs args) => ScheduleSave();
    private void ScheduleSave()
    {
        if (!loaded) return;
        SaveStatus.Text = "Salvando alterações..."; saveTimer.Stop(); saveTimer.Start();
    }
    private bool SaveConfiguration()
    {
        if (!loaded) return true;
        if (!saveAllowed) { SaveStatus.Text = "Configuração não salva: verifique o aviso"; return false; }
        try { store.Save(Snapshot()); SaveStatus.Text = "Configuração salva"; return true; }
        catch (Exception ex) { SaveStatus.Text = "Falha ao salvar"; ShowNotice("Configuração não salva", ex.Message, InfoBarSeverity.Error); return false; }
    }
    private void AddCard(CompilationJob job)
    {
        var card = new JobCard(job, PickPathAsync);
        card.Changed += () => { UpdateCount(); ScheduleSave(); };
        card.RemoveRequested += item => { if (cards.Count == 1) return; cards.Remove(item); JobsPanel.Children.Remove(item); UpdateCount(); ScheduleSave(); };
        cards.Add(card); JobsPanel.Children.Add(card);
    }
    private void AddJob(object sender, RoutedEventArgs args)
    {
        var number = cards.Count + 1;
        while (cards.Any(c => c.Snapshot().Name.Equals("Aplicacao" + number, StringComparison.OrdinalIgnoreCase))) number++;
        AddCard(new() { Name = "Aplicacao" + number }); UpdateCount(); ScheduleSave();
    }
    private void UpdateCount()
    {
        var selected = cards.Count(card => card.Snapshot().IsSelected);
        CountText.Text = (cards.Count == 1 ? "1 aplicação configurada" : $"{cards.Count} aplicações configuradas") + $" · {selected} selecionada(s)";
        foreach (var card in cards) card.SetRemovable(cards.Count > 1);
    }
    private async Task<string?> PickPathAsync(PathPickerKind kind)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (kind != PathPickerKind.Folder)
            {
                var picker = new FileOpenPicker();
                foreach (var extension in kind == PathPickerKind.Batch ? new[] { ".bat", ".cmd" } : new[] { ".config", ".xml", ".xdt" }) picker.FileTypeFilter.Add(extension);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                return (await picker.PickSingleFileAsync())?.Path;
            }
            var folder = new FolderPicker(); folder.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(folder, hwnd);
            return (await folder.PickSingleFolderAsync())?.Path;
        }
        catch (Exception ex) { ShowNotice("Não foi possível abrir o seletor", ex.Message, InfoBarSeverity.Error); return null; }
    }
    private async void BrowseDevCmd(object sender, RoutedEventArgs args) { var path = await PickPathAsync(PathPickerKind.Batch); if (path is not null) DevCmdBox.Text = path; }
    private async void BrowseDestination(object sender, RoutedEventArgs args) { var path = await PickPathAsync(PathPickerKind.Folder); if (path is not null) DestinationBox.Text = path; }
    private void OpenDestination(object sender, RoutedEventArgs args)
    {
        try
        {
            if (!Directory.Exists(DestinationBox.Text)) throw new DirectoryNotFoundException("A pasta de saída ainda não existe.");
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetFullPath(DestinationBox.Text)}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ShowNotice("Não foi possível abrir a saída", ex.Message, InfoBarSeverity.Warning); }
    }
    private async void RunClicked(object sender, RoutedEventArgs args)
    {
        if (runCancellation is not null) { runCancellation.Cancel(); RunButton.IsEnabled = false; RunStatus.Text = "Cancelando compilações..."; return; }
        var config = Snapshot();
        try { Validation.Validate(config); }
        catch (Exception ex) { ShowNotice("Revise a configuração", ex.Message, InfoBarSeverity.Warning); return; }
        if (!SaveConfiguration()) return;
        runningTask = RunAllAsync(config);
        await runningTask;
    }
    private async Task RunAllAsync(CompilerConfiguration config)
    {
        runCancellation = new();
        var token = runCancellation.Token;
        var selectedCards = cards.Select((card, index) => (Card: card, Job: config.Jobs[index])).Where(item => item.Job.IsSelected).ToList();
        EnvironmentPanel.IsEnabled = AddButton.IsEnabled = ExportButton.IsEnabled = false;
        foreach (var card in cards) card.SetEditing(false);
        foreach (var item in selectedCards) item.Card.Start(token);
        Notice.IsOpen = false; RunButton.Content = "Cancelar todas"; RunProgress.Visibility = Visibility.Visible; RunProgress.Value = 0;
        RunStatus.Text = "Compilações em andamento..."; logTimer.Start();
        var completed = 0;
        var results = new List<BuildResult>();
        try
        {
            using var limit = new SemaphoreSlim(2);
            var tasks = selectedCards.Select(async item =>
            {
                var card = item.Card;
                var job = item.Job;
                var jobToken = card.Cancellation!.Token;
                BuildResult result;
                var acquired = false;
                try
                {
                    await limit.WaitAsync(jobToken); acquired = true;
                    card.BeginExecution();
                    var logDirectory = Path.Combine(config.DestinationPath, "_logs");
                    Validation.RejectLinks(logDirectory); Directory.CreateDirectory(logDirectory);
                    var logPath = Path.Combine(logDirectory, $"{job.Name}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
                    using var writer = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                    var gate = new object();
                    result = await Task.Run(() => new BuildService().RunAsync(config, job, line =>
                    {
                        var entry = $"[{DateTime.Now:HH:mm:ss}] {line}";
                        lock (gate) writer.WriteLine(entry);
                        pendingLogs.Enqueue((card, entry));
                    }, jobToken, stage => pendingStages.Enqueue((card, stage))));
                    pendingLogs.Enqueue((card, "Log completo: " + logPath));
                }
                catch (OperationCanceledException) { result = new(job.Name, false, true, null, TimeSpan.Zero, null); }
                catch (Exception ex) { result = new(job.Name, false, false, null, TimeSpan.Zero, ex.Message); pendingLogs.Enqueue((card, ex.Message)); }
                finally { if (acquired) limit.Release(); }
                FlushLogs(); card.Finish(result); results.Add(result); completed++;
                RunProgress.Value = 100d * completed / selectedCards.Count; RunStatus.Text = $"{completed} de {selectedCards.Count} compilações finalizadas";
            }).ToArray();
            await Task.WhenAll(tasks);
            var successful = results.Count(r => r.Success);
            var summary = $"{successful} concluída(s), {results.Count(r => r.Cancelled)} cancelada(s), {results.Count(r => !r.Success && !r.Cancelled)} com falha.";
            var wantsArchives = config.CreateZip || config.Jobs.Any(job => job.IsSelected && job.CreateZip);
            if (wantsArchives && !token.IsCancellationRequested)
            {
                RunStatus.Text = "Compactando publicações...";
                var archives = await BuildService.CreateArchivesAsync(config, results, token);
                summary += archives.Count > 0 ? $" {archives.Count} ZIP(s) criado(s) na pasta de saída." : " ZIP não gerado porque nenhuma publicação elegível foi concluída.";
            }
            else if (wantsArchives) summary += " ZIP não gerado porque houve cancelamento.";
            var cleanupPending = results.Any(r => r.CleanupWarning is not null);
            if (cleanupPending) summary += " Há cópias temporárias com limpeza pendente. Consulte os caminhos e os avisos de limpeza nos logs.";
            ShowNotice("Processamento finalizado", summary, successful == results.Count && !cleanupPending ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            RunStatus.Text = cleanupPending ? "Processamento finalizado com limpeza pendente. Consulte os logs." : successful == results.Count ? "Todas as compilações foram concluídas." : "Processamento finalizado. Consulte os logs.";
        }
        catch (OperationCanceledException) { RunStatus.Text = "Compactação cancelada. As publicações concluídas foram preservadas."; }
        catch (Exception ex) { ShowNotice("Falha no processamento", ex.Message, InfoBarSeverity.Error); RunStatus.Text = "Processamento interrompido."; }
        finally
        {
            logTimer.Stop(); FlushLogs(); runCancellation.Dispose(); runCancellation = null;
            EnvironmentPanel.IsEnabled = AddButton.IsEnabled = ExportButton.IsEnabled = RunButton.IsEnabled = true;
            foreach (var card in cards) card.SetEditing(true);
            RunButton.Content = "Compilar"; RunProgress.Visibility = Visibility.Collapsed;
        }
    }
    private void FlushLogs()
    {
        while (pendingStages.TryDequeue(out var update)) update.Card.ReportStage(update.Stage);
        var grouped = new Dictionary<JobCard, StringBuilder>();
        while (pendingLogs.TryDequeue(out var entry)) { if (!grouped.TryGetValue(entry.Card, out var builder)) grouped[entry.Card] = builder = new(); builder.AppendLine(entry.Line); }
        foreach (var entry in grouped) entry.Key.Append(entry.Value.ToString().TrimEnd());
    }
    private async void ExportBatch(object sender, RoutedEventArgs args)
    {
        try
        {
            var config = Snapshot(); Validation.Validate(config);
            var destination = await PickPathAsync(PathPickerKind.Folder); if (destination is null) return;
            var path = BatchExporter.Export(config, destination, Path.Combine(AppContext.BaseDirectory, "runner", "Efesto.Runner.exe"));
            SaveConfiguration(); ShowNotice("BAT gerado", path + " — executa a configuração atual com o mesmo motor de compilação.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { ShowNotice("Não foi possível gerar o BAT", ex.Message, InfoBarSeverity.Error); }
    }
    private void ShowNotice(string title, string message, InfoBarSeverity severity) { Notice.Title = title; Notice.Message = message; Notice.Severity = severity; Notice.IsOpen = true; }
}
