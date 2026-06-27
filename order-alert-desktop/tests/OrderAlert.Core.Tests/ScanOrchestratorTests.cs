using OrderAlert.Core.Chrome;
using OrderAlert.Core.Models;
using OrderAlert.Core.Services;

namespace OrderAlert.Core.Tests;

public sealed class ScanOrchestratorTests
{
    [Fact]
    public async Task Same_account_cannot_scan_twice_concurrently()
    {
        var bridge = new FakeBridge { Hold = true };
        var orchestrator = Create(bridge, out _, out _);
        var account = Account();

        var first = orchestrator.ScanAccountAsync(account);
        await bridge.Started.Task;
        var second = await orchestrator.ScanAccountAsync(account);
        bridge.Release.SetResult();
        await first;

        Assert.Equal(ScanStatus.SkippedAlreadyRunning, second.Status);
    }

    [Fact]
    public async Task Successful_complete_batch_commits_once()
    {
        var orchestrator = Create(new FakeBridge(), out var store, out _);

        var result = await orchestrator.ScanAccountAsync(Account());

        Assert.Equal(ScanStatus.Succeeded, result.Status);
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(0, store.FailureCount);
    }

    [Fact]
    public async Task Partial_batch_records_failure_without_committing()
    {
        var bridge = new FakeBridge { Complete = false };
        var orchestrator = Create(bridge, out var store, out _);

        var result = await orchestrator.ScanAccountAsync(Account());

        Assert.Equal(ScanStatus.Failed, result.Status);
        Assert.Equal(0, store.CommitCount);
        Assert.Equal(1, store.FailureCount);
    }

    [Fact]
    public async Task Disabled_accounts_are_skipped()
    {
        var orchestrator = Create(new FakeBridge(), out var store, out var launcher);

        var result = await orchestrator.ScanAccountAsync(Account() with { IsEnabled = false });

        Assert.Equal(ScanStatus.SkippedDisabled, result.Status);
        Assert.Equal(0, launcher.Count);
        Assert.Equal(0, store.CommitCount);
    }

    private static ScanOrchestrator Create(
        FakeBridge bridge,
        out FakeStore store,
        out FakeLauncher launcher)
    {
        store = new FakeStore();
        launcher = new FakeLauncher();
        return new ScanOrchestrator(launcher, bridge, store);
    }

    private static StoreAccount Account() =>
        new(
            Guid.NewGuid(),
            PlatformKind.AliExpress,
            "US",
            "owner",
            "C:\\profiles\\test");

    private sealed class FakeLauncher : IChromeProfileLauncher
    {
        public int Count { get; private set; }

        public Task LaunchAsync(
            StoreAccount account,
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken)
        {
            Count += 1;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBridge : IScanBridge
    {
        public bool Complete { get; init; } = true;
        public bool Hold { get; init; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ScanBatch> ScanAsync(
            StoreAccount account,
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            return new ScanBatch(account.Id, Complete, now, now, [], 1);
        }
    }

    private sealed class FakeStore : IScanStore
    {
        public int CommitCount { get; private set; }
        public int FailureCount { get; private set; }

        public Task CommitBatchAsync(
            ScanBatch batch,
            CancellationToken cancellationToken)
        {
            CommitCount += 1;
            return Task.CompletedTask;
        }

        public Task RecordFailedBatchAsync(
            Guid accountId,
            DateTimeOffset startedAt,
            DateTimeOffset finishedAt,
            string error,
            CancellationToken cancellationToken)
        {
            FailureCount += 1;
            return Task.CompletedTask;
        }
    }
}
