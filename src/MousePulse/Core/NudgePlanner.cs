using System.Drawing;

namespace MousePulse.Core;

/// <summary>
/// Decides which relative mouse moves make up one "nudge".
/// </summary>
/// <remarks>
/// Normal case: move <c>distance</c> pixels toward the centre of the current monitor and straight back,
/// so the pointer ends where it started and never approaches a screen edge.
///
/// Edge case: Mouse Without Borders switches machines based on the pointer <i>position</i> (any move event
/// while the pointer sits on the edge pixel facing a neighbour can trigger a switch). If the pointer is
/// already on/near an edge, only a single inward move is sent and the pointer is NOT returned to the edge.
/// </remarks>
public static class NudgePlanner
{
    /// <summary>Pixels from a monitor edge that count as "on the edge".</summary>
    public const int EdgeMargin = 2;

    public static NudgePlan Plan(Point cursor, Rectangle monitor, int distance)
    {
        distance = Math.Clamp(distance, 0, AppLimits.MaxDistance);

        bool onEdge = cursor.X < monitor.Left + EdgeMargin || cursor.X >= monitor.Right - EdgeMargin ||
                      cursor.Y < monitor.Top + EdgeMargin || cursor.Y >= monitor.Bottom - EdgeMargin;

        int dirX = cursor.X < monitor.Left + monitor.Width / 2 ? 1 : -1;
        int dirY = cursor.Y < monitor.Top + monitor.Height / 2 ? 1 : -1;

        if (onEdge)
        {
            // Step inward far enough to leave the edge zone; never go back.
            int step = Math.Max(distance, EdgeMargin);
            return new NudgePlan([new Size(dirX * step, dirY * step)], EdgeSafe: true);
        }

        if (distance == 0)
        {
            // Zero-length move: still an input event for Windows, no visible pointer motion.
            return new NudgePlan([Size.Empty], EdgeSafe: false);
        }

        return new NudgePlan(
            [new Size(dirX * distance, dirY * distance), new Size(-dirX * distance, -dirY * distance)],
            EdgeSafe: false);
    }
}

public sealed record NudgePlan(IReadOnlyList<Size> Moves, bool EdgeSafe);
