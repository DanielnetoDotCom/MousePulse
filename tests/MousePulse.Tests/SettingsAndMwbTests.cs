using System.Net;
using System.Text;
using MousePulse.Core;
using MousePulse.Settings;

namespace MousePulse.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MousePulseTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Round_trip_preserves_values()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(new AppSettings
        {
            InactivityThresholdSeconds = 120,
            MoveIntervalSeconds = 45,
            MoveDistancePixels = 0,
            StartWithWindows = true,
            StartSimulationOnLaunch = true,
            MinimizeToTray = false,
            CloseToTray = false,
            VaryMovement = false,
        });

        AppSettings loaded = store.Load();
        Assert.False(loaded.VaryMovement);
        Assert.Equal(120, loaded.InactivityThresholdSeconds);
        Assert.Equal(45, loaded.MoveIntervalSeconds);
        Assert.Equal(0, loaded.MoveDistancePixels);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.StartSimulationOnLaunch);
        Assert.False(loaded.MinimizeToTray);
        Assert.False(loaded.CloseToTray);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void Schedule_round_trips_with_readable_day_names()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(new AppSettings
        {
            ScheduleEnabled = true,
            ScheduleDays = [DayOfWeek.Monday, DayOfWeek.Friday],
            ScheduleStart = new TimeOnly(8, 0),
            ScheduleEnd = new TimeOnly(17, 0),
        });

        Assert.Contains("\"Monday\"", File.ReadAllText(store.FilePath));
        AppSettings loaded = store.Load();
        Assert.True(loaded.ScheduleEnabled);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], loaded.ScheduleDays);
        Assert.Equal(new TimeOnly(8, 0), loaded.ScheduleStart);
        Assert.Equal(new TimeOnly(17, 0), loaded.ScheduleEnd);
    }

    [Fact]
    public void Older_settings_files_get_the_default_schedule_switched_off()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "InactivityThresholdSeconds": 60 }""");

        AppSettings loaded = new SettingsStore(path).Load();
        Assert.False(loaded.ScheduleEnabled);
        Assert.Null(loaded.ToSchedule());
        Assert.Equal(Schedule.Weekdays, loaded.ScheduleDays);
    }

    [Fact]
    public void Missing_or_corrupt_file_yields_defaults()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        Assert.Equal(AppLimits.DefaultThresholdSeconds, store.Load().InactivityThresholdSeconds);
        Assert.True(store.Load().VaryMovement);

        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.FilePath, "{ this is not json");
        Assert.Equal(AppLimits.DefaultIntervalSeconds, store.Load().MoveIntervalSeconds);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "InactivityThresholdSeconds": 1, "MoveIntervalSeconds": 999999, "MoveDistancePixels": -4 }""");

        AppSettings loaded = new SettingsStore(path).Load();
        Assert.Equal(AppLimits.MinThresholdSeconds, loaded.InactivityThresholdSeconds);
        Assert.Equal(AppLimits.MaxIntervalSeconds, loaded.MoveIntervalSeconds);
        Assert.Equal(AppLimits.MinDistance, loaded.MoveDistancePixels);
    }
}

public class MwbTests
{
    // Shape of %LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json (values anonymised).
    private const string SampleSettings = """
        {
          "properties": {
            "SecurityKey": { "value": "xxxx" },
            "BlockScreenSaverOnOtherMachines": { "value": true },
            "MoveMouseRelatively": { "value": false },
            "MachineMatrixString": [ "PC-ONE", "", "LAPTOP-TWO", "" ],
            "TCPPort": { "value": 15200 }
          },
          "name": "MouseWithoutBorders",
          "version": "1.1"
        }
        """;

    [Fact]
    public void Parses_PowerToys_settings()
    {
        var settings = MwbDetector.ParseSettings(new MemoryStream(Encoding.UTF8.GetBytes(SampleSettings)));

        Assert.True(settings.BlockScreenSaver);
        Assert.False(settings.MoveRelatively);
        Assert.Equal(15200, settings.TcpPort);
        Assert.Contains("LAPTOP-TWO", settings.Peers);
        Assert.DoesNotContain("", settings.Peers);
    }

    [Theory]
    [InlineData("PC-Desktop-Office1", true)]   // full host name
    [InlineData("pc-desktop-office1", true)]   // case-insensitive
    [InlineData("PC-DESKTOP-OFFI", true)]      // NetBIOS name
    [InlineData("PC-Desktop-Office2", false)]  // shares the 15-char NetBIOS prefix, but is a different PC
    [InlineData("WNB4YT05L3BR", false)]
    public void Recognises_this_machine_despite_NetBIOS_truncation(string name, bool expected)
    {
        Assert.Equal(expected, MwbDetector.IsThisMachine(name, hostName: "PC-Desktop-Office1", netBiosName: "PC-DESKTOP-OFFI"));
    }

    [Fact]
    public void Missing_properties_fall_back_to_defaults()
    {
        var settings = MwbDetector.ParseSettings(new MemoryStream(Encoding.UTF8.GetBytes("""{ "properties": {} }""")));

        Assert.Null(settings.BlockScreenSaver);
        Assert.Equal(15100, settings.TcpPort);
        Assert.Empty(settings.Peers);
    }

    private static MwbStatus Connected(bool? block) => new(
        MwbPresence.Connected, "PowerToys Mouse Without Borders", ["LAPTOP-TWO"], [IPAddress.Parse("192.168.0.24")], block, false, 15100);

    [Fact]
    public void Advice_is_good_when_block_screen_saver_is_on_and_interval_fits()
    {
        Advice advice = MwbAdvice.For(Connected(true), moveIntervalSeconds: 30);
        Assert.Equal(AdviceLevel.Good, advice.Level);
        Assert.Contains("LAPTOP-TWO", advice.Headline);
    }

    [Fact]
    public void Advice_warns_when_interval_exceeds_awake_beat()
    {
        Advice advice = MwbAdvice.For(Connected(true), moveIntervalSeconds: 90);
        Assert.Equal(AdviceLevel.Warning, advice.Level);
    }

    [Fact]
    public void Advice_warns_when_block_screen_saver_is_off()
    {
        Advice advice = MwbAdvice.For(Connected(false), moveIntervalSeconds: 30);
        Assert.Equal(AdviceLevel.Warning, advice.Level);
        Assert.Contains("Block screen saver on other machines", advice.Detail);
    }

    [Fact]
    public void Advice_for_absent_MWB_is_neutral()
    {
        var status = new MwbStatus(MwbPresence.NotInstalled, "", [], [], null, null, 15100);
        Assert.Equal(AdviceLevel.Neutral, MwbAdvice.For(status, 30).Level);
    }
}
