using OrderAlert.Core.Models;

namespace OrderAlert.Core.Services;

public sealed record AlertState(
    IReadOnlyDictionary<string, RiskLevel> OrderRisks,
    IReadOnlySet<Guid> FailedAccounts,
    DateTimeOffset? LastNotifiedAt,
    DateTimeOffset? SnoozedUntil);

public sealed record AlertDecision(
    bool ShouldNotify,
    bool ShouldPlaySound,
    string Reason,
    DateTimeOffset? NextEligibleAt,
    IReadOnlyList<string> AffectedOrderKeys);

public sealed class AlertPolicy
{
    public AlertDecision Decide(
        AlertState previous,
        AlertState current,
        AppSettings settings,
        DateTimeOffset now)
    {
        var affected = new List<string>();
        var hasEscalation = false;
        foreach (var (key, risk) in current.OrderRisks)
        {
            if (!IsActionable(risk)) continue;
            if (!previous.OrderRisks.TryGetValue(key, out var previousRisk))
            {
                affected.Add(key);
                continue;
            }
            if (Severity(risk) > Severity(previousRisk))
            {
                affected.Add(key);
                hasEscalation = true;
            }
        }

        var hasNewFailure = current.FailedAccounts
            .Except(previous.FailedAccounts)
            .Any();
        var isImmediate = affected.Count > 0 || hasNewFailure;
        var hasActiveRisk = current.OrderRisks.Values.Any(IsActionable)
            || current.FailedAccounts.Count > 0;

        if (isImmediate)
        {
            var reason = hasEscalation
                ? "risk-escalation"
                : hasNewFailure && affected.Count == 0
                    ? "account-failure"
                    : "new-risk";
            return Notify(reason, affected, settings, now);
        }

        if (!hasActiveRisk)
            return Suppress("no-active-risk", affected, previous, settings);
        if (previous.SnoozedUntil > now)
            return Suppress("snoozed", affected, previous, settings);

        var repeatInterval = TimeSpan.FromMinutes(settings.RepeatIntervalMinutes);
        if (!previous.LastNotifiedAt.HasValue
            || now - previous.LastNotifiedAt.Value >= repeatInterval)
        {
            return Notify("repeat-interval", affected, settings, now);
        }
        return Suppress("repeat-suppressed", affected, previous, settings);
    }

    private static AlertDecision Notify(
        string reason,
        IReadOnlyList<string> affected,
        AppSettings settings,
        DateTimeOffset now) =>
        new(
            true,
            settings.SoundEnabled,
            reason,
            now.AddMinutes(settings.RepeatIntervalMinutes),
            affected);

    private static AlertDecision Suppress(
        string reason,
        IReadOnlyList<string> affected,
        AlertState previous,
        AppSettings settings) =>
        new(
            false,
            false,
            reason,
            previous.SnoozedUntil
                ?? previous.LastNotifiedAt?.AddMinutes(settings.RepeatIntervalMinutes),
            affected);

    private static bool IsActionable(RiskLevel risk) =>
        risk is RiskLevel.DueSoon or RiskLevel.Critical or RiskLevel.Overdue;

    private static int Severity(RiskLevel risk) => risk switch
    {
        RiskLevel.DueSoon => 1,
        RiskLevel.Critical => 2,
        RiskLevel.Overdue => 3,
        _ => 0
    };
}
