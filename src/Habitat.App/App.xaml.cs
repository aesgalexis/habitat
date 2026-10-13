using Habitat.Services;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace Habitat;

public partial class App : Application
{
    public const string AppUserModelId = "HABITAT.Desktop";
    private readonly Mutex _singleInstance;
    private readonly bool _ownsInstance;

    public BridgeService Bridge { get; } = new();
    public MainWindow? MainWindow { get; private set; }

    public App()
    {
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        _singleInstance = new Mutex(true, "Local\\HABITAT.WinUI", out _ownsInstance);
        UnhandledException += LogUnhandledException;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!_ownsInstance)
        {
            Exit();
            return;
        }

        MainWindow = new MainWindow(Bridge);
        MainWindow.Activate();
        _ = Bridge.StartAsync();
    }

    private static void LogUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HABITAT");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "crash.log"), $"{DateTimeOffset.Now:O}{Environment.NewLine}{args.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
