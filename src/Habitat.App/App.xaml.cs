using Habitat.Services;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Habitat.Activation;

namespace Habitat;

public partial class App : Application
{
    public const string AppUserModelId = "HABITAT.Desktop";
    private readonly Mutex _singleInstance;
    private readonly bool _ownsInstance;

    private AppServices? _services;
#if !HABITAT_UI_CHECKS
    private ActivationChannel? _activation;
#endif
    public MainWindow? MainWindow { get; private set; }

    public App()
    {
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        _singleInstance = new Mutex(true, "Local\\HABITAT.WinUI", out _ownsInstance);
        UnhandledException += LogUnhandledException;
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
#if HABITAT_UI_CHECKS
        _services = new AppServices(AppDataPaths.Isolated(Path.Combine(AppContext.BaseDirectory, "tasks-ui-data", Guid.NewGuid().ToString("N"))));
        await _services.TaskSession.LoadAsync();
        if (Environment.GetCommandLineArgs().Contains("--window-menu-ui-only"))
        {
            MainWindow = new MainWindow(_services);
            MainWindow.StartWindowMenuChecks();
            return;
        }
        var page = new MainPage(_services);
        var window = new Window { Content = page };
        page.Loaded += async (_, _) => await page.CheckTasksUiAsync(window);
        window.Activate();
        return;
#else
        if (!_ownsInstance)
        {
            await ActivationChannel.SendAsync(ActivationChannel.Name("App"), new(Guid.NewGuid()), 5000);
            Exit();
            return;
        }

        _services = new AppServices(AppDataPaths.Production);
        await _services.TaskSession.LoadAsync();
        MainWindow = new MainWindow(_services);
        _activation = new ActivationChannel(ActivationChannel.Name("App"), request =>
        {
            if (request.Kind != "activate") return Task.FromResult(new ActivationReply(false));
            var queued = MainWindow.DispatcherQueue.TryEnqueue(() => MainWindow.ActivateComposer(request.Text));
            return Task.FromResult(new ActivationReply(queued));
        });
        MainWindow.Closed += async (_, _) =>
        {
            if (_activation is not null) await _activation.DisposeAsync();
        };
        MainWindow.Activate();
        _ = _services.StartAsync();
#endif
    }

    private void LogUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        try
        {
            var directory = _services?.Paths.LocalDirectory ?? Path.Combine(AppContext.BaseDirectory, "startup-diagnostics");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "crash.log"), $"{DateTimeOffset.Now:O}{Environment.NewLine}{args.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
