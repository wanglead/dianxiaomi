using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrderAlert.Core.Models;

namespace OrderAlert.App.ViewModels;

public sealed record OrderItemViewModel(
    PlatformKind Platform,
    Guid AccountId,
    string StoreName,
    string OrderId,
    string RawStatus,
    DateTimeOffset? AssessmentAt,
    int? ShippingSeconds,
    RiskLevel Risk,
    DateTimeOffset LastCheckedAt,
    string SourceUrl);

public interface IOrderAlertActions
{
    Task CheckNowAsync(CancellationToken cancellationToken);
    Task OpenOrderAsync(OrderItemViewModel order);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken);
    Task AddDianxiaomiAccountAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
    Task AddAliExpressAccountAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
    Task ReloginAsync(StoreAccount account, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class MainViewModel : ObservableObject
{
    private readonly IOrderAlertActions _actions;
    private PlatformKind? _selectedPlatform;
    private Guid? _selectedAccountId;
    private RiskLevel? _selectedRisk;
    private string _keyword = "";
    private bool _isChecking;
    private string _runState = "监控中";
    private DateTimeOffset? _nextCheckAt;
    private AppSettings _settings = AppSettings.Default;
    private StoreAccount? _selectedAccount;
    private string? _lastError;

    public MainViewModel(IOrderAlertActions actions)
    {
        _actions = actions;
        Orders.CollectionChanged += OnCollectionChanged;
        Accounts.CollectionChanged += OnCollectionChanged;
        CheckNowCommand = new AsyncRelayCommand(CheckNowAsync, () => !IsChecking);
        OpenOrderCommand = new AsyncRelayCommand<OrderItemViewModel>(
            order => order is null ? Task.CompletedTask : _actions.OpenOrderAsync(order));
        AddDianxiaomiAccountCommand = new AsyncRelayCommand(
            cancellationToken => ExecuteAccountActionAsync(
                () => _actions.AddDianxiaomiAccountAsync(cancellationToken)));
        AddAliExpressAccountCommand = new AsyncRelayCommand(
            cancellationToken => ExecuteAccountActionAsync(
                () => _actions.AddAliExpressAccountAsync(cancellationToken)));
        ReloginCommand = new AsyncRelayCommand(
            cancellationToken => ExecuteAccountActionAsync(
                () => SelectedAccount is null
                    ? Task.CompletedTask
                    : _actions.ReloginAsync(SelectedAccount, cancellationToken)));
        SaveSettingsCommand = new AsyncRelayCommand(
            cancellationToken => SaveSettingsAsync(Settings, cancellationToken));
    }

    public ObservableCollection<OrderItemViewModel> Orders { get; } = [];
    public ObservableCollection<StoreAccount> Accounts { get; } = [];

    public IAsyncRelayCommand CheckNowCommand { get; }
    public IAsyncRelayCommand<OrderItemViewModel> OpenOrderCommand { get; }
    public IAsyncRelayCommand AddDianxiaomiAccountCommand { get; }
    public IAsyncRelayCommand AddAliExpressAccountCommand { get; }
    public IAsyncRelayCommand ReloginCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }

    public IEnumerable<OrderItemViewModel> FilteredOrders => Orders.Where(order =>
        (!SelectedPlatform.HasValue || order.Platform == SelectedPlatform)
        && (!SelectedAccountId.HasValue || order.AccountId == SelectedAccountId)
        && (!SelectedRisk.HasValue || order.Risk == SelectedRisk)
        && (string.IsNullOrWhiteSpace(Keyword)
            || order.OrderId.Contains(Keyword, StringComparison.OrdinalIgnoreCase)
            || order.StoreName.Contains(Keyword, StringComparison.OrdinalIgnoreCase)));

    public int DueSoonCount => Orders.Count(
        order => order.Risk is RiskLevel.DueSoon or RiskLevel.Critical);
    public int CriticalCount => Orders.Count(order => order.Risk == RiskLevel.Critical);
    public int OverdueCount => Orders.Count(order => order.Risk == RiskLevel.Overdue);
    public int OnlineAccountCount => Accounts.Count(
        account => account.IsEnabled && string.IsNullOrEmpty(account.LastError));

    public PlatformKind? SelectedPlatform
    {
        get => _selectedPlatform;
        set => SetFilter(ref _selectedPlatform, value);
    }

    public Guid? SelectedAccountId
    {
        get => _selectedAccountId;
        set => SetFilter(ref _selectedAccountId, value);
    }

    public RiskLevel? SelectedRisk
    {
        get => _selectedRisk;
        set => SetFilter(ref _selectedRisk, value);
    }

    public string Keyword
    {
        get => _keyword;
        set => SetFilter(ref _keyword, value ?? "");
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (!SetProperty(ref _isChecking, value)) return;
            CheckNowCommand.NotifyCanExecuteChanged();
        }
    }

    public string RunState
    {
        get => _runState;
        set => SetProperty(ref _runState, value);
    }

    public DateTimeOffset? NextCheckAt
    {
        get => _nextCheckAt;
        set => SetProperty(ref _nextCheckAt, value);
    }

    public AppSettings Settings
    {
        get => _settings;
        set
        {
            if (!SetProperty(ref _settings, value)) return;
            OnPropertyChanged(nameof(MonitoringStart));
            OnPropertyChanged(nameof(MonitoringEnd));
            OnPropertyChanged(nameof(CheckIntervalMinutes));
            OnPropertyChanged(nameof(RepeatIntervalMinutes));
            OnPropertyChanged(nameof(PopupEnabled));
            OnPropertyChanged(nameof(SoundEnabled));
            OnPropertyChanged(nameof(LoginFailureNotificationEnabled));
            OnPropertyChanged(nameof(AutoStartEnabled));
        }
    }

    public string MonitoringStart
    {
        get => Settings.MonitoringStart.ToString("HH:mm");
        set
        {
            if (TimeOnly.TryParse(value, out var parsed))
                Settings = Settings with { MonitoringStart = parsed };
        }
    }

    public string MonitoringEnd
    {
        get => Settings.MonitoringEnd.ToString("HH:mm");
        set
        {
            if (TimeOnly.TryParse(value, out var parsed))
                Settings = Settings with { MonitoringEnd = parsed };
        }
    }

    public int CheckIntervalMinutes
    {
        get => Settings.CheckIntervalMinutes;
        set => Settings = Settings with { CheckIntervalMinutes = value };
    }

    public int RepeatIntervalMinutes
    {
        get => Settings.RepeatIntervalMinutes;
        set => Settings = Settings with { RepeatIntervalMinutes = value };
    }

    public bool PopupEnabled
    {
        get => Settings.PopupEnabled;
        set => Settings = Settings with { PopupEnabled = value };
    }

    public bool SoundEnabled
    {
        get => Settings.SoundEnabled;
        set => Settings = Settings with { SoundEnabled = value };
    }

    public bool LoginFailureNotificationEnabled
    {
        get => Settings.LoginFailureNotificationEnabled;
        set => Settings = Settings with { LoginFailureNotificationEnabled = value };
    }

    public bool AutoStartEnabled
    {
        get => Settings.AutoStartEnabled;
        set => Settings = Settings with { AutoStartEnabled = value };
    }

    public StoreAccount? SelectedAccount
    {
        get => _selectedAccount;
        set => SetProperty(ref _selectedAccount, value);
    }

    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    public async Task SaveSettingsAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (settings.CheckIntervalMinutes is < 5 or > 120)
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "检查间隔必须在 5–120 分钟之间。");
        if (settings.RepeatIntervalMinutes < 5)
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "复报间隔不能少于 5 分钟。");
        await _actions.SaveSettingsAsync(settings, cancellationToken);
        Settings = settings;
    }

    private async Task CheckNowAsync(CancellationToken cancellationToken)
    {
        IsChecking = true;
        LastError = null;
        try
        {
            await _actions.CheckNowAsync(cancellationToken);
        }
        catch (Exception error)
        {
            LastError = error.Message;
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task ExecuteAccountActionAsync(Func<Task> action)
    {
        LastError = null;
        try
        {
            await action();
        }
        catch (Exception error)
        {
            LastError = error.Message;
        }
    }

    private bool SetFilter<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name)) return false;
        OnPropertyChanged(nameof(FilteredOrders));
        return true;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(FilteredOrders));
        OnPropertyChanged(nameof(DueSoonCount));
        OnPropertyChanged(nameof(CriticalCount));
        OnPropertyChanged(nameof(OverdueCount));
        OnPropertyChanged(nameof(OnlineAccountCount));
    }
}
