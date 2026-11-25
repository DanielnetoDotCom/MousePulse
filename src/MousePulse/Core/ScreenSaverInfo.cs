using MousePulse.Native;

namespace MousePulse.Core;

/// <summary>Reads the current user's screen saver configuration (used for configuration hints only).</summary>
public static class ScreenSaverInfo
{
    /// <summary>Screen saver timeout, or null when the screen saver is disabled.</summary>
    public static TimeSpan? GetTimeout()
    {
        if (!NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETSCREENSAVEACTIVE, 0, out int active, 0) || active == 0)
        {
            return null;
        }

        return NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETSCREENSAVETIMEOUT, 0, out int seconds, 0) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }
}
