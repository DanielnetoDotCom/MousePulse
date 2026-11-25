using MousePulse.Core;
using MousePulse.Settings;
using MousePulse.UI;

namespace MousePulse;

internal static class Program
{
    private const string InstanceMutexName = @"Local\MousePulse.SingleInstance";
    private const string ActivateEventName = @"Local\MousePulse.Activate";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase)))
        {
            return IdleProbe.RunCommandLine(args);
        }

        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Bring the already running instance to the front instead of starting a second engine.
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out EventWaitHandle? existing))
            {
                using (existing)
                {
                    existing.Set();
                }
            }

            return 0;
        }

        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        bool startInTray = args.Any(a => a.Equals(StartupRegistration.TrayArgument, StringComparison.OrdinalIgnoreCase));

        ApplicationConfiguration.Initialize();
        using var form = new MainForm(startInTray, activate);
        Application.Run(form);

        GC.KeepAlive(mutex);
        return 0;
    }
}
