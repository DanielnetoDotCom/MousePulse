using Microsoft.Win32;

namespace MousePulse.Core;

/// <summary>
/// Detects administrator-enforced idle-lock policies. When one is present MousePulse refuses to simulate
/// activity, because doing so would defeat a security control that the machine owner put in place.
/// Only the registry is read; nothing is modified.
/// </summary>
public static class PolicyInspector
{
    /// <summary>Reads a registry value: (hive, subkey, value name) -> raw value or null.</summary>
    public delegate object? RegistryReader(RegistryHive hive, string subKey, string valueName);

    internal const string MachineSystemPolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    internal const string DesktopPolicyKey = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";
    internal const string MdmDeviceLockKey = @"SOFTWARE\Microsoft\PolicyManager\current\device\DeviceLock";

    public static PolicyStatus Inspect(RegistryReader? reader = null)
    {
        reader ??= ReadRegistry;
        var findings = new List<string>();

        // GPO: Computer Configuration > Windows Settings > Security Settings > Local Policies >
        //      Security Options > "Interactive logon: Machine inactivity limit".
        int? machineLimit = AsInt(reader(RegistryHive.LocalMachine, MachineSystemPolicyKey, "InactivityTimeoutSecs"));
        if (machineLimit > 0)
        {
            findings.Add($"Machine inactivity limit ({FormatSeconds(machineLimit.Value)})");
        }

        // GPO: User Configuration > Administrative Templates > Control Panel > Personalization >
        //      "Enable screen saver" + "Password protect the screen saver".
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            int? active = AsInt(reader(hive, DesktopPolicyKey, "ScreenSaveActive"));
            int? secure = AsInt(reader(hive, DesktopPolicyKey, "ScreenSaverIsSecure"));
            if (active == 1 && secure == 1)
            {
                int? timeout = AsInt(reader(hive, DesktopPolicyKey, "ScreenSaveTimeOut"));
                string suffix = timeout > 0 ? $" ({FormatSeconds(timeout.Value)})" : string.Empty;
                findings.Add($"Enforced password-protected screen saver{suffix}");
                break;
            }
        }

        // MDM (Intune etc.): DeviceLock/MaxInactivityTimeDeviceLock, in minutes.
        int? mdmMinutes = AsInt(reader(RegistryHive.LocalMachine, MdmDeviceLockKey, "MaxInactivityTimeDeviceLock"));
        if (mdmMinutes > 0)
        {
            findings.Add($"MDM device lock after inactivity ({FormatSeconds(mdmMinutes.Value * 60)})");
        }

        return new PolicyStatus(findings);
    }

    private static object? ReadRegistry(RegistryHive hive, string subKey, string valueName)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey? key = baseKey.OpenSubKey(subKey, writable: false);
            return key?.GetValue(valueName);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    // Policy values are REG_DWORD or REG_SZ depending on the template that wrote them.
    private static int? AsInt(object? value) => value switch
    {
        int i => i,
        long l => (int)l,
        string s when int.TryParse(s.Trim(), out int parsed) => parsed,
        _ => null,
    };

    private static string FormatSeconds(int seconds) =>
        seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds} s";
}

public sealed record PolicyStatus(IReadOnlyList<string> EnforcedIdleLocks)
{
    public bool BlocksSimulation => EnforcedIdleLocks.Count > 0;
}
