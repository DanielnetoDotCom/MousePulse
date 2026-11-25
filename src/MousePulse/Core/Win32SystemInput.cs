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

    public unsafe NudgeResult Nudge(int distance)
    {
        Point cursor = NativeMethods.GetCursorPos(out var p) ? new Point(p.X, p.Y) : Cursor.Position;
        Rectangle monitor = Screen.FromPoint(cursor).Bounds;
        NudgePlan plan = NudgePlanner.Plan(cursor, monitor, distance);

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

        uint sent = NativeMethods.SendInput((uint)count, inputs, sizeof(NativeMethods.INPUT));
        int error = sent == count ? 0 : Marshal.GetLastPInvokeError();
        bool corrected = sent == count && RestoreAfterAcceleration(plan, cursor);
        return new NudgeResult(sent == count, count, plan.EdgeSafe, error, corrected);
    }

    /// <summary>
    /// Relative moves are subject to pointer acceleration ("Enhance pointer precision"), so "+d then -d" can
    /// end 1-2 px away from the start (measured on Windows 11). Put the pointer back exactly with SetCursorPos,
    /// which is not an input event. Larger offsets are not ours (user, or Mouse Without Borders parking the
    /// pointer) and are left alone.
    /// </summary>
    private static bool RestoreAfterAcceleration(NudgePlan plan, Point origin)
    {
        if (plan.EdgeSafe || plan.Moves.Count != 2 || !NativeMethods.GetCursorPos(out var after))
        {
            return false;
        }

        int dx = after.X - origin.X;
        int dy = after.Y - origin.Y;
        if ((dx == 0 && dy == 0) || Math.Abs(dx) > MaxAccelerationDrift || Math.Abs(dy) > MaxAccelerationDrift)
        {
            return false;
        }

        return NativeMethods.SetCursorPos(origin.X, origin.Y);
    }

    private const int MaxAccelerationDrift = 3;
}
