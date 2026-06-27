using OrderAlert.Core.Models;
using OrderAlert.Core.Services;

namespace OrderAlert.Core.Tests;

public sealed class DeadlineEvaluatorTests
{
    private static readonly DateTimeOffset BeijingNow =
        new(2026, 6, 23, 19, 0, 0, TimeSpan.FromHours(8));

    [Theory]
    [InlineData(86400, RiskLevel.Normal)]
    [InlineData(86399, RiskLevel.DueSoon)]
    [InlineData(7200, RiskLevel.Critical)]
    [InlineData(1, RiskLevel.Critical)]
    [InlineData(0, RiskLevel.Overdue)]
    [InlineData(-1, RiskLevel.Overdue)]
    public void Shipping_countdown_uses_exact_boundaries(
        int seconds,
        RiskLevel expected)
    {
        var order = OrderSnapshot.WithShippingSeconds(seconds);

        Assert.Equal(expected, new DeadlineEvaluator().Evaluate(order, BeijingNow).Risk);
    }

    [Theory]
    [InlineData(86400, RiskLevel.Normal)]
    [InlineData(86399, RiskLevel.DueSoon)]
    [InlineData(0, RiskLevel.Overdue)]
    [InlineData(-1, RiskLevel.Overdue)]
    public void Assessment_time_is_relative_to_the_current_days_19_clock(
        int seconds,
        RiskLevel expected)
    {
        var order = OrderSnapshot.WithAssessmentAt(BeijingNow.AddSeconds(seconds));

        Assert.Equal(expected, new DeadlineEvaluator().Evaluate(order, BeijingNow).Risk);
    }

    [Fact]
    public void Higher_risk_wins_when_both_time_fields_exist()
    {
        var order = OrderSnapshot.WithShippingSeconds(3600) with
        {
            AssessmentAt = BeijingNow.AddHours(-1)
        };

        Assert.Equal(
            RiskLevel.Overdue,
            new DeadlineEvaluator().Evaluate(order, BeijingNow).Risk);
    }
}
