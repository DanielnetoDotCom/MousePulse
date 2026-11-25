using System.Diagnostics;
using Microsoft.Win32;
using MousePulse.Core;
using MousePulse.Settings;

namespace MousePulse.UI;

internal sealed class MainForm : Form
{
    private const int ColumnWidth = 370;
    private const int PolicyRefreshSeconds = 10;
    private const int MwbRefreshSeconds = 30;
    private static readonly TimeSpan ProbeDuration = TimeSpan.FromMinutes(10);

    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly ActivityEngine _engine;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Icon _appIcon;
    private readonly NotifyIcon _tray;
    private readonly bool _startInTray;
    private readonly RegisteredWaitHandle _activateRegistration;

    // Status card
    private readonly Label _statusDot = new();
    private readonly Label _statusTitle = new();
    private readonly Label _statusDetail = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _testButton = new();

    // Activity card
    private readonly Label _userIdleValue = new();
    private readonly Label _systemIdleValue = new();
    private readonly Label _lastMoveValue = new();
    private readonly Label _nextMoveValue = new();
    private readonly Label _movesValue = new();

    // Settings card
    private readonly NumericUpDown _thresholdInput = new();
    private readonly NumericUpDown _intervalInput = new();
    private readonly NumericUpDown _distanceInput = new();
    private readonly CheckBox _varyCheck = new();
    private readonly Label _settingsHint = new();

    // Application card
    private readonly CheckBox _startupCheck = new();
    private readonly CheckBox _autoStartCheck = new();
    private readonly CheckBox _minimizeCheck = new();
    private readonly CheckBox _closeCheck = new();

    // Schedule card
    private static readonly DayOfWeek[] WeekOrder =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private readonly CheckBox _scheduleCheck = new();
    private readonly CheckBox[] _dayChips = new CheckBox[7];
    private readonly DateTimePicker _fromPicker = new();
    private readonly DateTimePicker _toPicker = new();
    private readonly Label _scheduleStatus = new();
    private readonly ScheduleController _scheduleController = new();

    // Mouse Without Borders + policy
    private readonly Label _mwbStatusValue = new();
    private readonly Label _mwbAddressValue = new();
    private readonly Label _mwbBlockValue = new();
    private readonly Label _mwbDetail = new();
    private readonly LinkLabel _probeLink = new();
    private readonly Label _policyValue = new();

    private readonly ToolStripMenuItem _trayStart = new("Start");
    private readonly ToolStripMenuItem _trayStop = new("Stop");

    private PolicyStatus _policy = new([]);
    private MwbStatus? _mwb;
    private TimeSpan? _screenSaverTimeout;
    private int _refreshCountdown;
    private int _mwbCountdown;
    private bool _exiting;
    private bool _trayHintShown;
    private bool _firstShowHandled;
    private bool _applyingSettings;
    private IdleProbe? _probe;
    private string? _lastTrayText;

    public MainForm(bool startInTray, EventWaitHandle activateSignal)
    {
        _startInTray = startInTray;
        _settings = _store.Load();
        _engine = new ActivityEngine(new Win32SystemInput());
        _appIcon = LoadAppIcon();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "MousePulse";
        Icon = _appIcon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Controls.Add(BuildLayout());
        ResumeLayout(false);
        PerformLayout();

        _tray = BuildTray();

        ApplySettingsToControls();
        ApplySettingsToEngine();
        SyncStartupRegistration();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _activateRegistration = ThreadPool.RegisterWaitForSingleObject(
            activateSignal, (_, _) => RunOnUi(ShowFromTray), null, Timeout.Infinite, executeOnlyOnce: false);

        RefreshEnvironment(force: true);
        if (_settings.StartSimulationOnLaunch && !_policy.BlocksSimulation)
        {
            _engine.Start();
        }

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        OnTick();
    }

