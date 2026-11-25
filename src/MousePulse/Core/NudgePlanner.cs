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
///
/// Natural mode (a <see cref="Random"/> is passed): the heading is random inside the quadrant that faces the
/// monitor centre, and the trip out and back is split into a few eased steps, so the path is not a fixed
/// diagonal. Every intermediate point stays in that quadrant (never toward an edge) and the pointer still
/// ends where it started.
/// </remarks>
public static class NudgePlanner
{
    /// <summary>Pixels from a monitor edge that count as "on the edge".</summary>
    public const int EdgeMargin = 2;

    /// <summary>Maximum number of steps for each leg (out or back) of a natural path.</summary>
    public const int MaxStepsPerLeg = 4;

    public static NudgePlan Plan(Point cursor, Rectangle monitor, int distance, Random? natural = null)
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

        if (natural is not null)
        {
            return new NudgePlan(NaturalPath(dirX, dirY, distance, natural), EdgeSafe: false, Paced: true);
        }

        return new NudgePlan(
            [new Size(dirX * distance, dirY * distance), new Size(-dirX * distance, -dirY * distance)],
            EdgeSafe: false);
    }

    private static List<Size> NaturalPath(int dirX, int dirY, int distance, Random rng)
    {
        // Random heading inside the quadrant facing the centre. max(cos, sin) >= 0.707, so for distance >= 1
        // at least one component rounds to 1 or more.
        double angle = rng.NextDouble() * Math.PI / 2;
        var target = new Point(
            dirX * (int)Math.Round(distance * Math.Cos(angle)),
            dirY * (int)Math.Round(distance * Math.Sin(angle)));
        if (target == Point.Empty)
        {
            target = new Point(dirX, 0);
        }

        var waypoints = new List<Point> { Point.Empty };
        AddLeg(waypoints, Point.Empty, target, dirX, dirY, distance, rng);
        AddLeg(waypoints, target, Point.Empty, dirX, dirY, distance, rng);

        var moves = new List<Size>(waypoints.Count);
        for (int i = 1; i < waypoints.Count; i++)
        {
            var move = new Size(waypoints[i].X - waypoints[i - 1].X, waypoints[i].Y - waypoints[i - 1].Y);
            if (move != Size.Empty)
            {
                moves.Add(move);
            }
        }

        return moves;
    }

    /// <summary>Appends the waypoints from <paramref name="from"/> (exclusive) to <paramref name="to"/> (inclusive).</summary>
    private static void AddLeg(List<Point> waypoints, Point from, Point to, int dirX, int dirY, int distance, Random rng)
    {
        int steps = rng.Next(1, Math.Clamp(distance / 2, 1, MaxStepsPerLeg) + 1);
        for (int i = 1; i < steps; i++)
        {
            // Ease in/out: small steps at the start and end of the leg, bigger ones in the middle.
            double t = (1 - Math.Cos(Math.PI * i / steps)) / 2;
            int x = from.X + (int)Math.Round((to.X - from.X) * t);
            int y = from.Y + (int)Math.Round((to.Y - from.Y) * t);

            // A 1 px wobble, always toward the centre so the path never gets closer to an edge.
            if (rng.Next(2) == 0)
            {
                x += dirX * rng.Next(2);
            }
            else
            {
                y += dirY * rng.Next(2);
            }

            waypoints.Add(new Point(x, y));
        }

        waypoints.Add(to);
    }
}

/// <param name="Moves">Relative moves, sent in order.</param>
/// <param name="EdgeSafe">True when the cursor was on an edge and only an inward move is sent.</param>
/// <param name="Paced">True when the moves should be sent a few milliseconds apart instead of in one batch.</param>
public sealed record NudgePlan(IReadOnlyList<Size> Moves, bool EdgeSafe, bool Paced = false);
