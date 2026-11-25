namespace MousePulse.Core;

public enum EngineState
{
    /// <summary>The user has not started simulation.</summary>
    Stopped,

    /// <summary>Running, but the user is active - nothing is simulated.</summary>
    WaitingForIdle,

    /// <summary>Running and the user is idle - periodic nudges are being sent.</summary>
    Simulating,

    /// <summary>Running, but the Windows session is locked - nothing is simulated.</summary>
    PausedSessionLocked,

    /// <summary>Running, but an enforced idle-lock policy is active - nothing is simulated.</summary>
    BlockedByPolicy,
}

public readonly record struct EngineSnapshot(
    EngineState State,
    TimeSpan UserIdle,
    TimeSpan SystemIdle,
    TimeSpan? NextNudgeIn,
    DateTime? LastNudgeAt,
    int NudgeCount,
    bool? LastNudgeRegistered,
    bool NudgedThisTick);

/// <summary>
/// Core state machine. Call <see cref="Tick"/> periodically (the UI uses 1 s).
/// </summary>
/// <remarks>
/// GetLastInputInfo is reset by our own SendInput calls, so the engine keeps its own notion of the user's
/// idle time: a change of the last-input tick is attributed to MousePulse when it falls within
/// <see cref="OwnInputWindowMs"/> after one of our injections; any other change is real user activity
/// (or input forwarded by another tool such as Mouse Without Borders, which is also "someone is here").
/// No input hooks are installed.
/// </remarks>
public sealed class ActivityEngine
{
    /// <summary>Window after an injection in which a last-input change is considered ours.</summary>
    public const int OwnInputWindowMs = 750;

    /// <summary>Tolerance for tick granularity (GetTickCount resolution is ~15.6 ms).</summary>
    private const int TickSlackMs = 32;

    /// <summary>With <see cref="VaryMovement"/>, waits and distances are a random share of the setting from this value to 1.</summary>
    public const double MinVariation = 0.5;

    private readonly ISystemInput _input;
    private readonly Func<DateTime> _clock;
    private readonly Random _random;
    private double _intervalFactor = 1;

    private uint _lastSeenInputTick;
    private uint _lastUserInputTick;
    private uint? _lastNudgeTick;
    private bool _awaitingRegistration;

    public ActivityEngine(ISystemInput input, Func<DateTime>? clock = null, Random? random = null)
    {
        _input = input;
        _clock = clock ?? (() => DateTime.Now);
        _random = random ?? Random.Shared;
        Resync();
    }

    public TimeSpan InactivityThreshold { get; set; } = TimeSpan.FromSeconds(AppLimits.DefaultThresholdSeconds);
    public TimeSpan MoveInterval { get; set; } = TimeSpan.FromSeconds(AppLimits.DefaultIntervalSeconds);
    public int MoveDistance { get; set; } = AppLimits.DefaultDistance;

    /// <summary>
    /// Makes the simulation less regular: each wait is a random 50-100 % of <see cref="MoveInterval"/>, each
    /// move a random 50-100 % of <see cref="MoveDistance"/> with a random, stepped path. The settings remain
    /// upper bounds, so screen saver and Mouse Without Borders timing guarantees still hold.
    /// </summary>
    public bool VaryMovement { get; set; }

    /// <summary>Wait between the last nudge and the next one.</summary>
    private TimeSpan CurrentInterval => VaryMovement ? MoveInterval * _intervalFactor : MoveInterval;

    public bool IsRunning { get; private set; }
    public bool SessionLocked { get; set; }
    public bool BlockedByPolicy { get; set; }

    public DateTime? LastNudgeAt { get; private set; }
    public int NudgeCount { get; private set; }

    /// <summary>Whether Windows registered the last nudge as input (null = unknown yet).</summary>
    public bool? LastNudgeRegistered { get; private set; }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>Re-baselines idle tracking (e.g. after resume from sleep or unlock).</summary>
    public void Resync()
    {
        uint lii = _input.LastInputTick;
        _lastSeenInputTick = lii;
        _lastUserInputTick = lii;
        _lastNudgeTick = null;
        _awaitingRegistration = false;
    }