    // ------------------------------------------------------------------ layout

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
            Location = new Point(0, 0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ColumnWidth));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ColumnWidth));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.SetColumnSpan(root.GetControlFromPosition(0, 0)!, 2);

        root.Controls.Add(BuildStatusCard(), 0, 1);
        root.Controls.Add(BuildActivityCard(), 1, 1);
        root.Controls.Add(BuildSettingsCard(), 0, 2);
        root.Controls.Add(BuildApplicationCard(), 1, 2);

        root.Controls.Add(BuildScheduleCard(), 0, 3);
        root.Controls.Add(BuildPolicyCard(), 1, 3);

        Control mwb = BuildMwbCard();
        root.Controls.Add(mwb, 0, 4);
        root.SetColumnSpan(mwb, 2);
        return root;
    }

    private Control BuildScheduleCard()
    {
        var body = Stack();
        ConfigureCheck(_scheduleCheck, "Start and stop automatically on these days");
        body.Controls.Add(_scheduleCheck);

        var days = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 6) };
        for (int i = 0; i < WeekOrder.Length; i++)
        {
            var chip = new CheckBox
            {
                Text = WeekOrder[i].ToString()[..3],
                Tag = WeekOrder[i],
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(42, 26),
                Margin = new Padding(0, 0, 4, 0),
                Cursor = Cursors.Hand,
            };
            chip.FlatAppearance.BorderSize = 0;
            chip.CheckedChanged += (_, _) =>
            {
                PaintChip(chip);
                OnScheduleEdited();
            };
            chip.EnabledChanged += (_, _) => PaintChip(chip);
            _dayChips[i] = chip;
            days.Controls.Add(chip);
        }

        body.Controls.Add(days);

        var times = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        ConfigureTimePicker(_fromPicker);
        ConfigureTimePicker(_toPicker);
        times.Controls.Add(new Label { Text = "From", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 6, 0) });
        times.Controls.Add(_fromPicker);
        times.Controls.Add(new Label { Text = "to", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(6, 6, 6, 0) });
        times.Controls.Add(_toPicker);
        body.Controls.Add(times);

        ConfigureWrapped(_scheduleStatus, ColumnWidth - 40);
        _scheduleStatus.Margin = new Padding(0, 8, 0, 0);
        body.Controls.Add(_scheduleStatus);

        _scheduleCheck.CheckedChanged += (_, _) => OnScheduleEdited();
        _fromPicker.ValueChanged += (_, _) => OnScheduleEdited();
        _toPicker.ValueChanged += (_, _) => OnScheduleEdited();
        return Card("Schedule", body);
    }

    private static void ConfigureTimePicker(DateTimePicker picker)
    {
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "HH:mm";
        picker.ShowUpDown = true;
        picker.Width = 72;
        picker.Margin = new Padding(0, 2, 0, 2);
    }

    private static void PaintChip(CheckBox chip)
    {
        Color back = (chip.Checked, chip.Enabled) switch
        {
            (true, true) => Theme.Accent,
            (true, false) => Theme.AccentMuted,
            (false, true) => Theme.Secondary,
            _ => Theme.Disabled,
        };
        chip.BackColor = back;
        chip.FlatAppearance.CheckedBackColor = back;
        chip.FlatAppearance.MouseOverBackColor = back;
        chip.ForeColor = chip.Checked ? Theme.AccentText : chip.Enabled ? Theme.Text : Theme.Muted;
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 4, 6, 6),
        };
        var picture = new PictureBox
        {
            Image = new Icon(_appIcon, 32, 32).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(32, 32),
            Margin = new Padding(0, 2, 10, 0),
        };
        header.Controls.Add(picture, 0, 0);
        header.SetRowSpan(picture, 2);
        header.Controls.Add(new Label { Text = "MousePulse", AutoSize = true, Font = Theme.Semibold(14f), Margin = new Padding(0) }, 1, 0);
        header.Controls.Add(new Label
        {
            Text = "Keeps this PC awake with tiny mouse moves \u2013 only while you are away.",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(1, 0, 0, 0),
        }, 1, 1);
        return header;
    }

    private Control BuildStatusCard()
    {
        var body = Stack();

        var titleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Dock = DockStyle.Fill };
        _statusDot.Text = "●";
        _statusDot.AutoSize = true;
        _statusDot.Font = Theme.Regular(13f);
        _statusDot.Margin = new Padding(0, 0, 4, 0);
        _statusTitle.AutoSize = true;
        _statusTitle.Font = Theme.Semibold(12f);
        _statusTitle.Margin = new Padding(0, 3, 0, 0);
        titleRow.Controls.Add(_statusDot);
        titleRow.Controls.Add(_statusTitle);
        body.Controls.Add(titleRow);

        // Fixed size (two lines): this text changes every second while simulating.
        _statusDetail.AutoSize = false;
        _statusDetail.Size = new Size(ColumnWidth - 40, 46);
        _statusDetail.ForeColor = Theme.Muted;
        _statusDetail.Margin = new Padding(2, 2, 0, 6);
        body.Controls.Add(_statusDetail);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Dock = DockStyle.Fill };
        StyleButton(_startButton, "Start", primary: true);
        StyleButton(_stopButton, "Stop", primary: false);
        StyleButton(_testButton, "Test move", primary: false);
        _startButton.Click += (_, _) => StartSimulation();
        _stopButton.Click += (_, _) => StopSimulation();
        _testButton.Click += (_, _) => TestMove();
        var tips = new ToolTip();
        tips.SetToolTip(_testButton, "Send one simulated move right now (to check that it works).");
        buttons.Controls.AddRange([_startButton, _stopButton, _testButton]);
        body.Controls.Add(buttons);

        return Card("Status", body);
    }

    private Control BuildActivityCard()
    {
        var grid = Grid();
        const int live = 190;
        AddRow(grid, "Your inactivity", _userIdleValue, live);
        AddRow(grid, "Windows idle timer", _systemIdleValue, live);
        AddRow(grid, "Last simulated move", _lastMoveValue, live);
        AddRow(grid, "Next move", _nextMoveValue, live);
        AddRow(grid, "Simulated moves", _movesValue, live);
        return Card("Activity", grid);
    }

    private Control BuildSettingsCard()
    {
        var body = Stack();
        var grid = Grid();
        AddRow(grid, "Start after inactivity of", NumberWithUnit(_thresholdInput, AppLimits.MinThresholdSeconds, AppLimits.MaxThresholdSeconds, "seconds"));
        AddRow(grid, "Move every", NumberWithUnit(_intervalInput, AppLimits.MinIntervalSeconds, AppLimits.MaxIntervalSeconds, "seconds"));
        AddRow(grid, "Move distance", NumberWithUnit(_distanceInput, AppLimits.MinDistance, AppLimits.MaxDistance, "px (0 = invisible)"));
        body.Controls.Add(grid);

        ConfigureCheck(_varyCheck, "Vary timing, distance and path (more natural)");
        _varyCheck.Margin = new Padding(0, 6, 0, 0);
        body.Controls.Add(_varyCheck);

        ConfigureWrapped(_settingsHint, ColumnWidth - 40);
        _settingsHint.Margin = new Padding(0, 6, 0, 0);
        body.Controls.Add(_settingsHint);

        _thresholdInput.ValueChanged += (_, _) => OnSettingsEdited();
        _intervalInput.ValueChanged += (_, _) => OnSettingsEdited();
        _distanceInput.ValueChanged += (_, _) => OnSettingsEdited();
        _varyCheck.CheckedChanged += (_, _) => OnSettingsEdited();
        return Card("Simulation", body);
    }

    private Control BuildApplicationCard()
    {
        var body = Stack();
        ConfigureCheck(_startupCheck, "Start with Windows (in the tray)");
        ConfigureCheck(_autoStartCheck, "Start simulating when MousePulse launches");
        ConfigureCheck(_minimizeCheck, "Minimize to the system tray");
        ConfigureCheck(_closeCheck, "Keep running in the tray when closed");
        body.Controls.AddRange([_startupCheck, _autoStartCheck, _minimizeCheck, _closeCheck]);

        _startupCheck.CheckedChanged += (_, _) => OnStartupToggled();
        _autoStartCheck.CheckedChanged += (_, _) => OnOptionsEdited();
        _minimizeCheck.CheckedChanged += (_, _) => OnOptionsEdited();
        _closeCheck.CheckedChanged += (_, _) => OnOptionsEdited();

        var location = new Label
        {
            Text = "Preferences: " + _store.FilePath.Replace(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%", StringComparison.OrdinalIgnoreCase),
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = Theme.Regular(8f),
            Margin = new Padding(1, 8, 0, 0),
        };
        body.Controls.Add(location);
        return Card("Application", body);
    }

    private Control BuildMwbCard()
    {
        var body = Stack();
        var grid = Grid();
        _mwbStatusValue.Font = Theme.Semibold();
        AddRow(grid, "Status", _mwbStatusValue);
        AddRow(grid, "Linked addresses", _mwbAddressValue);
        AddRow(grid, "Block screen saver on other machines", _mwbBlockValue);
        body.Controls.Add(grid);

        ConfigureWrapped(_mwbDetail, (ColumnWidth * 2) - 40);
        _mwbDetail.Margin = new Padding(0, 6, 0, 0);
        body.Controls.Add(_mwbDetail);

        _probeLink.AutoSize = true;
        _probeLink.Margin = new Padding(0, 8, 0, 0);
        _probeLink.LinkColor = Theme.Accent;
        _probeLink.ActiveLinkColor = Theme.Accent;
        _probeLink.LinkClicked += (_, _) => ToggleProbe();
        body.Controls.Add(_probeLink);
        UpdateProbeLink(null);

        return Card("Mouse Without Borders", body);
    }

    private Control BuildPolicyCard()
    {
        ConfigureWrapped(_policyValue, ColumnWidth - 40);
        _policyValue.Margin = new Padding(0, 6, 0, 0);
        return Card("Security policy", _policyValue);
    }

    private NotifyIcon BuildTray()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open MousePulse", null, (_, _) => ShowFromTray()) { Font = Theme.Semibold() };
        _trayStart.Click += (_, _) => StartSimulation();
        _trayStop.Click += (_, _) => StopSimulation();
        menu.Items.AddRange([open, new ToolStripSeparator(), _trayStart, _trayStop, new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication())]);

        var tray = new NotifyIcon
        {
            Icon = new Icon(_appIcon, SystemInformation.SmallIconSize),
            Text = "MousePulse",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowFromTray();
            }
        };
        return tray;
    }

    // ------------------------------------------------------------------ layout helpers

    private static TableLayoutPanel Card(string title, Control content)
    {
        var card = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            BackColor = Theme.Card,
            Padding = new Padding(14, 10, 14, 12),
            Margin = new Padding(6),
        };
        card.Controls.Add(new Label
        {
            Text = title.ToUpperInvariant(),
            AutoSize = true,
            Font = Theme.Semibold(8.25f),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 0, 0, 4),
        });
        content.Dock = DockStyle.Fill;
        card.Controls.Add(content);
        card.Paint += (s, e) =>
        {
            var c = (Control)s!;
            using var pen = new Pen(Theme.CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
        };
        return card;
    }

    private static TableLayoutPanel Stack() => new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0),
    };

    private static TableLayoutPanel Grid()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    /// <param name="liveWidth">
    /// For values that change every second: a fixed size, so a text change only repaints the label instead of
    /// re-laying out the whole form (this kept a visible window at ~2% CPU before).
    /// </param>
    private static void AddRow(TableLayoutPanel grid, string caption, Control value, int? liveWidth = null)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            ForeColor = Theme.Muted,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 16, 4),
        };
        if (value is Label l)
        {
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 4, 0, 4);
            l.Text = "-";
            if (liveWidth is int width)
            {
                l.AutoSize = false;
                l.AutoEllipsis = true;
                l.Size = new Size(width, 17);
            }
            else
            {
                l.AutoSize = true;
            }
        }

        int row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    private static Control NumberWithUnit(NumericUpDown input, int min, int max, string unit)
    {
        input.Minimum = min;
        input.Maximum = max;
        input.Width = 76;
        input.TextAlign = HorizontalAlignment.Right;
        input.Margin = new Padding(0, 2, 6, 2);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Left };
        row.Controls.Add(input);
        row.Controls.Add(new Label { Text = unit, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 0, 0) });
        return row;
    }

    private static void ConfigureWrapped(Label label, int width)
    {
        label.AutoSize = true;
        label.MaximumSize = new Size(width, 0);
        label.ForeColor = Theme.Muted;
    }

    private static void ConfigureCheck(CheckBox check, string text)
    {
        check.Text = text;
        check.AutoSize = true;
        check.Margin = new Padding(0, 3, 0, 3);
    }

    private static void StyleButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Size = new Size(96, 32);
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = primary ? Theme.Semibold() : Theme.Regular();
        button.Cursor = Cursors.Hand;
        button.Tag = primary;
        button.EnabledChanged += (_, _) => PaintButton(button);
        PaintButton(button);
    }

    private static void PaintButton(Button button)
    {
        bool primary = button.Tag is true;
        if (!button.Enabled)
        {
            button.BackColor = Theme.Disabled;
            button.ForeColor = Theme.Muted;
        }
        else
        {
            button.BackColor = primary ? Theme.Accent : Theme.Secondary;
            button.ForeColor = primary ? Theme.AccentText : Theme.Text;
        }
    }

    private static Icon LoadAppIcon()
    {
        using Stream? stream = typeof(MainForm).Assembly.GetManifestResourceStream("MousePulse.ico");
        return stream is not null ? new Icon(stream) : (Icon)SystemIcons.Application.Clone();
    }

    // ------------------------------------------------------------------ settings

    private void ApplySettingsToControls()
    {
        _applyingSettings = true;
        try
        {
            _thresholdInput.Value = _settings.InactivityThresholdSeconds;
            _intervalInput.Value = _settings.MoveIntervalSeconds;
            _distanceInput.Value = _settings.MoveDistancePixels;
            _varyCheck.Checked = _settings.VaryMovement;
            _startupCheck.Checked = _settings.StartWithWindows;
            _autoStartCheck.Checked = _settings.StartSimulationOnLaunch;
            _minimizeCheck.Checked = _settings.MinimizeToTray;
            _closeCheck.Checked = _settings.CloseToTray;

            _scheduleCheck.Checked = _settings.ScheduleEnabled;
            foreach (CheckBox chip in _dayChips)
            {
                chip.Checked = _settings.ScheduleDays.Contains((DayOfWeek)chip.Tag!);
                PaintChip(chip);
            }

            _fromPicker.Value = DateTime.Today + _settings.ScheduleStart.ToTimeSpan();
            _toPicker.Value = DateTime.Today + _settings.ScheduleEnd.ToTimeSpan();
            UpdateScheduleControlsEnabled();
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void ApplySettingsToEngine()
    {
        _engine.InactivityThreshold = TimeSpan.FromSeconds(_settings.InactivityThresholdSeconds);
        _engine.MoveInterval = TimeSpan.FromSeconds(_settings.MoveIntervalSeconds);
        _engine.MoveDistance = _settings.MoveDistancePixels;
        _engine.VaryMovement = _settings.VaryMovement;
    }

    private void OnSettingsEdited()
    {
        if (_applyingSettings)
        {
            return;
        }

        _settings.InactivityThresholdSeconds = (int)_thresholdInput.Value;
        _settings.MoveIntervalSeconds = (int)_intervalInput.Value;
        _settings.MoveDistancePixels = (int)_distanceInput.Value;
        _settings.VaryMovement = _varyCheck.Checked;
        ApplySettingsToEngine();
        _store.Save(_settings);
        UpdateEnvironmentUi();
        UpdateLive(_engine.Tick());
    }

    private void OnScheduleEdited()
    {
        if (_applyingSettings)
        {
            return;
        }

        _settings.ScheduleEnabled = _scheduleCheck.Checked;
        _settings.ScheduleDays = _dayChips.Where(c => c.Checked).Select(c => (DayOfWeek)c.Tag!).ToList();
        _settings.ScheduleStart = TimeOnly.FromDateTime(_fromPicker.Value);
        _settings.ScheduleEnd = TimeOnly.FromDateTime(_toPicker.Value);
        _store.Save(_settings);
        UpdateScheduleControlsEnabled();

        // Re-evaluate as if freshly launched: inside the new window -> start; outside -> leave as is.
        _scheduleController.Reset();
        OnTick();
    }

    private void UpdateScheduleControlsEnabled()
    {
        bool on = _scheduleCheck.Checked;
        foreach (CheckBox chip in _dayChips)
        {
            chip.Enabled = on;
        }

        _fromPicker.Enabled = on;
        _toPicker.Enabled = on;
    }

    /// <summary>Starts/stops the engine when the schedule window opens/closes.</summary>
    private void ApplySchedule()
    {
        switch (_scheduleController.Evaluate(_settings.ToSchedule(), DateTime.Now))
        {
            case ScheduleAction.Start when !_engine.IsRunning && !_policy.BlocksSimulation:
                _engine.Start();
                break;
            case ScheduleAction.Stop when _engine.IsRunning:
                _engine.Stop();
                break;
        }
    }

    private void UpdateScheduleStatus()
    {
        Schedule? schedule = _settings.ToSchedule();
        string text;
        if (schedule is null)
        {
            text = "Off. MousePulse starts and stops only when you press Start or Stop.";
        }
        else if (schedule.Days.Count == 0)
        {
            text = "Select at least one day.";
        }
        else
        {
            DateTime now = DateTime.Now;
            DateTime? next = schedule.NextChange(now);
            text = schedule.IsActive(now)
                ? "Inside the schedule now" + (next is { } end ? $" – stops {Format.When(end, now)}." : ".")
                : "Outside the schedule" + (next is { } start ? $" – starts {Format.When(start, now)}." : ".");
            text += " Start/Stop by hand overrides it until the next scheduled change.";
            if (!_settings.StartWithWindows)
            {
                text += " Turn on \"Start with Windows\" so the schedule also works after a restart.";
            }
        }

        SetText(_scheduleStatus, text);
        _scheduleStatus.ForeColor = schedule is not null && !_settings.StartWithWindows ? Theme.Warning : Theme.Muted;
    }

    /// <summary>Next time the schedule will start simulation, or null without a schedule.</summary>
    private DateTime? NextScheduledStart(DateTime now)
    {
        Schedule? schedule = _settings.ToSchedule();
        DateTime? next = schedule?.NextChange(now);
        if (schedule is null || next is null)
        {
            return null;
        }

        return schedule.IsActive(now) ? schedule.NextChange(next.Value) : next;
    }

    private void OnOptionsEdited()
    {
        if (_applyingSettings)
        {
            return;
        }

        _settings.StartSimulationOnLaunch = _autoStartCheck.Checked;
        _settings.MinimizeToTray = _minimizeCheck.Checked;
        _settings.CloseToTray = _closeCheck.Checked;
        _store.Save(_settings);
    }

    private void OnStartupToggled()
    {
        if (_applyingSettings)
        {
            return;
        }

        if (!StartupRegistration.Apply(_startupCheck.Checked))
        {
            MessageBox.Show(this, "Windows did not allow MousePulse to change its startup entry.", "MousePulse",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _applyingSettings = true;
            _startupCheck.Checked = StartupRegistration.IsEnabled();
            _applyingSettings = false;
        }

        _settings.StartWithWindows = _startupCheck.Checked;
        _store.Save(_settings);
    }

    /// <summary>Keeps the Run entry pointing at the current exe and reflects entries removed externally.</summary>
    private void SyncStartupRegistration()
    {
        if (_settings.StartWithWindows)
        {
            StartupRegistration.Apply(true);
        }
        else if (StartupRegistration.IsEnabled())
        {
            _settings.StartWithWindows = true;
            StartupRegistration.Apply(true);
            _applyingSettings = true;
            _startupCheck.Checked = true;
            _applyingSettings = false;
        }
    }

    // ------------------------------------------------------------------ actions

    private void StartSimulation()
    {
        RefreshEnvironment();
        if (_policy.BlocksSimulation)
        {
            ShowFromTray();
            return;
        }

        _engine.Start();
        OnTick();
    }

    private void StopSimulation()
    {
        _engine.Stop();
        OnTick();
    }

    private void TestMove()
    {
        _engine.NudgeNow();
        OnTick();
    }

    private void ToggleProbe()
    {
        if (_probe is not null)
        {
            IdleProbe finished = _probe;
            _probe = null;
            finished.Dispose();
            UpdateProbeLink(finished);
            return;
        }

        try
        {
            _probe = IdleProbe.Start(IdleProbe.DefaultPath(), ProbeDuration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not create the idle log: " + ex.Message, "MousePulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        IdleProbe started = _probe;
        started.Completed += (_, _) => RunOnUi(() =>
        {
            if (ReferenceEquals(_probe, started))
            {
                _probe = null;
                UpdateProbeLink(started);
            }
        });
        UpdateProbeLink(null);
    }

    private void UpdateProbeLink(IdleProbe? finished)
    {
        if (_probe is not null)
        {
            _probeLink.Text = $"Recording idle log for {ProbeDuration.TotalMinutes:0} min to {Path.GetFileName(_probe.FilePath)} on the Desktop \u2013 click to stop";
        }
        else if (finished is not null)
        {
            _probeLink.Text = $"Idle log saved ({finished.Samples} samples, {finished.Resets} idle-timer resets): {finished.FilePath} \u2013 click to record again";
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{finished.FilePath}\"") { UseShellExecute = false });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Opening Explorer is a convenience only.
            }
        }
        else
        {
            _probeLink.Text = "Record idle log (10 min) \u2013 run this on the OTHER PC to verify that its idle timer resets";
        }
    }

    // ------------------------------------------------------------------ periodic update

    private void OnTick()
    {
        if (--_refreshCountdown <= 0)
        {
            RefreshEnvironment();
        }

        ApplySchedule();

        EngineSnapshot snapshot = _engine.Tick();
        UpdateTray(snapshot);
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            UpdateLive(snapshot);
        }
    }

    private void RefreshEnvironment(bool force = false)
    {
        _refreshCountdown = PolicyRefreshSeconds;
        _policy = PolicyInspector.Inspect(); // a few registry reads: cheap, and it gates the engine
        _engine.BlockedByPolicy = _policy.BlocksSimulation;

        // MWB / screen saver details are display-only: refresh less often, and never while hidden in the tray.
        bool displayed = Visible || !IsHandleCreated;
        if (displayed && (force || --_mwbCountdown <= 0))
        {
            _mwbCountdown = MwbRefreshSeconds / PolicyRefreshSeconds;
            _mwb = MwbDetector.Detect();
            _screenSaverTimeout = ScreenSaverInfo.GetTimeout();
        }

        if (displayed)
        {
            UpdateEnvironmentUi();
        }
    }

    private void UpdateLive(EngineSnapshot s)
    {
        (string title, string detail, Color color) = s.State switch
        {
            EngineState.Stopped => ("Stopped", "Press Start to keep this PC awake while you are away.", Theme.Neutral),
            EngineState.WaitingForIdle => ("Watching \u2013 you are active",
                $"Moves begin after {Format.Seconds(_settings.InactivityThresholdSeconds)} without input" +
                (s.NextNudgeIn is { } n ? $" (in {Format.Duration(n)})." : "."), Theme.Info),
            EngineState.Simulating => ("Simulating activity",
                $"You have been away {Format.Duration(s.UserIdle)}. Any real mouse or keyboard input stops the moves immediately.", Theme.Good),
            EngineState.PausedSessionLocked => ("Paused \u2013 session locked",
                "Windows is locked. Nothing is simulated until you unlock it.", Theme.Warning),
            EngineState.BlockedByPolicy => ("Blocked by security policy",
                "An enforced idle-lock policy is active, so MousePulse will not simulate input.", Theme.Warning),
            _ => ("", "", Theme.Neutral),
        };

        if (s.LastNudgeRegistered == false && s.State is EngineState.Simulating or EngineState.WaitingForIdle)
        {
            detail += " Windows did not register the last move (an elevated window is in front, or another app captured it).";
        }

        if (s.State == EngineState.Stopped && NextScheduledStart(DateTime.Now) is { } nextStart)
        {
            detail = $"The schedule starts it {Format.When(nextStart, DateTime.Now)}. Press Start to run it now.";
        }

        SetText(_statusTitle, title);
        SetText(_statusDetail, detail);
        UpdateScheduleStatus();
        _statusDot.ForeColor = color;

        SetText(_userIdleValue, Format.Duration(s.UserIdle));
        SetText(_systemIdleValue, Format.Duration(s.SystemIdle));
        SetText(_lastMoveValue, s.LastNudgeAt is { } at
            ? $"{Format.Clock(at)}  ({Format.Duration(DateTime.Now - at)} ago)"
            : "None yet");
        SetText(_nextMoveValue, s.State switch
        {
            EngineState.Simulating when s.NextNudgeIn is { } n => $"in {Format.Duration(n)}",
            EngineState.WaitingForIdle when s.NextNudgeIn is { } n => $"in {Format.Duration(n)} if you stay idle",
            EngineState.Stopped => "Not running",
            _ => "Paused",
        });
        SetText(_movesValue, s.NudgeCount == 0
            ? "0"
            : s.NudgeCount + s.LastNudgeRegistered switch
            {
                true => "  · last one registered",
                false => "  · last one NOT registered",
                null => string.Empty,
            });

        bool canSimulate = !_policy.BlocksSimulation && !_engine.SessionLocked;
        _startButton.Enabled = !_engine.IsRunning && !_policy.BlocksSimulation;
        _stopButton.Enabled = _engine.IsRunning;
        _testButton.Enabled = canSimulate;
    }

    private void UpdateEnvironmentUi()
    {
        // Settings hint
        var hints = new List<string>();
        int threshold = _settings.InactivityThresholdSeconds;
        int interval = _settings.MoveIntervalSeconds;
        if (_screenSaverTimeout is { } ss)
        {
            if (threshold >= ss.TotalSeconds)
            {
                hints.Add($"Your screen saver starts after {Format.Duration(ss)} \u2013 lower the inactivity threshold below that, or the screen saver will start first.");
            }
            else if (interval >= ss.TotalSeconds)
            {
                hints.Add($"The move interval should be shorter than your screen saver timeout ({Format.Duration(ss)}).");
            }
        }

        int distancePx = _settings.MoveDistancePixels;
        if (distancePx == 0)
        {
            hints.Add("A 0 px move is an input event without visible pointer motion.");
        }
        else if (_settings.VaryMovement)
        {
            int minInterval = (int)Math.Ceiling(interval * ActivityEngine.MinVariation);
            int minDistance = distancePx == 1 ? 1 : (int)Math.Ceiling(distancePx * ActivityEngine.MinVariation);
            string distance = minDistance == distancePx ? $"{distancePx} px" : $"{minDistance}\u2013{distancePx} px";
            hints.Add($"Moves come every {minInterval}\u2013{interval} s and travel {distance} in a random direction toward the screen centre, in small steps, then return.");
        }
        else
        {
            hints.Add($"Each move shifts the pointer {distancePx} px toward the screen centre and straight back.");
        }
        SetText(_settingsHint, string.Join(" ", hints));

        // Mouse Without Borders
        if (_mwb is { } mwb)
        {
            Advice advice = MwbAdvice.For(mwb, interval);
            SetText(_mwbStatusValue, advice.Headline);
            _mwbStatusValue.ForeColor = advice.Level switch
            {
                AdviceLevel.Good => Theme.Good,
                AdviceLevel.Warning => Theme.Warning,
                _ => Theme.Text,
            };
            SetText(_mwbAddressValue, mwb.ConnectedAddresses.Count > 0 ? string.Join(", ", mwb.ConnectedAddresses) : "-");
            SetText(_mwbBlockValue, mwb.BlockScreenSaverOnOtherMachines switch
            {
                true => "On",
                false => "Off",
                null => "Unknown",
            });
            SetText(_mwbDetail, advice.Detail);
        }

        // Policy
        if (_policy.BlocksSimulation)
        {
            SetText(_policyValue, "Simulation is disabled because this PC enforces an idle lock: " +
                string.Join("; ", _policy.EnforcedIdleLocks) + ". MousePulse does not work around security policies.");
            _policyValue.ForeColor = Theme.Warning;
        }
        else
        {
            SetText(_policyValue, "No enforced idle-lock policy detected. Locked sessions are never touched: simulation pauses while Windows is locked.");
            _policyValue.ForeColor = Theme.Muted;
        }
    }

    private void UpdateTray(EngineSnapshot s)
    {
        string text = s.State switch
        {
            EngineState.Stopped => "MousePulse \u2013 stopped",
            EngineState.WaitingForIdle => "MousePulse \u2013 watching (you are active)",
            EngineState.Simulating => "MousePulse \u2013 simulating activity",
            EngineState.PausedSessionLocked => "MousePulse \u2013 paused (locked)",
            EngineState.BlockedByPolicy => "MousePulse \u2013 blocked by policy",
            _ => "MousePulse",
        };

        if (text != _lastTrayText)
        {
            _lastTrayText = text;
            _tray.Text = text;
            _trayStart.Enabled = !_engine.IsRunning && !_policy.BlocksSimulation;
            _trayStop.Enabled = _engine.IsRunning;
        }
    }

    private static void SetText(Control control, string text)
    {
        // Avoid relayout/flicker when nothing changed.
        if (control.Text != text)
        {
            control.Text = text;
        }
    }

    // ------------------------------------------------------------------ window / tray behaviour

    protected override void SetVisibleCore(bool value)
    {
        if (!_firstShowHandled)
        {
            _firstShowHandled = true;
            if (_startInTray)
            {
                if (!IsHandleCreated)
                {
                    CreateHandle();
                }

                value = false;
            }
        }

        base.SetVisibleCore(value);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            RefreshEnvironment(force: true);
            UpdateLive(_engine.Tick());
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
        {
            HideToTray();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _store.Save(_settings);
        base.OnFormClosing(e);
    }

    private void HideToTray()
    {
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(3000, "MousePulse is still running",
                "Click the tray icon to open it, or right-click for Start, Stop and Exit.", ToolTipIcon.Info);
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is being destroyed.
        }
    }

    // ------------------------------------------------------------------ system events

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        RunOnUi(() =>
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.ConsoleDisconnect:
                case SessionSwitchReason.RemoteDisconnect:
                    _engine.SessionLocked = true;
                    break;
                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.RemoteConnect:
                    _engine.SessionLocked = false;
                    _engine.Resync();
                    break;
            }

            OnTick();
        });
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            RunOnUi(() =>
            {
                _engine.Resync();
                OnTick();
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _activateRegistration.Unregister(null);
            _timer.Dispose();
            _probe?.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
