namespace OrderAlert.Core.Services;

public sealed record ScheduleWindow(TimeOnly Start, TimeOnly End)
{
    public bool Contains(TimeOnly time)
    {
        if (Start == End) return true;
        return Start < End
            ? time >= Start && time < End
            : time >= Start || time < End;
    }

    public DateTimeOffset NextRun(
        DateTimeOffset now,
        DateTimeOffset? lastRun,
        TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        var candidate = lastRun.HasValue && lastRun.Value + interval > now
            ? lastRun.Value + interval
            : now;
        if (Contains(TimeOnly.FromDateTime(candidate.DateTime))) return candidate;
        return NextOpening(candidate);
    }

    private DateTimeOffset NextOpening(DateTimeOffset value)
    {
        var localDate = DateOnly.FromDateTime(value.DateTime);
        var localTime = TimeOnly.FromDateTime(value.DateTime);
        var openingDate = localTime < Start ? localDate : localDate.AddDays(1);
        return new DateTimeOffset(
            openingDate.ToDateTime(Start),
            value.Offset);
    }
}
