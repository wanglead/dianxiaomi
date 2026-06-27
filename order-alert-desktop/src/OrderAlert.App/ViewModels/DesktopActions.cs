using System.Diagnostics;
using Microsoft.VisualBasic;
using OrderAlert.App.Startup;
using OrderAlert.Core.Accounts;
using OrderAlert.Core.Chrome;
using OrderAlert.Core.Models;
using OrderAlert.Core.Persistence;
using OrderAlert.Core.Services;

namespace OrderAlert.App.ViewModels;

public sealed class DesktopActions : IOrderAlertActions
{
    private readonly SqliteStore _store;
    private readonly AccountService _accounts;
    private readonly AutoStartService _autoStart;
    private readonly IChromeProfileLauncher _launcher;
    private readonly ScanOrchestrator _orchestrator;

    public DesktopActions(
        SqliteStore store,
        AccountService accounts,
        AutoStartService autoStart,
        IChromeProfileLauncher launcher,
        ScanOrchestrator orchestrator)
    {
        _store = store;
        _accounts = accounts;
        _autoStart = autoStart;
        _launcher = launcher;
        _orchestrator = orchestrator;
    }

    public event Action<StoreAccount>? AccountAdded;

    public async Task CheckNowAsync(CancellationToken cancellationToken)
    {
        foreach (var account in (await _accounts.ListAsync(cancellationToken))
                     .Where(account => account.IsEnabled))
        {
            await _orchestrator.ScanAccountAsync(account, cancellationToken);
        }
    }

    public Task OpenOrderAsync(OrderItemViewModel order)
    {
        Process.Start(new ProcessStartInfo(order.SourceUrl) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public async Task SaveSettingsAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        await _store.SaveSettingsAsync(settings, cancellationToken);
        _autoStart.SetEnabled(
            settings.AutoStartEnabled,
            Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定程序路径。"));
    }

    public Task AddDianxiaomiAccountAsync(CancellationToken cancellationToken) =>
        AddAccountAsync(
            PlatformKind.Dianxiaomi,
            "添加店小秘账号",
            cancellationToken);

    public Task AddAliExpressAccountAsync(CancellationToken cancellationToken) =>
        AddAccountAsync(
            PlatformKind.AliExpress,
            "添加速卖通店铺",
            cancellationToken);

    private async Task AddAccountAsync(
        PlatformKind platform,
        string dialogTitle,
        CancellationToken cancellationToken)
    {
        var displayName = Interaction.InputBox(
            "请输入店铺显示名称：",
            dialogTitle);
        if (string.IsNullOrWhiteSpace(displayName)) return;
        var identifier = Interaction.InputBox(
            "请输入账号标识（不需要密码）：",
            dialogTitle);
        if (string.IsNullOrWhiteSpace(identifier)) return;
        var account = platform == PlatformKind.Dianxiaomi
            ? await _accounts.AddDianxiaomiAsync(
                displayName,
                identifier,
                cancellationToken)
            : await _accounts.AddAliExpressAsync(
                displayName,
                identifier,
                cancellationToken);
        AccountAdded?.Invoke(account);
        await LaunchAccountAsync(account, cancellationToken);
    }

    public Task ReloginAsync(
        StoreAccount account,
        CancellationToken cancellationToken) =>
        LaunchAccountAsync(account, cancellationToken);

    private Task LaunchAccountAsync(
        StoreAccount account,
        CancellationToken cancellationToken)
    {
        return _launcher.LaunchAsync(
            account,
            AccountLoginTargets.For(account.Platform),
            cancellationToken);
    }
}
