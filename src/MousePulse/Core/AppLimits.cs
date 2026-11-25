namespace MousePulse.Core;

/// <summary>Allowed ranges for user-configurable values (shared by UI, settings and engine).</summary>
public static class AppLimits
{
    public const int MinThresholdSeconds = 5;
    public const int MaxThresholdSeconds = 4 * 60 * 60;
    public const int DefaultThresholdSeconds = 60;

    public const int MinIntervalSeconds = 5;
    public const int MaxIntervalSeconds = 60 * 60;
    public const int DefaultIntervalSeconds = 30;

    public const int MinDistance = 0;
    public const int MaxDistance = 50;
    public const int DefaultDistance = 1;
}
