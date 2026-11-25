// Manual integration harness: exercises the real Win32 input path on the current machine.
// Usage: dotnet run --project tests/MousePulse.IntegrationHarness [-- --engine-seconds N]
// It moves the pointer by a few pixels (and back) - do not run it on a machine someone else is using.
using System.Diagnostics;
using System.Runtime.InteropServices;
using MousePulse.Core;

int engineSeconds = args.Length >= 2 && args[0] == "--engine-seconds" ? int.Parse(args[1]) : 20;
var input = new Win32SystemInput();

[DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint b, int[] v, uint c);

int[] mouse = new int[3];
SystemParametersInfo(0x0003 /* SPI_GETMOUSE */, 0, mouse, 0);
Console.WriteLine($"Machine: {Environment.MachineName}; Enhance pointer precision: {(mouse[2] != 0 ? "ON" : "off")}; thresholds {mouse[0]}/{mouse[1]}");
Console.WriteLine($"Screens: {string.Join(" | ", Screen.AllScreens.Select(s => $"{s.DeviceName} {s.Bounds}"))}");

Console.WriteLine();
Console.WriteLine("== 1. SendInput resets GetLastInputInfo; pointer returns to origin ==");
int failures = 0;
foreach (int distance in new[] { 0, 1, 3, 10 })
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        // Only measure while nobody touches the mouse/keyboard, otherwise positions are meaningless.
        var wait = Stopwatch.StartNew();
        while (unchecked((int)(input.TickCount - input.LastInputTick)) < 1500)
        {
            if (wait.Elapsed > TimeSpan.FromMinutes(3))
            {
                Console.WriteLine("  (user kept using the PC for 3 min - skipping)");
                return 2;
            }

            Thread.Sleep(100);
        }

        GetCursorPos(out POINT before);
        uint liiBefore = input.LastInputTick;
        uint tickBefore = input.TickCount;
        NudgeResult result = input.Nudge(distance);
        uint liiImmediate = input.LastInputTick;
        var sw = Stopwatch.StartNew();
        while (input.LastInputTick == liiBefore && sw.ElapsedMilliseconds < 1000)
        {
            Thread.Sleep(1);
        }

        Thread.Sleep(200);
        uint liiAfter = input.LastInputTick;
        GetCursorPos(out POINT after);
        if (unchecked((int)(liiAfter - tickBefore)) > 100)
        {
            Console.WriteLine($"  distance={distance}: real input arrived during the measurement - retrying");
            attempt--;
            continue;
        }

        bool registered = liiAfter != liiBefore;
        bool returned = result.EdgeSafe || (before.X == after.X && before.Y == after.Y);
        int liiDelta = unchecked((int)(liiAfter - tickBefore));
        if (!result.Sent || !registered || !returned || liiDelta > ActivityEngine.OwnInputWindowMs)
        {
            failures++;
        }

        Console.WriteLine(
            $"distance={distance,2} sent={result.Sent} events={result.EventCount} edgeSafe={result.EdgeSafe} corrected={result.Corrected} " +
            $"registered={registered} (sync={liiImmediate != liiBefore}, lii-tickBefore={liiDelta} ms, waited {sw.ElapsedMilliseconds} ms) " +
            $"pos {before.X},{before.Y} -> {after.X},{after.Y} returned={returned}");
    }
}

Console.WriteLine();
Console.WriteLine("== 1b. Cumulative drift over 40 consecutive 1 px moves ==");
{
    while (unchecked((int)(input.TickCount - input.LastInputTick)) < 1500)
    {
        Thread.Sleep(100);
    }

    GetCursorPos(out POINT start);
    int corrections = 0;
    bool interrupted = false;
    for (int i = 0; i < 40; i++)
    {
        uint t0 = input.TickCount;
        if (input.Nudge(1).Corrected)
        {
            corrections++;
        }

        Thread.Sleep(100);
        if (unchecked((int)(input.LastInputTick - t0)) > 50)
        {
            interrupted = true;
            break;
        }
    }

    GetCursorPos(out POINT end);
    bool noDrift = start.X == end.X && start.Y == end.Y;
    if (!interrupted && !noDrift)
    {
        failures++;
    }

    Console.WriteLine(interrupted
        ? "  interrupted by real input - not evaluated"
        : $"  start {start.X},{start.Y} end {end.X},{end.Y} drift={(noDrift ? "none" : "YES")} (acceleration corrections applied: {corrections}/40)");
}

Console.WriteLine();
Console.WriteLine($"== 2. ActivityEngine live run: threshold 5 s, interval 3 s, {engineSeconds} s ==");
var engine = new ActivityEngine(input) { InactivityThreshold = TimeSpan.FromSeconds(5), MoveInterval = TimeSpan.FromSeconds(3), MoveDistance = 1 };
engine.Start();
var cpu = Process.GetCurrentProcess().TotalProcessorTime;
for (int i = 0; i <= engineSeconds; i++)
{
    EngineSnapshot s = engine.Tick();
    Console.WriteLine($"t={i,3}s state={s.State,-15} userIdle={s.UserIdle.TotalSeconds,6:0.0}s systemIdle={s.SystemIdle.TotalSeconds,5:0.0}s nudged={(s.NudgedThisTick ? "YES" : "-"),-3} count={s.NudgeCount} registered={s.LastNudgeRegistered?.ToString() ?? "?"}");
    Thread.Sleep(1000);
}

Console.WriteLine();
Console.WriteLine($"== 3. MWB detection ==");
MwbStatus mwb = MwbDetector.Detect();
Console.WriteLine($"{mwb.Presence}; peers={string.Join(",", mwb.ConfiguredPeers)}; addresses={string.Join(",", mwb.ConnectedAddresses)}; blockScreenSaver={mwb.BlockScreenSaverOnOtherMachines}; relative={mwb.MoveMouseRelatively}; port={mwb.TcpPort}");
Console.WriteLine(MwbAdvice.For(mwb, 30).Headline);
Console.WriteLine($"Policy: {(PolicyInspector.Inspect() is { BlocksSimulation: true } p ? string.Join("; ", p.EnforcedIdleLocks) : "no enforced idle lock")}");
Console.WriteLine($"Screen saver timeout: {ScreenSaverInfo.GetTimeout()?.ToString() ?? "disabled"}");

Console.WriteLine();
Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: {failures} injection check(s) failed");
return failures == 0 ? 0 : 1;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}
