namespace MousePulse.Core;

public enum AdviceLevel
{
    Neutral,
    Good,
    Warning,
}

public sealed record Advice(string Headline, string Detail, AdviceLevel Level);

/// <summary>
/// Turns a <see cref="MwbStatus"/> into a user-facing compatibility explanation.
/// The behaviour described here was established from the PowerToys MWB source code
/// (see docs/MWB-COMPATIBILITY.md) and must stay consistent with that document.
/// </summary>
public static class MwbAdvice
{
    public const int AwakeBeatSeconds = 60;

    public static Advice For(MwbStatus status, int moveIntervalSeconds)
    {
        switch (status.Presence)
        {
            case MwbPresence.NotInstalled:
                return new Advice("Not detected",
                    "Mouse Without Borders is not installed. Simulated activity affects this PC only.",
                    AdviceLevel.Neutral);

            case MwbPresence.InstalledNotRunning:
                return new Advice("Installed, not running",
                    "Nothing is forwarded to other PCs while Mouse Without Borders is stopped.",
                    AdviceLevel.Neutral);

            case MwbPresence.RunningNotConnected:
                return new Advice("Running, no other PC connected",
                    "Simulated activity affects this PC only until another PC connects.",
                    AdviceLevel.Neutral);
        }

        string peers = status.ConfiguredPeers.Count > 0 ? string.Join(", ", status.ConfiguredPeers) : "another PC";
        string headline = $"Connected to {peers}";

        var lines = new List<string>
        {
            "Pointer on a remote PC: MousePulse's moves are forwarded to that PC like real mouse input.",
        };

        AdviceLevel level;
        if (status.BlockScreenSaverOnOtherMachines == true)
        {
            lines.Add(
                $"Pointer on this PC: \"Block screen saver on other machines\" is on, so about every {AwakeBeatSeconds} s Mouse Without Borders " +
                "tells the other PCs that someone is active here (MousePulse's moves count), and each of them makes its own small mouse move.");
            if (moveIntervalSeconds > AwakeBeatSeconds)
            {
                lines.Add($"Set the move interval to {AwakeBeatSeconds} s or less so that every one of those signals reports activity.");
                level = AdviceLevel.Warning;
            }
            else
            {
                level = AdviceLevel.Good;
            }
        }
        else
        {
            lines.Add(
                "Pointer on this PC: other PCs receive nothing. To keep them awake too, turn on \"Block screen saver on other machines\" " +
                "in PowerToys > Mouse Without Borders.");
            level = AdviceLevel.Warning;
        }

        lines.Add("Whether the other PC's idle timer resets can only be confirmed on that PC (Record idle log there).");
        return new Advice(headline, string.Join(Environment.NewLine, lines.Select(l => "• " + l)), level);
    }
}
