namespace MousePulse.Core;

/// <summary>
/// Weekly time window in local time, e.g. Monday-Friday 08:00-17:00.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Start is inclusive, End is exclusive (08:00-17:00 is active at 08:00:00 and inactive at 17:00:00).</item>
/// <item>If End is earlier than Start, the window crosses midnight and belongs to the day it starts on
/// (Friday 22:00-06:00 is active on Saturday 03:00).</item>
/// <item>If Start equals End, the selected days are active all day.</item>
/// </list>
/// </remarks>
public sealed record Schedule(IReadOnlySet<DayOfWeek> Days, TimeOnly Start, TimeOnly End)
{
    public static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public bool IsActive(DateTime now)
    {
        TimeOnly time = TimeOnly.FromDateTime(now);
        DayOfWeek today = now.DayOfWeek;

        if (Start == End)
        {
            return Days.Contains(today);
        }

        if (Start < End)
        {
            return Days.Contains(today) && time >= Start && time < End;
        }

        // Crosses midnight: evening part belongs to today, morning part to yesterday's window.
        DayOfWeek yesterday = (DayOfWeek)(((int)today + 6) % 7);
        return (Days.Contains(today) && time >= Start) || (Days.Contains(yesterday) && time < End);
    }

    /// <summary>The next moment at which <see cref="IsActive"/> changes, or null if it never does.</summary>
    public DateTime? NextChange(DateTime now)
    {
        bool current = IsActive(now);
        var candidates = new List<DateTime>();
        for (int offset = -1; offset <= 8; offset++)
        {
            DateTime day = now.Date.AddDays(offset);
            candidates.Add(day + Start.ToTimeSpan());
            candidates.Add(day + End.ToTimeSpan());
        }

        candidates.Sort();
        foreach (DateTime candidate in candidates)
        {
            if (candidate > now && IsActive(candidate) != current)
            {
                return candidate;
            }
        }

        return null;
    }
}

public enum ScheduleAction
{
    None,
    Start,
    Stop,
}

/// <summary>
/// Turns the schedule into Start/Stop actions. Only <b>transitions</b> act, so a manual Start or Stop is kept
/// until the next scheduled change. When the schedule is first evaluated (app launch, schedule edited) it only
/// starts - it never stops something the user started by hand.
/// </summary>
public sealed class ScheduleController
{
    private bool? _lastActive;

    public ScheduleAction Evaluate(Schedule? schedule, DateTime now)
    {
        if (schedule is null)
        {
            _lastActive = null;
            return ScheduleAction.None;
        }

        bool active = schedule.IsActive(now);
        bool? previous = _lastActive;
        _lastActive = active;

        if (previous is null)
        {
            return active ? ScheduleAction.Start : ScheduleAction.None;
        }

        if (previous == active)
        {
            return ScheduleAction.None;
        }

        return active ? ScheduleAction.Start : ScheduleAction.Stop;
    }

    /// <summary>Forces the next evaluation to be treated as a first evaluation (after the schedule was edited).</summary>
    public void Reset() => _lastActive = null;
}
