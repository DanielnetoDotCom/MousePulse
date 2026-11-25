using MousePulse.Core;

namespace MousePulse.Tests;

public class ActivityEngineTests
{
    private static (ActivityEngine Engine, FakeSystemInput Input) Create(int threshold = 60, int interval = 30, uint start = 1_000_000, int seed = 1)
    {
        var input = new FakeSystemInput(start);
        var engine = new ActivityEngine(input, () => new DateTime(2026, 1, 1, 12, 0, 0), new Random(seed))
        {
            InactivityThreshold = TimeSpan.FromSeconds(threshold),
            MoveInterval = TimeSpan.FromSeconds(interval),
            MoveDistance = 1,
        };
        return (engine, input);
    }

    /// <summary>Ticks once per second for <paramref name="seconds"/> seconds.</summary>
    private static EngineSnapshot Run(ActivityEngine engine, FakeSystemInput input, int seconds)
    {
        EngineSnapshot last = engine.Tick();
        for (int i = 0; i < seconds; i++)
        {
            input.AdvanceSeconds(1);
            last = engine.Tick();
        }

        return last;
    }

    [Fact]
    public void Stopped_engine_never_injects()
    {
        var (engine, input) = Create();
        EngineSnapshot s = Run(engine, input, 600);

        Assert.Equal(EngineState.Stopped, s.State);
        Assert.Empty(input.Nudges);
        Assert.Equal(TimeSpan.FromSeconds(600), s.UserIdle);
    }

    [Fact]
    public void Active_user_is_never_disturbed()
    {
        var (engine, input) = Create();
        engine.Start();
        for (int i = 0; i < 300; i++)
        {
            input.AdvanceSeconds(1);
            if (i % 20 == 0)
            {
                input.UserInput();
            }

            Assert.NotEqual(EngineState.Simulating, engine.Tick().State);
        }

        Assert.Empty(input.Nudges);
    }

    [Fact]
    public void First_move_happens_when_threshold_is_reached()
    {
        var (engine, input) = Create(threshold: 60);
        engine.Start();

        EngineSnapshot s = Run(engine, input, 59);
        Assert.Equal(EngineState.WaitingForIdle, s.State);
        Assert.Empty(input.Nudges);
        Assert.Equal(TimeSpan.FromSeconds(1), s.NextNudgeIn);

        input.AdvanceSeconds(1);
        s = engine.Tick();
        Assert.Equal(EngineState.Simulating, s.State);
        Assert.True(s.NudgedThisTick);
        Assert.Single(input.Nudges);
    }

    [Fact]
    public void Moves_repeat_at_the_configured_interval()
    {
        var (engine, input) = Create(threshold: 60, interval: 30);
        engine.Start();
        Run(engine, input, 60 + 300);

        // t=60, 90, ..., 360 => 11 moves
        Assert.Equal(11, input.Nudges.Count);
    }

    [Fact]
    public void Own_moves_do_not_count_as_user_activity()
    {
        var (engine, input) = Create(threshold: 60, interval: 30);
        engine.Start();
        EngineSnapshot s = Run(engine, input, 200);

        Assert.Equal(EngineState.Simulating, s.State);
        Assert.Equal(TimeSpan.FromSeconds(200), s.UserIdle);
        Assert.True(s.SystemIdle < TimeSpan.FromSeconds(30), "Windows idle timer must be kept low by the moves");
        Assert.True(s.LastNudgeRegistered);
    }

    [Fact]
    public void Real_input_stops_simulation_and_restarts_the_threshold()
    {
        var (engine, input) = Create(threshold: 60, interval: 30);
        engine.Start();
        Run(engine, input, 100);
        int before = input.Nudges.Count;

        input.AdvanceSeconds(1);
        input.UserInput();
        EngineSnapshot s = engine.Tick();
        Assert.Equal(EngineState.WaitingForIdle, s.State);
        Assert.Equal(TimeSpan.Zero, s.UserIdle);

        s = Run(engine, input, 59);
        Assert.Equal(EngineState.WaitingForIdle, s.State);
        Assert.Equal(before, input.Nudges.Count);

        // A new idle period starts with an immediate move, regardless of when the previous move happened.
        input.AdvanceSeconds(1);
        s = engine.Tick();
        Assert.True(s.NudgedThisTick);
        Assert.Equal(before + 1, input.Nudges.Count);
    }

    [Fact]
    public void User_input_shortly_after_a_move_is_detected_on_continued_activity()
    {
        var (engine, input) = Create(threshold: 60, interval: 30);
        engine.Start();
        Run(engine, input, 60); // move at t=60

        // The user moves the mouse continuously: inputs beyond the own-input window are detected.
        input.Advance(TimeSpan.FromMilliseconds(400));
        input.UserInput();
        engine.Tick();
        input.Advance(TimeSpan.FromMilliseconds(600));
        input.UserInput();
        EngineSnapshot s = engine.Tick();

        Assert.Equal(EngineState.WaitingForIdle, s.State);
    }

    [Fact]
    public void Locked_session_pauses_simulation()
    {
        var (engine, input) = Create(threshold: 10, interval: 10);
        engine.Start();
        engine.SessionLocked = true;
        EngineSnapshot s = Run(engine, input, 300);

        Assert.Equal(EngineState.PausedSessionLocked, s.State);
        Assert.Empty(input.Nudges);
        Assert.False(engine.NudgeNow());
    }

