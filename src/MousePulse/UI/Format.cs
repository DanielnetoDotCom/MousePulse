using System.Globalization;

namespace MousePulse.UI;

internal static class Format
{
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        if (t.TotalSeconds < 60)
        {
            return $"{(int)t.TotalSeconds} s";
        }

        if (t.TotalHours < 1)
        {
            return $"{(int)t.TotalMinutes} min {t.Seconds:00} s";
        }

        return $"{(int)t.TotalHours} h {t.Minutes:00} min";
    }

    public static string Seconds(int seconds) => Duration(TimeSpan.FromSeconds(seconds));

    public static string Clock(DateTime time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>"at 17:00", "tomorrow at 08:00", "on Monday at 08:00".</summary>
    public static string When(DateTime time, DateTime now)
    {
        string hm = time.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (time.Date == now.Date)
        {
            return $"at {hm}";
        }

        return time.Date == now.Date.AddDays(1)
            ? $"tomorrow at {hm}"
            : $"on {time.ToString("dddd", CultureInfo.InvariantCulture)} at {hm}";
    }
}
