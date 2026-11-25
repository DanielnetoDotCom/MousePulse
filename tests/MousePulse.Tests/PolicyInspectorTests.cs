using Microsoft.Win32;
using MousePulse.Core;

namespace MousePulse.Tests;

public class PolicyInspectorTests
{
    private static PolicyStatus Inspect(params (RegistryHive Hive, string Key, string Name, object Value)[] values)
    {
        var map = values.ToDictionary(v => (v.Hive, v.Key, v.Name), v => v.Value);
        return PolicyInspector.Inspect((hive, key, name) => map.TryGetValue((hive, key, name), out object? v) ? v : null);
    }

    [Fact]
    public void Clean_machine_does_not_block()
    {
        Assert.False(Inspect().BlocksSimulation);
    }

    [Fact]
    public void Machine_inactivity_limit_blocks()
    {
        PolicyStatus status = Inspect((RegistryHive.LocalMachine, PolicyInspector.MachineSystemPolicyKey, "InactivityTimeoutSecs", 900));
        Assert.True(status.BlocksSimulation);
        Assert.Contains("15 min", status.EnforcedIdleLocks[0]);
    }

    [Fact]
    public void Zero_machine_inactivity_limit_means_not_configured()
    {
        Assert.False(Inspect((RegistryHive.LocalMachine, PolicyInspector.MachineSystemPolicyKey, "InactivityTimeoutSecs", 0)).BlocksSimulation);
    }

    [Fact]
    public void Enforced_secure_screen_saver_blocks_REG_SZ_values()
    {
        PolicyStatus status = Inspect(
            (RegistryHive.CurrentUser, PolicyInspector.DesktopPolicyKey, "ScreenSaveActive", "1"),
            (RegistryHive.CurrentUser, PolicyInspector.DesktopPolicyKey, "ScreenSaverIsSecure", "1"),
            (RegistryHive.CurrentUser, PolicyInspector.DesktopPolicyKey, "ScreenSaveTimeOut", "600"));
        Assert.True(status.BlocksSimulation);
        Assert.Contains("10 min", status.EnforcedIdleLocks[0]);
    }

    [Fact]
    public void Password_policy_without_enforced_screen_saver_does_not_block()
    {
        // The user can still turn the screen saver off, so no idle lock is enforced.
        Assert.False(Inspect((RegistryHive.CurrentUser, PolicyInspector.DesktopPolicyKey, "ScreenSaverIsSecure", "1")).BlocksSimulation);
    }

    [Fact]
    public void Mdm_device_lock_blocks()
    {
        PolicyStatus status = Inspect((RegistryHive.LocalMachine, PolicyInspector.MdmDeviceLockKey, "MaxInactivityTimeDeviceLock", 5));
        Assert.True(status.BlocksSimulation);
        Assert.Contains("5 min", status.EnforcedIdleLocks[0]);
    }

    [Fact]
    public void Real_registry_can_be_read_without_admin_rights()
    {
        // Smoke test against the actual machine: must not throw (result depends on the machine).
        _ = PolicyInspector.Inspect();
    }
}
