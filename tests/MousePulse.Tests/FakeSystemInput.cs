using MousePulse.Core;

namespace MousePulse.Tests;

/// <summary>Deterministic stand-in for GetTickCount / GetLastInputInfo / SendInput.</summary>
internal sealed class FakeSystemInput : ISystemInput
{
    public FakeSystemInput(uint start = 1_000_000)
    {
        TickCount = start;
        LastInputTick = start;
    }

    public uint TickCount { get; set; }

    public uint LastInputTick { get; set; }

    /// <summary>When false, simulates Windows ignoring injected input (UIPI, secure desktop...).</summary>
    public bool InjectionRegisters { get; set; } = true;

    public bool SendInputSucceeds { get; set; } = true;

    public List<int> Nudges { get; } = [];

    /// <summary>Tick at which each nudge was sent (parallel to <see cref="Nudges"/>).</summary>
    public List<uint> NudgeTicks { get; } = [];

    /// <summary>The "natural" flag passed with each nudge (parallel to <see cref="Nudges"/>).</summary>
    public List<bool> NudgeNatural { get; } = [];

    public NudgeResult Nudge(int distance, bool natural = false)
    {
        Nudges.Add(distance);
        NudgeTicks.Add(TickCount);
        NudgeNatural.Add(natural);
        if (SendInputSucceeds && InjectionRegisters)
        {
            LastInputTick = TickCount;
        }

        return new NudgeResult(SendInputSucceeds, 2, false, SendInputSucceeds ? 0 : 5);
    }

    public void Advance(TimeSpan by) => TickCount = unchecked(TickCount + (uint)by.TotalMilliseconds);

    public void AdvanceSeconds(int seconds) => Advance(TimeSpan.FromSeconds(seconds));

    public void UserInput() => LastInputTick = TickCount;
}
