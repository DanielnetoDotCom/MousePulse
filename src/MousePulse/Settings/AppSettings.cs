using System.Text.Json.Serialization;
using MousePulse.Core;

namespace MousePulse.Settings;

public sealed class AppSettings
{
    public int InactivityThresholdSeconds { get; set; } = AppLimits.DefaultThresholdSeconds;
    public int MoveIntervalSeconds { get; set; } = AppLimits.DefaultIntervalSeconds;
    public int MoveDistancePixels { get; set; } = AppLimits.DefaultDistance;

    /// <summary>Randomize the wait, distance and path of each move (see ActivityEngine.VaryMovement).</summary>
    public bool VaryMovement { get; set; } = true;

    public bool StartWithWindows { get; set; }
    public bool StartSimulationOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;

    /// <summary>Automatically start at <see cref="ScheduleStart"/> and stop at <see cref="ScheduleEnd"/>.</summary>
    public bool ScheduleEnabled { get; set; }
    public List<DayOfWeek> ScheduleDays { get; set; } = [.. Schedule.Weekdays];
    public TimeOnly ScheduleStart { get; set; } = new(8, 0);
    public TimeOnly ScheduleEnd { get; set; } = new(17, 0);

    /// <summary>The active schedule, or null when scheduling is off.</summary>
    public Schedule? ToSchedule() =>
        ScheduleEnabled ? new Schedule(ScheduleDays.ToHashSet(), ScheduleStart, ScheduleEnd) : null;

    /// <summary>Clamps every value into its supported range (protects against hand-edited files).</summary>
    public AppSettings Normalize()
    {
        ScheduleDays = (ScheduleDays ?? [.. Schedule.Weekdays]).Where(Enum.IsDefined).Distinct().ToList();
        ScheduleStart = new TimeOnly(ScheduleStart.Hour, ScheduleStart.Minute);
        ScheduleEnd = new TimeOnly(ScheduleEnd.Hour, ScheduleEnd.Minute);

        InactivityThresholdSeconds = Math.Clamp(InactivityThresholdSeconds, AppLimits.MinThresholdSeconds, AppLimits.MaxThresholdSeconds);
        MoveIntervalSeconds = Math.Clamp(MoveIntervalSeconds, AppLimits.MinIntervalSeconds, AppLimits.MaxIntervalSeconds);
        MoveDistancePixels = Math.Clamp(MoveDistancePixels, AppLimits.MinDistance, AppLimits.MaxDistance);
        return this;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true,ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
