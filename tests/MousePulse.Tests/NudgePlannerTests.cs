using System.Drawing;
using MousePulse.Core;

namespace MousePulse.Tests;

public class NudgePlannerTests
{
    private static readonly Rectangle Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Centre_area_moves_out_and_back()
    {
        NudgePlan plan = NudgePlanner.Plan(new Point(500, 400), Monitor, 3);

        Assert.False(plan.EdgeSafe);
        Assert.Equal(2, plan.Moves.Count);
        Assert.Equal(new Size(3, 3), plan.Moves[0]);          // toward the centre
        Assert.Equal(new Size(-3, -3), plan.Moves[1]);        // and back
    }

    [Fact]
    public void Moves_point_toward_the_monitor_centre()
    {
        NudgePlan plan = NudgePlanner.Plan(new Point(1800, 1000), Monitor, 1);
        Assert.Equal(new Size(-1, -1), plan.Moves[0]);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(1919, 500)]
    [InlineData(800, 0)]
    [InlineData(800, 1079)]
    [InlineData(1919, 1079)]
    public void Edge_positions_only_step_inward(int x, int y)
    {
        NudgePlan plan = NudgePlanner.Plan(new Point(x, y), Monitor, 1);

        Assert.True(plan.EdgeSafe);
        Size move = Assert.Single(plan.Moves);
        Point after = new(x + move.Width, y + move.Height);
        Assert.InRange(after.X, Monitor.Left + NudgePlanner.EdgeMargin, Monitor.Right - NudgePlanner.EdgeMargin - 1);
        Assert.InRange(after.Y, Monitor.Top + NudgePlanner.EdgeMargin, Monitor.Bottom - NudgePlanner.EdgeMargin - 1);
    }

    [Fact]
    public void Zero_distance_sends_a_single_zero_move()
    {
        NudgePlan plan = NudgePlanner.Plan(new Point(500, 400), Monitor, 0);
        Assert.Equal(Size.Empty, Assert.Single(plan.Moves));
    }

    [Fact]
    public void Zero_distance_on_an_edge_still_leaves_the_edge()
    {
        NudgePlan plan = NudgePlanner.Plan(new Point(0, 0), Monitor, 0);
        Assert.Equal(new Size(NudgePlanner.EdgeMargin, NudgePlanner.EdgeMargin), Assert.Single(plan.Moves));
    }

    [Fact]
    public void Secondary_monitor_with_negative_coordinates()
    {
        var left = new Rectangle(-2560, 0, 2560, 1440);
        NudgePlan plan = NudgePlanner.Plan(new Point(-2559, 700), left, 1);

        Assert.True(plan.EdgeSafe);
        Assert.True(plan.Moves[0].Width > 0);
    }

    [Fact]
    public void Distance_is_clamped()
    {
        NudgePlan plan = NudgePlanner.Plan(new Point(900, 500), Monitor, 10_000);
        Assert.Equal(AppLimits.MaxDistance, plan.Moves[0].Width);
    }
}
