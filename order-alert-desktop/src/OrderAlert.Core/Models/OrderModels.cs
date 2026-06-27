namespace OrderAlert.Core.Models;

public enum PlatformKind
{
    Dianxiaomi,
    AliExpress
}

public enum RiskLevel
{
    Normal,
    DueSoon,
    Critical,
    Overdue,
    Unrecognized
}

public sealed record StoreAccount(
    Guid Id,
    PlatformKind Platform,
    string DisplayName,
    string AccountIdentifier,
    string ProfilePath,
    bool IsEnabled = true,
    DateTimeOffset? LastSuccessfulScanAt = null,
    string? LastError = null);

public sealed record OrderSnapshot(
    PlatformKind Platform,
    Guid AccountId,
    string OrderId,
    string SourceUrl,
    string RawStatus,
    DateTimeOffset? AssessmentAt,
    int? ShippingSeconds,
    DateTimeOffset LastSeenAt,
    bool IsActive = true)
{
    public string OrderKey => $"{Platform}:{AccountId}:{OrderId}";

    public static OrderSnapshot WithShippingSeconds(int seconds) =>
        new(
            PlatformKind.AliExpress,
            Guid.Empty,
            "test-order",
            "https://example.invalid/order",
            "",
            null,
            seconds,
            DateTimeOffset.UnixEpoch);

    public static OrderSnapshot WithAssessmentAt(DateTimeOffset assessmentAt) =>
        WithShippingSeconds(86400) with
        {
            AssessmentAt = assessmentAt,
            ShippingSeconds = null
        };
}

public sealed record ScanBatch(
    Guid AccountId,
    bool Complete,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<OrderSnapshot> Orders,
    int PageCount = 0,
    string? Error = null);

public sealed record AppSettings(
    TimeOnly MonitoringStart,
    TimeOnly MonitoringEnd,
    int CheckIntervalMinutes,
    int RepeatIntervalMinutes,
    bool PopupEnabled,
    bool SoundEnabled,
    bool LoginFailureNotificationEnabled,
    bool AutoStartEnabled)
{
    public static AppSettings Default { get; } = new(
        new TimeOnly(8, 0),
        new TimeOnly(22, 0),
        15,
        120,
        true,
        true,
        true,
        false);
}

public sealed record EvaluationResult(RiskLevel Risk, string Reason);
