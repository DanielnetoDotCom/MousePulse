using MousePulse.Core;

namespace MousePulse.Tests;

public class ScheduleTests
{
    // 2026-11-23 is a Monday.
    private static DateTime Mon(int h, int m = 0) => new(2026, 11, 23, h, m, 0);

    private static readonly Schedule Office = new(
        new HashSet<DayOfWeek>(Schedule.Weekdays), new TimeOnly(8, 0), new TimeOnly(17, 0));

    [Theory]
    [InlineData(0, 7, 59, false)]
    [InlineData(0, 8, 0, true)]    // start is inclusive
    [InlineData(0, 12, 30, true)]
    [InlineData(0, 16, 59, true)]
    [InlineData(0, 17, 0, false)]  // end is exclusive
    [InlineData(4, 10, 0, true)]   // Friday
    [InlineData(5, 10, 0, false)]  // Saturday
    [InlineData(6, 10, 0, false)]  // Sunday
    public void Weekday_office_hours(int dayOffset, int h, int m, bool expected)
    {
        Assert.Equal(expected, Office.IsActive(Mon(h, m).AddDays(dayOffset)));
    }

    [Fact]
    public void Next_change_during_the_day_is_the_end_time()
    {
        Assert.Equal(Mon(17), Office.NextChange(Mon(9, 15)));
    }

    [Fact]
    public void Next_change_on_friday_evening_is_monday_morning()
    {
        DateTime fridayEvening = Mon(18).AddDays(4);
        Assert.Equal(Mon(8).AddDays(7), Office.NextChange(fridayEvening));
    }

    [Fact]
    public void Overnight_window_belongs_to_the_start_day()
    {
        var night = new Schedule(new HashSet<DayOfWeek> { DayOfWeek.Friday }, new TimeOnly(22, 0), new TimeOnly(6, 0));
        DateTime friday = Mon(0).AddDays(4);

        Assert.False(night.IsActive(friday.AddHours(3)));   // Fri 03:00 belongs to Thursday's window
        Assert.True(night.IsActive(friday.AddHours(23)));   // Fri 23:00
        Assert.True(night.IsActive(friday.AddHours(27)));   // Sat 03:00
        Assert.False(night.IsActive(friday.AddHours(30)));  // Sat 06:00
    }

    [Fact]
    public void Equal_start_and_end_means_all_day()
    {
        var allDay = new Schedule(new HashSet<DayOfWeek> { DayOfWeek.Monday }, new TimeOnly(0, 0), new TimeOnly(0, 0));
        Assert.True(allDay.IsActive(Mon(3)));
        Assert.False(allDay.IsActive(Mon(3).AddDays(1)));
    }

    [Fact]
    public void No_days_never_changes()
    {
        var none = new Schedule(new HashSet<DayOfWeek>(), new TimeOnly(8, 0), new TimeOnly(17, 0));
        Assert.Null(none.NextChange(Mon(9)));
    }

    [Fact]
    public void Controller_starts_at_start_time_and_stops_at_end_time()
    {
        var controller = new ScheduleController();
        Assert.Equal(ScheduleAction.None, controller.Evaluate(Office, Mon(7, 59)));
        Assert.Equal(ScheduleAction.Start, controller.Evaluate(Office, Mon(8, 0)));
        Assert.Equal(ScheduleAction.None, controller.Evaluate(Office, Mon(12, 0)));
        Assert.Equal(ScheduleAction.Stop, controller.Evaluate(Office, Mon(17, 0)));
        Assert.Equal(ScheduleAction.None, controller.Evaluate(Office, Mon(17, 1)));
    }

    [Fact]
    public void Launch_inside_the_window_starts_immediately()
    {
        Assert.Equal(ScheduleAction.Start, new ScheduleController().Evaluate(Office, Mon(10)));
    }

    [Fact]
    public void Launch_outside_the_window_does_not_stop_a_manual_start()
    {
        Assert.Equal(ScheduleAction.None, new ScheduleController().Evaluate(Office, Mon(20)));
    }

    [Fact]
    public void Wake_from_sleep_across_the_boundary_still_applies()
    {
        var controller = new ScheduleController();
        controller.Evaluate(Office, Mon(16, 30));
        Assert.Equal(ScheduleAction.Stop, controller.Evaluate(Office, Mon(19, 0))); // PC slept over 17:00
    }

    [Fact]
    public void Disabled_schedule_does_nothing()
    {
        var controller = new ScheduleController();
        Assert.Equal(ScheduleAction.None, controller.Evaluate(null, Mon(10)));
    }
}
