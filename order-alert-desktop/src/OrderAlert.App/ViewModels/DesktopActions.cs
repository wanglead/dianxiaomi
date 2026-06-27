using System.Diagnostics;
using Microsoft.VisualBasic;
using OrderAlert.App.Startup;
using OrderAlert.Core.Accounts;
using OrderAlert.Core.Chrome;
using OrderAlert.Core.Models;
using OrderAlert.Core.Persistence;

namespace OrderAlert.App.ViewModels;

public sealed class DesktopActions : IOrderAlertActions
{
    private readonly SqliteStore _store;
    private readonly AccountService _accounts;
    private readonly AutoStartService _autoStart;
    private readonly string _extensionDirectory;

    public DesktopActions(
        SqliteStore store,
        AccountService accounts,
        AutoStartService autoStart,
        string extensionDirectory)
    {
        _store = store;
        _accounts = accounts;
        _autoStart = autoStart;
        _extensionDirectory = extensionDirectory;
    }

    public event Action<StoreAccount>? AccountAdded;

    public async Task CheckNowAsync(CancellationToken cancellationToken)
    {
        foreach (var account in (await _accounts.ListAsync(cancellationToken))
                     .Where(account => account.IsEnabled))
        {
            await LaunchAccountAsync(account, cancellationToken);
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

    public async Task AddAccountAsync(CancellationToken cancellationToken)
    {
        var displayName = Interaction.InputBox(
            "请输入店铺显示名称：",
            "添加速卖通店铺");
        if (string.IsNullOrWhiteSpace(displayName)) return;
        var identifier = Interaction.InputBox(
            "请输入账号标识（不需要密码）：",
            "添加速卖通店铺");
        if (string.IsNullOrWhiteSpace(identifier)) return;
        var account = await _accounts.AddAliExpressAsync(
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
        var launcher = new ChromeProfileLauncher(_extensionDirectory);
        var urls = account.Platform == PlatformKind.Dianxiaomi
            ? new[] { "https://www.dianxiaomi.com/web/order/paid?go=m100" }
            : new[]
            {
                "https://csp.aliexpress.com/m_apps/order-manage/orderList?channelId=244176"
            };
        return launcher.LaunchAsync(account, urls, cancellationToken);
    }
}