    public EngineSnapshot Tick()
    {
        ObserveInput();
        uint now = _input.TickCount;
        TimeSpan userIdle = Elapsed(now, _lastUserInputTick);

        EngineState state;
        TimeSpan? next = null;
        bool nudged = false;

        if (!IsRunning)
        {
            state = EngineState.Stopped;
        }
        else if (BlockedByPolicy)
        {
            state = EngineState.BlockedByPolicy;
        }
        else if (SessionLocked)
        {
            state = EngineState.PausedSessionLocked;
        }
        else if (userIdle < InactivityThreshold)
        {
            state = EngineState.WaitingForIdle;
            next = InactivityThreshold - userIdle;
        }
        else
        {
            state = EngineState.Simulating;
            if (IsNudgeDue(now))
            {
                NudgeCore(now);
                nudged = true;
            }

            next = CurrentInterval - Elapsed(_input.TickCount, _lastNudgeTick!.Value);
            if (next < TimeSpan.Zero)
            {
                next = TimeSpan.Zero;
            }
        }

        return new EngineSnapshot(
            state,
            userIdle,
            Elapsed(_input.TickCount, _lastSeenInputTick),
            next,
            LastNudgeAt,
            NudgeCount,
            LastNudgeRegistered,
            nudged);
    }

    /// <summary>
    /// Sends one nudge immediately (used by the "Test move" button). Refused while locked or policy-blocked.
    /// </summary>
    public bool NudgeNow()
    {
        if (SessionLocked || BlockedByPolicy)
        {
            return false;
        }

        ObserveInput();
        NudgeCore(_input.TickCount);
        return true;
    }

    private bool IsNudgeDue(uint now)
    {
        if (_lastNudgeTick is not uint lastNudge)
        {
            return true;
        }

        // A nudge from an earlier idle period does not count: the user came back since then.
        if (unchecked((int)(_lastUserInputTick - lastNudge)) > 0)
        {
            return true;
        }

        return Elapsed(now, lastNudge) >= CurrentInterval;
    }

    private void NudgeCore(uint now)
    {
        int distance = MoveDistance;
        if (VaryMovement && distance > 1)
        {
            distance = _random.Next((int)Math.Ceiling(distance * MinVariation), distance + 1);
        }

        NudgeResult result = _input.Nudge(distance, VaryMovement);
        _intervalFactor = MinVariation + _random.NextDouble() * (1 - MinVariation);
        _lastNudgeTick = now;
        LastNudgeAt = _clock();
        NudgeCount++;

        if (result.Sent)
        {
            _awaitingRegistration = true;
            ObserveInput();
        }
        else
        {
            _awaitingRegistration = false;
            LastNudgeRegistered = false;
        }
    }

    private void ObserveInput()
    {
        uint lii = _input.LastInputTick;
        if (lii != _lastSeenInputTick)
        {
            _lastSeenInputTick = lii;
            if (IsOwnInjection(lii))
            {
                if (_awaitingRegistration)
                {
                    _awaitingRegistration = false;
                    LastNudgeRegistered = true;
                }
            }
            else
            {
                _lastUserInputTick = lii;
            }
        }
        else if (_awaitingRegistration && _lastNudgeTick is uint t &&
                 unchecked((int)(_input.TickCount - t)) > OwnInputWindowMs)
        {
            // Windows never registered the injected events (UIPI, secure desktop, or swallowed by another hook).
            _awaitingRegistration = false;
            LastNudgeRegistered = false;
        }
    }

    private bool IsOwnInjection(uint lii)
    {
        if (_lastNudgeTick is not uint t)
        {
            return false;
        }

        int delta = unchecked((int)(lii - t));
        return delta >= -TickSlackMs && delta <= OwnInputWindowMs;
    }

    private static TimeSpan Elapsed(uint now, uint then)
    {
        int ms = unchecked((int)(now - then));
        return TimeSpan.FromMilliseconds(Math.Max(0, ms));
    }
}
