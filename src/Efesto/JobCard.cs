using Efesto.Core;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Efesto;

internal enum PathPickerKind { Folder, Batch, Configuration }

internal sealed class JobCard : UserControl
{
    private readonly TextBox name = new() { Header = "Nome da aplicação", PlaceholderText = "Ex.: Condo" };
    private readonly TextBox source = new() { Header = "Pasta dos fontes", PlaceholderText = "Caminho da aplicação original" };
    private readonly ComboBox mode = new() { Header = "Tipo de compilação", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox batch = new() { Header = "BAT personalizado", PlaceholderText = @"C:\scripts\compilar.bat" };
    private readonly ComboBox webConfigMode = new() { Header = "web.config", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox webConfigPath = new() { Header = "Arquivo de configuração", PlaceholderText = @"C:\configs\web.config" };
    private readonly TextBlock status = new() { Text = "Aguardando", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox logBox = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Height = 200 };
    private readonly StackPanel fields = new() { Spacing = 12 };
    private readonly ContentControl editingFields = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Button remove = new() { Content = "Remover" };
    private readonly CheckBox individualZip = new() { Content = "Gerar ZIP desta aplicação", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox selected = new() { Content = "Compilar esta aplicação", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button cancel = new() { Content = "Cancelar", Visibility = Visibility.Collapsed };
    private readonly Expander application = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock applicationName = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel logPanel = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly Grid batchRow;
    private bool removable;
    private readonly Stopwatch elapsed = new();
    private string phase = "Na fila";
    public CancellationTokenSource? Cancellation { get; private set; }
    public event Action? Changed;
    public event Action<JobCard>? RemoveRequested;
    public CompilationJob Snapshot() => new() { Name = name.Text.Trim(), SourcePath = source.Text.Trim(), Mode = (CompilationMode)mode.SelectedIndex, BatchPath = batch.Text.Trim(), WebConfigMode = (WebConfigMode)webConfigMode.SelectedIndex, WebConfigPath = webConfigPath.Text.Trim(), IsExpanded = application.IsExpanded, CreateZip = individualZip.IsChecked == true, IsSelected = selected.IsChecked == true };

    public JobCard(CompilationJob job, Func<PathPickerKind, Task<string?>> pick)
    {
        var body = new StackPanel { Spacing = 14 };
        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var summary = new StackPanel { Spacing = 4 };
        summary.Children.Add(applicationName); summary.Children.Add(status);
        header.Children.Add(summary); Grid.SetColumn(selected, 1); header.Children.Add(selected); Grid.SetColumn(individualZip, 2); header.Children.Add(individualZip); Grid.SetColumn(cancel, 3); header.Children.Add(cancel); Grid.SetColumn(remove, 4); header.Children.Add(remove);
        selected.IsChecked = job.IsSelected;
        status.Text = job.IsSelected ? "Aguardando" : "Não selecionada";
        void SelectionChanged()
        {
            status.Text = selected.IsChecked == true ? "Aguardando" : "Não selecionada";
            status.ClearValue(TextBlock.ForegroundProperty);
            Changed?.Invoke();
        }
        selected.Checked += (_, _) => SelectionChanged();
        selected.Unchecked += (_, _) => SelectionChanged();
        individualZip.IsChecked = job.CreateZip;
        individualZip.Checked += (_, _) => Changed?.Invoke();
        individualZip.Unchecked += (_, _) => Changed?.Invoke();
        application.Header = header;
        var top = new Grid { ColumnSpacing = 16 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mode.Items.Add("Site ASP.NET (.NET Framework)"); mode.Items.Add("BAT personalizado");
        top.Children.Add(name); Grid.SetColumn(mode, 1); top.Children.Add(mode);
        fields.Children.Add(top);
        fields.Children.Add(PathRow(source, PathPickerKind.Folder, pick));
        batchRow = PathRow(batch, PathPickerKind.Batch, pick);
        fields.Children.Add(batchRow);
        var help = new TextBlock { Text = "O BAT recebe origem, saída e nome como argumentos. Também pode usar %AC_SOURCE%, %AC_OUTPUT% e %AC_NAME%.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .7 };
        fields.Children.Add(help);
        foreach (var label in new[] { "Padrão do projeto", "Copiar arquivo completo", "Aplicar transformação (XDT)", "Manter web.config original" }) webConfigMode.Items.Add(label);
        fields.Children.Add(webConfigMode);
        var configRow = PathRow(webConfigPath, PathPickerKind.Configuration, pick);
        fields.Children.Add(configRow);
        var configHelp = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .7 };
        fields.Children.Add(configHelp);
        void RefreshConfig()
        {
            configRow.Visibility = webConfigMode.SelectedIndex is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
            webConfigPath.Header = webConfigMode.SelectedIndex == 2 ? "Arquivo de transformação XDT" : "Arquivo web.config completo";
            configHelp.Text = webConfigMode.SelectedIndex switch
            {
                1 => "Substitui o web.config na cópia temporária antes de compilar. Não aplica web.publish.config.",
                2 => "Aplica o XDT ao web.config da cópia temporária antes de compilar. O arquivo base deve existir nos fontes.",
                3 => "Usa o web.config dos fontes sem aplicar transformações.",
                _ => mode.SelectedIndex == 0 ? "Aplica web.publish.config automaticamente, se existir nos fontes." : "Usa os fontes como estão. Selecione Copiar ou Transformar para preparar o web.config antes do BAT."
            };
        }
        mode.SelectionChanged += (_, _) => { batchRow.Visibility = help.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; RefreshConfig(); Changed?.Invoke(); };
        webConfigMode.SelectionChanged += (_, _) => { RefreshConfig(); Changed?.Invoke(); };
        name.Text = job.Name; source.Text = job.SourcePath; batch.Text = job.BatchPath; mode.SelectedIndex = (int)job.Mode;
        webConfigPath.Text = job.WebConfigPath; webConfigMode.SelectedIndex = (int)job.WebConfigMode;
        editingFields.Content = fields;
        body.Children.Add(editingFields);
        ScrollViewer.SetHorizontalScrollBarVisibility(logBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(logBox, ScrollBarVisibility.Auto);
        logPanel.Children.Add(new TextBlock { Text = "Log da compilação", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        logPanel.Children.Add(logBox);
        body.Children.Add(logPanel);
        application.Content = body;
        application.IsExpanded = job.IsExpanded;
        applicationName.Text = string.IsNullOrWhiteSpace(job.Name) ? "Nova aplicação" : job.Name;
        application.Expanding += (_, _) => Changed?.Invoke();
        application.Collapsed += (_, _) => Changed?.Invoke();
        application.SizeChanged += (_, _) => header.Width = Math.Max(200, application.ActualWidth - 100);
        name.TextChanged += (_, _) => applicationName.Text = string.IsNullOrWhiteSpace(name.Text) ? "Nova aplicação" : name.Text;
        Content = application;
        foreach (var box in new[] { name, source, batch, webConfigPath }) box.TextChanged += (_, _) => Changed?.Invoke();
        remove.Click += (_, _) => RemoveRequested?.Invoke(this);
        cancel.Click += (_, _) => { Cancellation?.Cancel(); cancel.IsEnabled = false; RefreshElapsed(); };
    }

    private static Grid PathRow(TextBox box, PathPickerKind kind, Func<PathPickerKind, Task<string?>> pick)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var button = new Button { Content = "Procurar", VerticalAlignment = VerticalAlignment.Bottom };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, kind switch { PathPickerKind.Batch => "Selecionar BAT personalizado", PathPickerKind.Configuration => "Selecionar arquivo de configuração", _ => "Selecionar pasta dos fontes" });
        button.Click += async (_, _) => { var path = await pick(kind); if (path is not null) box.Text = path; };
        grid.Children.Add(box); Grid.SetColumn(button, 1); grid.Children.Add(button); return grid;
    }

    public void SetRemovable(bool value) { removable = value; remove.IsEnabled = value && editingFields.IsEnabled; }
    public void SetEditing(bool enabled) { editingFields.IsEnabled = selected.IsEnabled = individualZip.IsEnabled = enabled; remove.IsEnabled = enabled && removable; }
    public void Start(CancellationToken parent)
    {
        elapsed.Reset(); phase = "Na fila";
        logBox.Text = ""; logPanel.Visibility = Visibility.Visible; status.Text = phase; status.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);
        cancel.Visibility = Visibility.Visible; cancel.IsEnabled = true;
    }
    public void BeginExecution()
    {
        elapsed.Restart(); phase = "Preparando"; RefreshElapsed();
    }
    public void ReportStage(BuildStage stage)
    {
        if (Cancellation is null) return;
        phase = stage switch
        {
            BuildStage.CopyingSources => "Copiando fontes",
            BuildStage.PreparingConfiguration => "Preparando configuração",
            BuildStage.Compiling => "Compilando",
            BuildStage.ExecutingBatch => "Executando BAT",
            BuildStage.Publishing => "Organizando publicação",
            BuildStage.CleaningWorkspace => "Limpando temporários",
            _ => "Preparando"
        };
        RefreshElapsed();
    }
    public void RefreshElapsed()
    {
        if (Cancellation is null) return;
        var label = Cancellation.IsCancellationRequested ? "Cancelando" : phase;
        var text = elapsed.IsRunning ? $"{label} · {elapsed.Elapsed:hh\\:mm\\:ss}" : label;
        if (status.Text != text) status.Text = text;
    }
    public void Append(string text)
    {
        logPanel.Visibility = Visibility.Visible;
        var value = logBox.Text + text + Environment.NewLine;
        logBox.Text = value.Length > 60000 ? "[Trecho anterior disponível no arquivo de log]\n" + value[^50000..] : value;
    }
    public void Finish(BuildResult result)
    {
        elapsed.Stop();
        var duration = elapsed.Elapsed > result.Duration ? elapsed.Elapsed : result.Duration;
        status.Text = $"{(result.Success ? "Concluído" : result.Cancelled ? "Cancelado" : "Falha")} · {duration:hh\\:mm\\:ss}";
        if (result.CleanupWarning is not null) status.Text += " · Limpeza pendente";
        status.Foreground = new SolidColorBrush(result.Success && result.CleanupWarning is null ? Windows.UI.Color.FromArgb(255, 20, 135, 80) : Windows.UI.Color.FromArgb(255, 185, 80, 35));
        cancel.Visibility = Visibility.Collapsed; Cancellation?.Dispose(); Cancellation = null;
        if (!result.Success || result.CleanupWarning is not null) application.IsExpanded = true;
    }
}
