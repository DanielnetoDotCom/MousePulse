using System.Globalization;
using MousePulse.Native;

namespace MousePulse.Core;

/// <summary>
/// Records the Windows idle timer (GetLastInputInfo) once per second to a CSV file.
/// Run it on the <b>secondary</b> computer to verify whether activity from the primary computer
/// (forwarded by Mouse Without Borders) really resets that computer's idle timer.
/// </summary>
public sealed class IdleProbe : IDisposable
{
    private readonly object _gate = new();
    private readonly ISystemInput _input;
    private readonly StreamWriter _writer;
    private readonly System.Threading.Timer _timer;
    private readonly DateTime _endsAt;
    private uint _lastInputTick;
    private bool _stopped;

    private IdleProbe(string filePath, TimeSpan duration, ISystemInput input)
    {
        _input = input;
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _writer = new StreamWriter(filePath, append: false) { AutoFlush = true };
        _writer.WriteLine("# MousePulse idle probe on " + System.Net.Dns.GetHostName());
        _writer.WriteLine("timestamp,idle_ms,last_input_tick,input_since_previous_sample,cursor_x,cursor_y");
        _lastInputTick = input.LastInputTick;
        _endsAt = DateTime.Now + duration;
        _timer = new System.Threading.Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public string FilePath { get; }

    public int Samples { get; private set; }

    public int Resets { get; private set; }

    /// <summary>Raised (on a thread-pool thread) when the duration elapses.</summary>
    public event EventHandler? Completed;

    public static IdleProbe Start(string filePath, TimeSpan duration, ISystemInput? input = null) =>
        new(filePath, duration, input ?? new Win32SystemInput());

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        $"MousePulse-idle-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

    private void Sample()
    {
        bool finished;
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            uint now = _input.TickCount;
            uint lii = _input.LastInputTick;
            bool changed = lii != _lastInputTick;
            _lastInputTick = lii;
            if (changed)
            {
                Resets++;
            }

            NativeMethods.GetCursorPos(out var p);
            _writer.WriteLine(string.Join(',',
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Math.Max(0, unchecked((int)(now - lii))).ToString(CultureInfo.InvariantCulture),
                lii.ToString(CultureInfo.InvariantCulture),
                changed ? "1" : "0",
                p.X.ToString(CultureInfo.InvariantCulture),
                p.Y.ToString(CultureInfo.InvariantCulture)));
            Samples++;
            finished = DateTime.Now >= _endsAt;
        }

        if (finished)
        {
            Dispose();
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            _timer.Dispose();
            _writer.Dispose();
        }
    }

    /// <summary>
    /// Command line: <c>MousePulse.exe --probe [--seconds N] [--out file.csv]</c>. Runs without UI.
    /// </summary>
    public static int RunCommandLine(string[] args)
    {
        int seconds = 600;
        string path = DefaultPath();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--seconds", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s > 0)
            {
                seconds = s;
            }
            else if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.GetFullPath(args[i + 1]);
            }
        }

        using var done = new ManualResetEventSlim();
        using IdleProbe probe = Start(path, TimeSpan.FromSeconds(seconds));
        probe.Completed += (_, _) => done.Set();
        done.Wait();
        return 0;
    }
}
