using OrderAlert.App.ViewModels;
using OrderAlert.Core.Models;

namespace OrderAlert.Core.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void Calculates_summary_counts_and_filters_orders()
    {
        var viewModel = new MainViewModel(new FakeActions());
        viewModel.Orders.Add(Order("due", RiskLevel.DueSoon));
        viewModel.Orders.Add(Order("critical", RiskLevel.Critical));
        viewModel.Orders.Add(Order("overdue", RiskLevel.Overdue));

        Assert.Equal(2, viewModel.DueSoonCount);
        Assert.Equal(1, viewModel.CriticalCount);
        Assert.Equal(1, viewModel.OverdueCount);

        viewModel.SelectedRisk = RiskLevel.Critical;
        Assert.Equal("critical", Assert.Single(viewModel.FilteredOrders).OrderId);
    }

    [Fact]
    public void Online_count_excludes_disabled_accounts()
    {
        var viewModel = new MainViewModel(new FakeActions());
        viewModel.Accounts.Add(Account(true));
        viewModel.Accounts.Add(Account(false));

        Assert.Equal(1, viewModel.OnlineAccountCount);
    }

    [Fact]
    public async Task Check_now_command_is_disabled_while_running()
    {
        var actions = new FakeActions { HoldCheck = true };
        var viewModel = new MainViewModel(actions);

        var running = viewModel.CheckNowCommand.ExecuteAsync(null);
        await actions.Started.Task;
        Assert.False(viewModel.CheckNowCommand.CanExecute(null));
        actions.Release.SetResult();
        await running;
        Assert.True(viewModel.CheckNowCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    public async Task Rejects_check_intervals_outside_five_to_120_minutes(int interval)
    {
        var viewModel = new MainViewModel(new FakeActions());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => viewModel.SaveSettingsAsync(
                AppSettings.Default with { CheckIntervalMinutes = interval }));
    }

    private static OrderItemViewModel Order(string id, RiskLevel risk) =>
        new(
            PlatformKind.AliExpress,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "US",
            id,
            "Awaiting shipment",
            null,
            3600,
            risk,
            DateTimeOffset.UtcNow,
            "https://example.invalid/order");

    private static StoreAccount Account(bool enabled) =>
        new(
            Guid.NewGuid(),
            PlatformKind.AliExpress,
            "US",
            "owner",
            "C:\\profile",
            enabled,
            enabled ? DateTimeOffset.UtcNow : null);

    private sealed class FakeActions : IOrderAlertActions
    {
        public bool HoldCheck { get; init; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task CheckNowAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (HoldCheck) await Release.Task.WaitAsync(cancellationToken);
        }

        public Task OpenOrderAsync(OrderItemViewModel order) => Task.CompletedTask;

        public Task SaveSettingsAsync(
            AppSettings settings,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
