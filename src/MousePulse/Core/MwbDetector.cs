using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace MousePulse.Core;

public enum MwbPresence
{
    NotInstalled,
    InstalledNotRunning,
    RunningNotConnected,
    Connected,
}

public sealed record MwbStatus(
    MwbPresence Presence,
    string Edition,
    IReadOnlyList<string> ConfiguredPeers,
    IReadOnlyList<IPAddress> ConnectedAddresses,
    bool? BlockScreenSaverOnOtherMachines,
    bool? MoveMouseRelatively,
    int TcpPort);

/// <summary>
/// Read-only, local detection of Microsoft PowerToys Mouse Without Borders (or the legacy standalone app).
/// Uses the process list, the local PowerToys settings file and the local TCP connection table.
/// MousePulse never opens any network connection itself.
/// </summary>
public static class MwbDetector
{
    private const int DefaultTcpPort = 15100;

    private static readonly string[] PowerToysProcesses =
        ["PowerToys.MouseWithoutBorders", "PowerToys.MouseWithoutBordersService"];

    private static readonly string[] LegacyProcesses = ["MouseWithoutBorders", "MouseWithoutBordersSvc"];

    public static MwbStatus Detect()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string settingsPath = Path.Combine(localAppData, "Microsoft", "PowerToys", "MouseWithoutBorders", "settings.json");

        HashSet<string> running = RunningProcessNames();
        bool powerToysRunning = PowerToysProcesses.Any(running.Contains);
        bool legacyRunning = !powerToysRunning && LegacyProcesses.Any(running.Contains);
        bool installed = powerToysRunning || legacyRunning || File.Exists(settingsPath) || LegacyInstalled();
        string edition = legacyRunning ? "Mouse without Borders (standalone)" : "PowerToys Mouse Without Borders";

        var settings = powerToysRunning || File.Exists(settingsPath) ? ReadSettings(settingsPath) : MwbSettings.Empty;

        if (!installed)
        {
            return new MwbStatus(MwbPresence.NotInstalled, edition, [], [], null, null, DefaultTcpPort);
        }

        if (!powerToysRunning && !legacyRunning)
        {
            return new MwbStatus(MwbPresence.InstalledNotRunning, edition, settings.Peers, [],
                settings.BlockScreenSaver, settings.MoveRelatively, settings.TcpPort);
        }

        IReadOnlyList<IPAddress> addresses = FindConnections(settings.TcpPort);
        return new MwbStatus(
            addresses.Count > 0 ? MwbPresence.Connected : MwbPresence.RunningNotConnected,
            edition,
            settings.Peers,
            addresses,
            settings.BlockScreenSaver,
            settings.MoveRelatively,
            settings.TcpPort);
    }

    /// <summary>
    /// MWB listens on TCPPort (clipboard/files) and TCPPort+1 (input/messages). Established, non-loopback
    /// connections on either port indicate a live link to another machine.
    /// </summary>
    private static IReadOnlyList<IPAddress> FindConnections(int tcpPort)
    {
        try
        {
            var ports = new HashSet<int> { tcpPort, tcpPort + 1 };
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Where(c => c.State == TcpState.Established && !IPAddress.IsLoopback(c.RemoteEndPoint.Address) &&
                            (ports.Contains(c.LocalEndPoint.Port) || ports.Contains(c.RemoteEndPoint.Port)))
                .Select(c => c.RemoteEndPoint.Address.IsIPv4MappedToIPv6 ? c.RemoteEndPoint.Address.MapToIPv4() : c.RemoteEndPoint.Address)
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>One process-list snapshot (each GetProcessesByName call would enumerate every process again).</summary>
    private static HashSet<string> RunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process p in Process.GetProcesses())
        {
            using (p)
            {
                names.Add(p.ProcessName);
            }
        }

        return names;
    }

    private static bool LegacyInstalled()
    {
        string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return File.Exists(Path.Combine(x86, "Microsoft Garage", "Mouse without Borders", "MouseWithoutBorders.exe"));
    }

    internal static MwbSettings ReadSettings(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return ParseSettings(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return MwbSettings.Empty;
        }
    }

    internal static MwbSettings ParseSettings(Stream json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("properties", out JsonElement props))
        {
            return MwbSettings.Empty;
        }

        var peers = new List<string>();
        if (props.TryGetProperty("MachineMatrixString", out JsonElement matrix) && matrix.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement m in matrix.EnumerateArray())
            {
                string? name = m.ValueKind == JsonValueKind.String ? m.GetString()?.Trim() : null;
                if (!string.IsNullOrEmpty(name) && !IsThisMachine(name))
                {
                    peers.Add(name);
                }
            }
        }

        int port = ReadValue(props, "TCPPort") is { ValueKind: JsonValueKind.Number } p && p.TryGetInt32(out int v) && v is > 0 and < 65535
            ? v
            : DefaultTcpPort;

        return new MwbSettings(peers, ReadBool(props, "BlockScreenSaverOnOtherMachines"), ReadBool(props, "MoveMouseRelatively"), port);
    }

    /// <summary>
    /// MWB stores the full host name, while Environment.MachineName is the NetBIOS name, which is truncated
    /// to 15 characters (e.g. "PC-Desktop-Office1" vs "PC-DESKTOP-OFFI"). Both exact forms are compared;
    /// no prefix matching, so a peer that merely shares the first 15 characters is not mistaken for this PC.
    /// </summary>
    internal static bool IsThisMachine(string name, string? hostName = null, string? netBiosName = null)
    {
        hostName ??= SafeHostName();
        netBiosName ??= Environment.MachineName;
        return string.Equals(name, hostName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, netBiosName, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeHostName()
    {
        try
        {
            return Dns.GetHostName(); // Local call: reads the configured host name, no network traffic.
        }
        catch (System.Net.Sockets.SocketException)
        {
            return Environment.MachineName;
        }
    }

    private static JsonElement? ReadValue(JsonElement props, string name) =>
        props.TryGetProperty(name, out JsonElement prop) && prop.ValueKind == JsonValueKind.Object &&
        prop.TryGetProperty("value", out JsonElement value)
            ? value
            : null;

    private static bool? ReadBool(JsonElement props, string name) => ReadValue(props, name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => null,
    };

    internal sealed record MwbSettings(IReadOnlyList<string> Peers, bool? BlockScreenSaver, bool? MoveRelatively, int TcpPort)
    {
        public static readonly MwbSettings Empty = new([], null, null, DefaultTcpPort);
    }
}
