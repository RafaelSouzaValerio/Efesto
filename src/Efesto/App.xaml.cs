using Microsoft.UI.Xaml;

namespace Efesto;

public partial class App : Application
{
    private Window? window;
    public App()
    {
        UnhandledException += (_, args) => RecordError(args.Exception);
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception ex) { RecordError(ex); throw; }
    }
    private static void RecordError(Exception exception)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApplicationCompiler.WinUI");
            Directory.CreateDirectory(path);
            File.AppendAllText(Path.Combine(path, "startup-errors.log"), $"{DateTime.Now:O}\n{exception}\n");
        }
        catch { }
    }
}
