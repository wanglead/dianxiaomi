using System.Collections.Concurrent;
using OrderAlert.Core.Chrome;
using OrderAlert.Core.Models;

namespace OrderAlert.Core.Services;

public interface IScanBridge
{
    Task<ScanBatch> ScanAsync(
        StoreAccount account,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken);
}

public interface IScanStore
{
    Task CommitBatchAsync(ScanBatch batch, CancellationToken cancellationToken);

    Task RecordFailedBatchAsync(
        Guid accountId,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        string error,
        CancellationToken cancellationToken);
}

public enum ScanStatus
{
    Succeeded,
    Failed,
    SkippedDisabled,
    SkippedAlreadyRunning
}

public sealed record ScanOutcome(ScanStatus Status, string? Error = null);

public sealed class ScanOrchestrator
{
    private static readonly string[] DianxiaomiUrls =
    [
        "https://www.dianxiaomi.com/web/order/paid?go=m100",
        "https://www.dianxiaomi.com/web/order/approved?go=m101",
        "https://www.dianxiaomi.com/web/order/processed/self_warehouse?go=m10201",
        "https://www.dianxiaomi.com/web/order/allocated/has?go=m10301"
    ];

    private static readonly string[] AliExpressUrls =
    [
        "https://csp.aliexpress.com/m_apps/order-manage/orderList?channelId=244176"
    ];

    private readonly IChromeProfileLauncher _launcher;
    private readonly IScanBridge _bridge;
    private readonly IScanStore _store;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _accountLocks = new();

    public ScanOrchestrator(
        IChromeProfileLauncher launcher,
        IScanBridge bridge,
        IScanStore store)
    {
        _launcher = launcher;
        _bridge = bridge;
        _store = store;
    }

    public async Task<ScanOutcome> ScanAccountAsync(
        StoreAccount account,
        CancellationToken cancellationToken = default)
    {
        if (!account.IsEnabled) return new ScanOutcome(ScanStatus.SkippedDisabled);
        var gate = _accountLocks.GetOrAdd(account.Id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
            return new ScanOutcome(ScanStatus.SkippedAlreadyRunning);

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var urls = account.Platform == PlatformKind.Dianxiaomi
                ? DianxiaomiUrls
                : AliExpressUrls;
            await _launcher.LaunchAsync(account, urls, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var batch = await _bridge.ScanAsync(account, urls, timeout.Token);
            if (!batch.Complete)
                throw new InvalidOperationException("The extension returned a partial scan.");
            await _store.CommitBatchAsync(batch, cancellationToken);
            return new ScanOutcome(ScanStatus.Succeeded);
        }
        catch (Exception error) when (error is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            await _store.RecordFailedBatchAsync(
                account.Id,
                startedAt,
                DateTimeOffset.UtcNow,
                error.Message,
                cancellationToken);
            return new ScanOutcome(ScanStatus.Failed, error.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ScanOutcome>> ScanEnabledAsync(
        IEnumerable<StoreAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var tasks = accounts
            .Where(account => account.IsEnabled)
            .Select(account => ScanAccountAsync(account, cancellationToken));
        return await Task.WhenAll(tasks);
    }
}
