using OrderAlert.Core.Models;

namespace OrderAlert.Core.Services;

public sealed class DeadlineEvaluator
{
    public EvaluationResult Evaluate(OrderSnapshot order, DateTimeOffset now)
    {
        var assessmentRisk = EvaluateAssessment(order.AssessmentAt, now);
        var shippingRisk = EvaluateShipping(order.ShippingSeconds);
        var risk = Max(assessmentRisk, shippingRisk);
        var reason = risk == shippingRisk && order.ShippingSeconds.HasValue
            ? "shipping-countdown"
            : order.AssessmentAt.HasValue
                ? "assessment-time"
                : "time-unrecognized";
        return new EvaluationResult(risk, reason);
    }

    private static RiskLevel EvaluateAssessment(
        DateTimeOffset? assessmentAt,
        DateTimeOffset now)
    {
        if (!assessmentAt.HasValue) return RiskLevel.Normal;
        var anchor = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            19,
            0,
            0,
            now.Offset);
        var remaining = assessmentAt.Value - anchor;
        if (remaining <= TimeSpan.Zero) return RiskLevel.Overdue;
        return remaining < TimeSpan.FromHours(24)
            ? RiskLevel.DueSoon
            : RiskLevel.Normal;
    }

    private static RiskLevel EvaluateShipping(int? seconds)
    {
        if (!seconds.HasValue) return RiskLevel.Normal;
        if (seconds.Value <= 0) return RiskLevel.Overdue;
        if (seconds.Value <= 7200) return RiskLevel.Critical;
        return seconds.Value < 86400 ? RiskLevel.DueSoon : RiskLevel.Normal;
    }

    private static RiskLevel Max(RiskLevel left, RiskLevel right) =>
        (RiskLevel)Math.Max((int)left, (int)right);
}
