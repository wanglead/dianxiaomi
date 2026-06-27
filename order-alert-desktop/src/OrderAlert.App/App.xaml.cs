using System.IO;
using System.Threading;
using System.Windows;
using OrderAlert.App.Notifications;
using OrderAlert.App.Startup;
using OrderAlert.App.Tray;
using OrderAlert.App.ViewModels;
using OrderAlert.Core.Accounts;
using OrderAlert.Core.Persistence;
using OrderAlert.Core.Chrome;
using OrderAlert.Core.Messaging;
using OrderAlert.Core.Services;

namespace OrderAlert.App;

public partial class App : Application
{
    private const string MutexName = "Local\\OrderAlert.SingleInstance";
    private const string ActivationEventName = "Local\\OrderAlert.Activate";
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _lifetime;
    private TrayIconService? _tray;
    private NativeHostPipeServer? _pipeServer;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                EventWaitHandle.OpenExisting(ActivationEventName).Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The first process is still completing startup.
            }
            Shutdown();
            return;
        }

        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _lifetime = new CancellationTokenSource();

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrderAlert",
            "data");
        var store = new SqliteStore(Path.Combine(dataRoot, "orders.db"));
        await store.InitializeAsync();
        var accountService = new AccountService(store);
        var autoStart = new AutoStartService();
        var extensionDirectory = Path.Combine(AppContext.BaseDirectory, "extension");
        var launcher = new ChromeProfileLauncher(extensionDirectory);
        _pipeServer = new NativeHostPipeServer();
        _ = _pipeServer.RunAsync(_lifetime.Token);
        var orchestrator = new ScanOrchestrator(
            launcher,
            new NativePipeScanBridge(_pipeServer),
            store);
        var actions = new DesktopActions(
            store,
            accountService,
            autoStart,
            launcher,
            orchestrator);
        var viewModel = new MainViewModel(actions)
        {
            Settings = await store.GetSettingsAsync(),
            NextCheckAt = DateTimeOffset.Now.AddMinutes(15)
        };
        foreach (var account in await accountService.ListAsync())
            viewModel.Accounts.Add(account);
        actions.AccountAdded += account => Dispatcher.Invoke(
            () => viewModel.Accounts.Add(account));

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        _tray = new TrayIconService(window, viewModel);
        window.Show();
        _ = ListenForActivationAsync(window, _lifetime.Token);

        if (eventArgs.Args.Contains("--notification-smoke-test"))
        {
            new WindowsAlertService(GetTrayIconForSmokeTest())
                .Show(new AlertSummary(1, 1, 0, 1, 0), viewModel.Settings);
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _lifetime?.Cancel();
        _tray?.Dispose();
        if (_pipeServer is not null)
            _pipeServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _activationEvent?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(eventArgs);
    }

    private async Task ListenForActivationAsync(
        MainWindow window,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Run(() => _activationEvent!.WaitOne(), cancellationToken);
            await Dispatcher.InvokeAsync(() =>
            {
                window.Show();
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Activate();
            });
        }
    }

    private static Hardcodet.Wpf.TaskbarNotification.TaskbarIcon
        GetTrayIconForSmokeTest() =>
        new() { ToolTipText = "订单临期预警测试" };
}
