namespace MousePulse.Core;

/// <summary>
/// Abstraction over the Windows input/idle APIs so the engine can be unit-tested deterministically.
/// All tick values use the GetTickCount time base (milliseconds, wraps every ~49.7 days).
/// </summary>
public interface ISystemInput
{
    /// <summary>Current GetTickCount value.</summary>
    uint TickCount { get; }

    /// <summary>Tick of the last input event in this session (GetLastInputInfo).</summary>
    uint LastInputTick { get; }

    /// <summary>Injects a tiny mouse movement (SendInput).</summary>
    /// <param name="distance">Length of the move in pixels.</param>
    /// <param name="natural">Random heading and a stepped, paced path instead of a fixed diagonal.</param>
    NudgeResult Nudge(int distance, bool natural = false);
}

/// <param name="Sent">True when SendInput accepted every event.</param>
/// <param name="EventCount">Number of mouse events injected.</param>
/// <param name="EdgeSafe">True when the cursor was on a monitor edge and only an inward move was sent.</param>
/// <param name="Win32Error">GetLastError when SendInput failed, otherwise 0.</param>
/// <param name="Corrected">True when pointer acceleration left a small offset that was undone with SetCursorPos.</param>
public readonly record struct NudgeResult(bool Sent, int EventCount, bool EdgeSafe, int Win32Error, bool Corrected = false);