    [Fact]
    public void Enforced_policy_blocks_simulation()
    {
        var (engine, input) = Create(threshold: 10, interval: 10);
        engine.Start();
        engine.BlockedByPolicy = true;
        EngineSnapshot s = Run(engine, input, 300);

        Assert.Equal(EngineState.BlockedByPolicy, s.State);
        Assert.Empty(input.Nudges);
        Assert.False(engine.NudgeNow());
    }

    [Fact]
    public void Unregistered_moves_are_reported()
    {
        var (engine, input) = Create(threshold: 10, interval: 10);
        input.InjectionRegisters = false;
        engine.Start();
        EngineSnapshot s = Run(engine, input, 12);

        Assert.NotEmpty(input.Nudges);
        Assert.False(s.LastNudgeRegistered);
        Assert.Equal(TimeSpan.FromSeconds(12), s.SystemIdle);
    }

    [Fact]
    public void Failed_SendInput_is_reported_immediately()
    {
        var (engine, input) = Create(threshold: 10, interval: 10);
        input.SendInputSucceeds = false;
        engine.Start();
        EngineSnapshot s = Run(engine, input, 10);

        Assert.True(s.NudgedThisTick);
        Assert.False(s.LastNudgeRegistered);
    }

    [Fact]
    public void Works_across_GetTickCount_wraparound()
    {
        var (engine, input) = Create(threshold: 60, interval: 30, start: uint.MaxValue - 30_000);
        engine.Start();
        EngineSnapshot s = Run(engine, input, 120);

        Assert.Equal(EngineState.Simulating, s.State);
        Assert.Equal(TimeSpan.FromSeconds(120), s.UserIdle);
        Assert.Equal(3, input.Nudges.Count); // t=60, 90, 120
    }

    [Fact]
    public void Test_move_works_while_user_is_active_and_is_not_user_activity()
    {
        var (engine, input) = Create(threshold: 60, interval: 30);
        engine.Start();
        Run(engine, input, 20);

        Assert.True(engine.NudgeNow());
        input.AdvanceSeconds(1);
        EngineSnapshot s = engine.Tick();

        Assert.Single(input.Nudges);
        Assert.Equal(1, s.NudgeCount);
        Assert.Equal(TimeSpan.FromSeconds(21), s.UserIdle);
        Assert.True(s.LastNudgeRegistered);
    }

    [Fact]
    public void Fixed_mode_sends_the_exact_distance_at_the_exact_interval()
    {
        var (engine, input) = Create(threshold: 60, interval: 30);
        engine.MoveDistance = 10;
        engine.Start();
        Run(engine, input, 600);

        Assert.All(input.Nudges, d => Assert.Equal(10, d));
        Assert.All(input.NudgeNatural, Assert.False);
        Assert.All(Gaps(input), g => Assert.Equal(30, g));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Varied_mode_randomizes_interval_and_distance_within_the_settings(int seed)
    {
        var (engine, input) = Create(threshold: 60, interval: 30, seed: seed);
        engine.MoveDistance = 10;
        engine.VaryMovement = true;
        engine.Start();
        Run(engine, input, 1800);

        Assert.True(input.Nudges.Count > 60);                    // ~1 per 22.5 s on average
        Assert.All(input.Nudges, d => Assert.InRange(d, 5, 10));
        Assert.True(input.Nudges.Distinct().Count() > 1);
        Assert.All(input.NudgeNatural, Assert.True);

        int[] gaps = Gaps(input);
        Assert.All(gaps, g => Assert.InRange(g, 15, 30));       // never longer than "Move every"
        Assert.True(gaps.Distinct().Count() > 3);
    }

    [Fact]
    public void Varied_mode_keeps_one_pixel_moves_at_one_pixel()
    {
        var (engine, input) = Create(threshold: 60, interval: 30);
        engine.VaryMovement = true;
        engine.Start();
        Run(engine, input, 600);

        Assert.All(input.Nudges, d => Assert.Equal(1, d));
    }

    [Fact]
    public void Varied_mode_reports_a_next_move_within_the_interval()
    {
        var (engine, input) = Create(threshold: 60, interval: 30);
        engine.VaryMovement = true;
        engine.Start();
        EngineSnapshot s = Run(engine, input, 60);

        Assert.True(s.NudgedThisTick);
        Assert.InRange(s.NextNudgeIn!.Value, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
    }

    /// <summary>Seconds between consecutive nudges.</summary>
    private static int[] Gaps(FakeSystemInput input) =>
        input.NudgeTicks.Zip(input.NudgeTicks.Skip(1), (a, b) => (int)((b - a) / 1000)).ToArray();

    [Fact]
    public void Settings_changes_apply_immediately()
    {
        var (engine, input) = Create(threshold: 600, interval: 30);
        engine.Start();
        Run(engine, input, 120);
        Assert.Empty(input.Nudges);

        engine.InactivityThreshold = TimeSpan.FromSeconds(60);
        input.AdvanceSeconds(1);
        Assert.True(engine.Tick().NudgedThisTick);
    }
}
