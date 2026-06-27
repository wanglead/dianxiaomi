using OrderAlert.Core.Models;
using OrderAlert.Core.Services;

namespace OrderAlert.Core.Tests;

public sealed class AlertPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 6, 23, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void First_due_soon_order_alerts_immediately()
    {
        var decision = Decide(State(), State(("order-1", RiskLevel.DueSoon)));

        Assert.True(decision.ShouldNotify);
        Assert.Contains("order-1", decision.AffectedOrderKeys);
    }

    [Fact]
    public void Escalation_bypasses_an_existing_snooze()
    {
        var previous = State(("order-1", RiskLevel.DueSoon)) with
        {
            SnoozedUntil = Now.AddHours(1)
        };

        var decision = Decide(previous, State(("order-1", RiskLevel.Critical)));

        Assert.True(decision.ShouldNotify);
        Assert.Equal("risk-escalation", decision.Reason);
    }

    [Fact]
    public void Stable_risk_is_suppressed_before_repeat_interval()
    {
        var previous = State(("order-1", RiskLevel.DueSoon)) with
        {
            LastNotifiedAt = Now.AddMinutes(-119)
        };

        Assert.False(Decide(previous, State(("order-1", RiskLevel.DueSoon))).ShouldNotify);
    }

    [Fact]
    public void Stable_risk_repeats_at_exact_interval()
    {
        var previous = State(("order-1", RiskLevel.DueSoon)) with
        {
            LastNotifiedAt = Now.AddMinutes(-120)
        };

        Assert.True(Decide(previous, State(("order-1", RiskLevel.DueSoon))).ShouldNotify);
    }

    [Fact]
    public void Snooze_suppresses_a_stable_repeat()
    {
        var previous = State(("order-1", RiskLevel.DueSoon)) with
        {
            LastNotifiedAt = Now.AddHours(-3),
            SnoozedUntil = Now.AddMinutes(10)
        };

        Assert.False(Decide(previous, State(("order-1", RiskLevel.DueSoon))).ShouldNotify);
    }

    [Fact]
    public void New_order_and_account_failure_alert_immediately()
    {
        var previous = State(("old", RiskLevel.DueSoon)) with
        {
            SnoozedUntil = Now.AddHours(1)
        };
        var current = State(
            [("old", RiskLevel.DueSoon), ("new", RiskLevel.DueSoon)],
            [Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]);

        var decision = Decide(previous, current);

        Assert.True(decision.ShouldNotify);
        Assert.Contains("new", decision.AffectedOrderKeys);
    }

    private static AlertDecision Decide(AlertState previous, AlertState current) =>
        new AlertPolicy().Decide(previous, current, AppSettings.Default, Now);

    private static AlertState State(params (string Key, RiskLevel Risk)[] risks) =>
        State(risks, []);

    private static AlertState State(
        IEnumerable<(string Key, RiskLevel Risk)> risks,
        IEnumerable<Guid> failures) =>
        new(
            risks.ToDictionary(item => item.Key, item => item.Risk),
            failures.ToHashSet(),
            null,
            null);
}
