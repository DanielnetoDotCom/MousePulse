using System.Drawing;
using System.Runtime.InteropServices;
using MousePulse.Native;

namespace MousePulse.Core;

/// <summary>
/// Real implementation backed by GetLastInputInfo / SendInput.
/// </summary>
public sealed class Win32SystemInput : ISystemInput
{
    /// <summary>Value placed in MOUSEINPUT.dwExtraInfo so our own events are identifiable ("MPLS").</summary>
    public const nint Signature = 0x4D504C53;

    public uint TickCount => unchecked((uint)Environment.TickCount);

    public uint LastInputTick
    {
        get
        {
            var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
            return NativeMethods.GetLastInputInfo(ref info) ? info.dwTime : TickCount;
        }
    }

    public unsafe NudgeResult Nudge(int distance, bool natural = false)
    {
        Point cursor = NativeMethods.GetCursorPos(out var p) ? new Point(p.X, p.Y) : Cursor.Position;
        Rectangle monitor = Screen.FromPoint(cursor).Bounds;
        NudgePlan plan = NudgePlanner.Plan(cursor, monitor, distance, natural ? Random.Shared : null);

        int count = plan.Moves.Count;
        NativeMethods.INPUT* inputs = stackalloc NativeMethods.INPUT[count];
        for (int i = 0; i < count; i++)
        {
            inputs[i] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = plan.Moves[i].Width,
                        dy = plan.Moves[i].Height,
                        dwFlags = NativeMethods.MOUSEEVENTF_MOVE,
                        dwExtraInfo = Signature,
                    },
                },
            };
        }

        uint sent;
        if (plan.Paced)
        {
            // One event at a time, a few ms apart (<= ~130 ms in total, well inside the engine's
            // own-input window), so the pointer glides instead of jumping.
            sent = 0;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    Thread.Sleep(Random.Shared.Next(MinStepDelayMs, MaxStepDelayMs + 1));
                }

                if (NativeMethods.SendInput(1, inputs + i, sizeof(NativeMethods.INPUT)) != 1)
                {
                    break;
                }

                sent++;
            }
        }
        else
        {
            sent = NativeMethods.SendInput((uint)count, inputs, sizeof(NativeMethods.INPUT));
        }

        int error = sent == count ? 0 : Marshal.GetLastPInvokeError();
        bool corrected = sent == count && RestoreAfterAcceleration(plan, cursor);
        return new NudgeResult(sent == count, count, plan.EdgeSafe, error, corrected);
    }

    private const int MinStepDelayMs = 6;
    private const int MaxStepDelayMs = 18;

    /// <summary>
    /// Relative moves are subject to pointer acceleration ("Enhance pointer precision"), so "+d then -d" can
    /// end 1-2 px away from the start (measured on Windows 11). Put the pointer back exactly with SetCursorPos,
    /// which is not an input event. Larger offsets are not ours (user, or Mouse Without Borders parking the
    /// pointer) and are left alone. Every non-edge plan with more than one move sums to zero. Steps of different
    /// sizes are accelerated differently, so a natural path can end up to its own excursion away (measured: 8 px
    /// on a 25 px path); offsets up to that excursion are therefore treated as ours.
    /// </summary>
    private static bool RestoreAfterAcceleration(NudgePlan plan, Point origin)
    {
        if (plan.EdgeSafe || plan.Moves.Count < 2 || !NativeMethods.GetCursorPos(out var after))
        {
            return false;
        }

        int dx = after.X - origin.X;
        int dy = after.Y - origin.Y;
        int tolerance = Math.Max(MaxAccelerationDrift, MaxExcursion(plan));
        if ((dx == 0 && dy == 0) || Math.Abs(dx) > tolerance || Math.Abs(dy) > tolerance)
        {
            return false;
        }

        return NativeMethods.SetCursorPos(origin.X, origin.Y);
    }

    private const int MaxAccelerationDrift = 3;

    /// <summary>Largest distance (per axis) the plan takes the pointer away from its starting point.</summary>
    private static int MaxExcursion(NudgePlan plan)
    {
        int x = 0, y = 0, max = 0;
        foreach (Size move in plan.Moves)
        {
            x += move.Width;
            y += move.Height;
            max = Math.Max(max, Math.Max(Math.Abs(x), Math.Abs(y)));
        }

        return max;
    }
}
