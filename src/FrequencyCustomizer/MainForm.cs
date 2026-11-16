using FrequencyCustomizer.Audio;
using FrequencyCustomizer.Tuning;
using NAudio.CoreAudioApi;

namespace FrequencyCustomizer;

public sealed class MainForm : Form
{
    // ---- theme ----
    private static readonly Color Bg = Color.FromArgb(24, 26, 32);
    private static readonly Color Panel = Color.FromArgb(34, 37, 46);
    private static readonly Color Border = Color.FromArgb(52, 56, 68);
    private static readonly Color Fg = Color.FromArgb(232, 234, 240);
    private static readonly Color Muted = Color.FromArgb(150, 156, 172);
    private static readonly Color Accent = Color.FromArgb(108, 140, 255);
    private static readonly Color Good = Color.FromArgb(80, 200, 140);
    private static readonly Color Bad = Color.FromArgb(240, 110, 110);

    private readonly Settings _settings = Settings.Load();
    private readonly AudioEngine _engine = new();
    private readonly Dictionary<TuningPreset, Button> _presetButtons = new();
    private readonly Dictionary<TuningPreset, ToolStripMenuItem> _trayPresetItems = new();

    private readonly Label _bigHz = new();
    private readonly Label _desc = new();
    private readonly NumericUpDown _hzBox = new();
    private readonly ComboBox _refCombo = new();
    private readonly TrackBar _cents = new();
    private readonly Label _centsLabel = new();
    private readonly Button _nearestBtn = new();
    private readonly ComboBox _inCombo = new();
    private readonly ComboBox _outCombo = new();
    private readonly CheckBox _autoSwitch = new();
    private readonly CheckBox _startOnLaunch = new();
    private readonly Button _startBtn = new();
    private readonly CheckBox _bypass = new();
    private readonly Label _status = new();
    private readonly Label _stats = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _trayStart = new("Start");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 300 };
    private Icon _iconIdle = null!, _iconActive = null!;

    private bool _updating;
    private bool _trayTipShown;
    private string? _sinkId; // Windows default while running (e.g. "CABLE Input")

    private sealed record DeviceItem(string Id, string Name) { public override string ToString() => Name; }

    public MainForm()
    {
        Text = "Frequency Customizer";
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9.75f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18);

        _iconIdle = AppIcon.Create(Accent, false);
        _iconActive = AppIcon.Create(Accent, true);
        Icon = _iconIdle;

        BuildUi();
        BuildTray();

        _engine.Faulted += msg => BeginInvoke(() =>
        {
            StopEngine();
            SetStatus("Stopped: " + msg, Bad);
        });
        _timer.Tick += (_, _) => UpdateStats();
        _timer.Start();

        Application.ApplicationExit += (_, _) => StopEngine();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => StopEngine();
    }

    // ===================================================================== UI

    private void BuildUi()
    {
        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
            Dock = DockStyle.Fill, BackColor = Bg,
        };
        Controls.Add(root);
        const int W = 540;

        root.Controls.Add(MakeLabel("Frequency Customizer", 16f, FontStyle.Bold, Fg));
        root.Controls.Add(MakeLabel("Retune everything your PC plays from standard 440 Hz tuning.", 9.75f, FontStyle.Regular, Muted, bottom: 14));

        // ---- readout ----
        var readout = MakeCard(W);
        _bigHz.Font = new Font("Segoe UI", 30f, FontStyle.Bold);
        _bigHz.ForeColor = Fg; _bigHz.AutoSize = true;
        _desc.ForeColor = Muted; _desc.AutoSize = true; _desc.Margin = new Padding(3, 0, 3, 4);
        var readFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        readFlow.Controls.Add(_bigHz);
        readFlow.Controls.Add(_desc);
        readout.Controls.Add(readFlow);
        root.Controls.Add(readout);

        // ---- presets ----
        var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 10) };
        AddPreset(presets, TuningPreset.Off440, "440 Hz", "Off");
        AddPreset(presets, TuningPreset.Hz432, "432 Hz", "A4");
        AddPreset(presets, TuningPreset.Hz528, "528 Hz", "C5");
        AddPreset(presets, TuningPreset.Hz639, "639 Hz", "D#5");
        AddPreset(presets, TuningPreset.Custom, "Custom", "any Hz");
        root.Controls.Add(presets);

        // ---- custom ----
        var custom = MakeCard(W);
        var cg = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        cg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        cg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        cg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _hzBox.Minimum = 20; _hzBox.Maximum = 20000; _hzBox.DecimalPlaces = 2; _hzBox.Increment = 1;
        _hzBox.Width = 120; StyleInput(_hzBox);
        _hzBox.ValueChanged += (_, _) => { if (!_updating) OnCustomHzChanged(); };

        _refCombo.DropDownStyle = ComboBoxStyle.DropDownList; _refCombo.Width = 260; StyleInput(_refCombo);
        for (int m = 36; m <= 96; m++)
            _refCombo.Items.Add(new DeviceItem(m.ToString(), $"{TuningMath.NoteName(m)}  ({TuningMath.NoteFrequency(m):0.00} Hz)"));
        _refCombo.SelectedIndexChanged += (_, _) => { if (!_updating) OnCustomRefChanged(); };

        StyleButton(_nearestBtn, "Nearest note", 110, 30);
        _nearestBtn.Click += (_, _) => { SelectRef(TuningMath.NearestNote((double)_hzBox.Value)); OnCustomRefChanged(); };

        _cents.Minimum = -100; _cents.Maximum = 100; _cents.TickFrequency = 25; _cents.Width = 260;
        _cents.BackColor = Panel; _cents.SmallChange = 1; _cents.LargeChange = 10;
        _cents.Scroll += (_, _) => OnCentsScrolled();
        _centsLabel.AutoSize = true; _centsLabel.ForeColor = Muted; _centsLabel.Anchor = AnchorStyles.Left;

        cg.Controls.Add(FieldLabel("Target frequency"), 0, 0);
        cg.Controls.Add(Row(_hzBox, MakeLabel("Hz", 9.75f, FontStyle.Regular, Muted)), 1, 0);
        cg.Controls.Add(FieldLabel("Reference note"), 0, 1);
        cg.Controls.Add(_refCombo, 1, 1);
        cg.Controls.Add(_nearestBtn, 2, 1);
        cg.Controls.Add(FieldLabel("Fine-tune"), 0, 2);
        cg.Controls.Add(_cents, 1, 2);
        cg.Controls.Add(_centsLabel, 2, 2);
        custom.Controls.Add(cg);
        root.Controls.Add(custom);

        // ---- devices ----
        var dev = MakeCard(W);
        dev.Margin = new Padding(0, 10, 0, 0);
        var dg = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        dg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        dg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _inCombo.DropDownStyle = ComboBoxStyle.DropDownList; _inCombo.Width = 370; StyleInput(_inCombo);
        _outCombo.DropDownStyle = ComboBoxStyle.DropDownList; _outCombo.Width = 370; StyleInput(_outCombo);
        _inCombo.SelectedIndexChanged += (_, _) => { if (!_updating) { _settings.InputDeviceId = (_inCombo.SelectedItem as DeviceItem)?.Id; _settings.Save(); } };
        _outCombo.SelectedIndexChanged += (_, _) => { if (!_updating) { _settings.OutputDeviceId = (_outCombo.SelectedItem as DeviceItem)?.Id; _settings.Save(); } };

        var refreshBtn = new Button();
        StyleButton(refreshBtn, "Refresh devices", 130, 30);
        refreshBtn.Click += (_, _) => RefreshDevices();

        _autoSwitch.Text = "Route Windows sound through the cable automatically while running";
        _autoSwitch.AutoSize = true; _autoSwitch.ForeColor = Fg;
        _autoSwitch.Checked = _settings.AutoSwitchDefault;
        _autoSwitch.CheckedChanged += (_, _) => { _settings.AutoSwitchDefault = _autoSwitch.Checked; _settings.Save(); };

        _startOnLaunch.Text = "Start retuning as soon as the app opens";
        _startOnLaunch.AutoSize = true; _startOnLaunch.ForeColor = Fg;
        _startOnLaunch.Checked = _settings.StartOnLaunch;
        _startOnLaunch.CheckedChanged += (_, _) => { _settings.StartOnLaunch = _startOnLaunch.Checked; _settings.Save(); };

        dg.Controls.Add(FieldLabel("Capture from"), 0, 0);
        dg.Controls.Add(_inCombo, 1, 0);
        dg.Controls.Add(FieldLabel("Play to"), 0, 1);
        dg.Controls.Add(_outCombo, 1, 1);
        dg.Controls.Add(refreshBtn, 1, 2);
        dg.Controls.Add(_autoSwitch, 0, 3); dg.SetColumnSpan(_autoSwitch, 2);
        dg.Controls.Add(_startOnLaunch, 0, 4); dg.SetColumnSpan(_startOnLaunch, 2);
        dev.Controls.Add(dg);
        root.Controls.Add(dev);

        // ---- start / bypass ----
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 6) };
        StyleButton(_startBtn, "▶  Start", 200, 46);
        _startBtn.BackColor = Accent; _startBtn.FlatAppearance.BorderColor = Accent;
        _startBtn.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _startBtn.Click += (_, _) => ToggleEngine();

        _bypass.Appearance = Appearance.Button;
        _bypass.Text = "Bypass (hear original)";
        _bypass.FlatStyle = FlatStyle.Flat; _bypass.FlatAppearance.BorderColor = Border;
        _bypass.FlatAppearance.CheckedBackColor = Color.FromArgb(120, 90, 40);
        _bypass.BackColor = Panel; _bypass.ForeColor = Fg;
        _bypass.Size = new Size(200, 46); _bypass.TextAlign = ContentAlignment.MiddleCenter;
        _bypass.Margin = new Padding(10, 0, 0, 0);
        _bypass.CheckedChanged += (_, _) => { _engine.Bypass = _bypass.Checked; UpdateStats(); };

        actions.Controls.Add(_startBtn);
        actions.Controls.Add(_bypass);
        root.Controls.Add(actions);

        _status.AutoSize = true; _status.MaximumSize = new Size(W, 0); _status.ForeColor = Muted;
        _stats.AutoSize = true; _stats.ForeColor = Muted; _stats.Font = new Font("Segoe UI", 8.5f);
        root.Controls.Add(_status);
        root.Controls.Add(_stats);
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show window", null, (_, _) => ShowFromTray());
        menu.Items.Add(new ToolStripSeparator());
        foreach (var (p, name) in new[] { (TuningPreset.Off440, "440 Hz (off)"), (TuningPreset.Hz432, "432 Hz"),
                                          (TuningPreset.Hz528, "528 Hz"), (TuningPreset.Hz639, "639 Hz"), (TuningPreset.Custom, "Custom") })
        {
            var item = new ToolStripMenuItem(name, null, (_, _) => SelectPreset(p));
            _trayPresetItems[p] = item;
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        _trayStart.Click += (_, _) => ToggleEngine();
        menu.Items.Add(_trayStart);
        menu.Items.Add("Exit", null, (_, _) => Close());

        _tray.Icon = _iconIdle;
        _tray.Text = "Frequency Customizer";
        _tray.ContextMenuStrip = menu;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // If the app was closed abnormally while the cable was the Windows default, put the speakers back.
        if (_settings.RestoreDeviceId != null)
        {
            DefaultDeviceSwitcher.SetDefault(_settings.RestoreDeviceId);
            _settings.RestoreDeviceId = null;
            _settings.Save();
        }

        _updating = true;
        _hzBox.Value = (decimal)Math.Clamp(_settings.CustomHz, 20, 20000);
        SelectRef(_settings.CustomReferenceMidi);
        _updating = false;

        RefreshDevices();
        SelectPreset(_settings.Preset);

        if (_settings.StartOnLaunch) ToggleEngine();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            if (!_trayTipShown)
            {
                _trayTipShown = true;
                _tray.ShowBalloonTip(2500, "Frequency Customizer",
                    _engine.IsRunning ? "Still retuning in the background. Right-click the tray icon for options." : "Minimized to the tray.",
                    ToolTipIcon.Info);
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopEngine();
        _settings.Save();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    // ================================================================ tuning

    private TuningPreset _preset;

    private void SelectPreset(TuningPreset p)
    {
        _preset = p;
        _settings.Preset = p;
        foreach (var (key, btn) in _presetButtons)
        {
            bool on = key == p;
            btn.BackColor = on ? Accent : Panel;
            btn.FlatAppearance.BorderColor = on ? Accent : Border;
        }
        foreach (var (key, item) in _trayPresetItems) item.Checked = key == p;

        bool custom = p == TuningPreset.Custom;
        _hzBox.Enabled = _refCombo.Enabled = _cents.Enabled = _nearestBtn.Enabled = custom;

        if (!custom)
        {
            // Show what the preset does in the (read-only) custom fields.
            var t = TuningTarget.ForPreset(p);
            _updating = true;
            _hzBox.Value = (decimal)t.TargetHz;
            SelectRef(t.ReferenceMidi);
            _updating = false;
        }
        else
        {
            _updating = true;
            _hzBox.Value = (decimal)Math.Clamp(_settings.CustomHz, 20, 20000);
            SelectRef(_settings.CustomReferenceMidi);
            _updating = false;
        }
        ApplyTarget();
    }

    private void OnCustomHzChanged()
    {
        _settings.CustomHz = (double)_hzBox.Value;
        ApplyTarget();
    }

    private void OnCustomRefChanged()
    {
        _settings.CustomReferenceMidi = SelectedRef();
        ApplyTarget();
    }

    private void OnCentsScrolled()
    {
        double hz = TuningMath.NoteFrequency(SelectedRef()) * TuningMath.RatioFromCents(_cents.Value);
        _updating = true;
        _hzBox.Value = (decimal)Math.Round(Math.Clamp(hz, 20, 20000), 2);
        _updating = false;
        OnCustomHzChanged();
    }

    private TuningTarget CurrentTarget() => _preset == TuningPreset.Custom
        ? new TuningTarget((double)_hzBox.Value, SelectedRef())
        : TuningTarget.ForPreset(_preset);

    private void ApplyTarget()
    {
        var t = CurrentTarget();
        _engine.Ratio = t.Ratio;

        _bigHz.Text = _preset == TuningPreset.Off440 ? "440 Hz  ·  Off" : $"{t.TargetHz:0.##} Hz";
        _desc.Text = t.Describe();

        _updating = true;
        _cents.Value = (int)Math.Round(Math.Clamp(t.Cents, _cents.Minimum, _cents.Maximum));
        _updating = false;
        _centsLabel.Text = Math.Abs(t.Cents) > 100
            ? $"{t.Cents:+0;-0} cents (beyond slider)"
            : $"{t.Cents:+0.0;-0.0} cents";

        _settings.Save();
        UpdateStats();
    }

    private int SelectedRef() =>
        _refCombo.SelectedItem is DeviceItem d ? int.Parse(d.Id) : TuningMath.A4Midi;

    private void SelectRef(int midi)
    {
        midi = Math.Clamp(midi, 36, 96);
        _refCombo.SelectedIndex = midi - 36;
    }

    // ================================================================ devices

    private static bool IsVirtual(string name) =>
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CABLE", StringComparison.OrdinalIgnoreCase);

    private void RefreshDevices()
    {
        _updating = true;
        try
        {
            using var en = new MMDeviceEnumerator();
            var caps = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                         .Select(d => new DeviceItem(d.ID, d.FriendlyName)).ToList();
            var rends = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                          .Select(d => new DeviceItem(d.ID, d.FriendlyName)).ToList();

            _inCombo.Items.Clear();
            caps.ForEach(c => _inCombo.Items.Add(c));
            _outCombo.Items.Clear();
            rends.ForEach(r => _outCombo.Items.Add(r));

            var input = caps.FirstOrDefault(c => c.Id == _settings.InputDeviceId)
                        ?? caps.FirstOrDefault(c => c.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
                        ?? caps.FirstOrDefault(c => IsVirtual(c.Name));
            string? defId = DefaultDeviceSwitcher.GetDefaultRenderId();
            var output = rends.FirstOrDefault(r => r.Id == _settings.OutputDeviceId && !IsVirtual(r.Name))
                         ?? rends.FirstOrDefault(r => r.Id == defId && !IsVirtual(r.Name))
                         ?? rends.FirstOrDefault(r => !IsVirtual(r.Name));

            if (input != null) _inCombo.SelectedItem = input;
            if (output != null) _outCombo.SelectedItem = output;

            if (!caps.Any(c => IsVirtual(c.Name)))
                SetStatus("VB-CABLE was not found. Install it from vb-audio.com/Cable (free), restart the PC, then click Refresh devices.", Bad);
            else
                SetStatus(_engine.IsRunning ? "Running." : "Ready. Click Start to retune all PC audio.", Muted);
        }
        catch (Exception ex)
        {
            SetStatus("Could not list audio devices: " + ex.Message, Bad);
        }
        finally { _updating = false; }
    }

    /// <summary>"CABLE Output (VB-Audio Virtual Cable)" → its playback side "CABLE Input (VB-Audio Virtual Cable)".</summary>
    private static string? FindSinkFor(MMDeviceEnumerator en, string captureName)
    {
        var rends = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        string wanted = captureName.Replace("Output", "Input", StringComparison.OrdinalIgnoreCase);
        return (rends.FirstOrDefault(r => r.FriendlyName.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                ?? rends.FirstOrDefault(r => r.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)))?.ID;
    }

    // ================================================================ engine

    private void ToggleEngine()
    {
        if (_engine.IsRunning) { StopEngine(); SetStatus("Stopped. Windows sound is back to normal.", Muted); return; }

        if (_inCombo.SelectedItem is not DeviceItem inItem || _outCombo.SelectedItem is not DeviceItem outItem)
        {
            SetStatus("Pick a capture device (CABLE Output) and your speakers first.", Bad);
            return;
        }

        try
        {
            using var en = new MMDeviceEnumerator();
            _sinkId = FindSinkFor(en, inItem.Name);
            if (_sinkId != null && _sinkId == outItem.Id)
            {
                SetStatus("“Play to” must be your real speakers/headphones, not the cable.", Bad);
                return;
            }

            var input = en.GetDevice(inItem.Id);
            var output = en.GetDevice(outItem.Id);
            _engine.Bypass = _bypass.Checked;
            _engine.Ratio = CurrentTarget().Ratio;
            _engine.Start(input, output);

            string msg = $"Running: sound from “{inItem.Name}” is retuned and played on “{outItem.Name}”.";
            if (_autoSwitch.Checked && _sinkId != null)
            {
                string? prev = DefaultDeviceSwitcher.GetDefaultRenderId();
                if (prev != _sinkId)
                {
                    _settings.RestoreDeviceId = prev ?? outItem.Id;
                    _settings.Save();
                    if (!DefaultDeviceSwitcher.SetDefault(_sinkId))
                    {
                        _settings.RestoreDeviceId = null; _settings.Save();
                        msg = "Running, but Windows' default output could not be changed automatically. " +
                              "Set it yourself: Settings › System › Sound › Output › “CABLE Input”.";
                    }
                }
            }
            else if (_sinkId == null || !_autoSwitch.Checked)
            {
                msg += " Make sure Windows' output device is set to the cable (“CABLE Input”).";
            }
            SetRunningUi(true);
            SetStatus(msg, Good);
        }
        catch (Exception ex)
        {
            StopEngine();
            SetStatus("Could not start: " + ex.Message, Bad);
        }
    }

    private void StopEngine()
    {
        _engine.Stop();
        if (_settings.RestoreDeviceId != null)
        {
            DefaultDeviceSwitcher.SetDefault(_settings.RestoreDeviceId);
            _settings.RestoreDeviceId = null;
            _settings.Save();
        }
        if (!IsDisposed) SetRunningUi(false);
    }

    private void SetRunningUi(bool running)
    {
        _startBtn.Text = running ? "■  Stop" : "▶  Start";
        _startBtn.BackColor = running ? Bad : Accent;
        _startBtn.FlatAppearance.BorderColor = _startBtn.BackColor;
        _trayStart.Text = running ? "Stop" : "Start";
        _inCombo.Enabled = _outCombo.Enabled = !running;
        Icon = running ? _iconActive : _iconIdle;
        _tray.Icon = Icon;
        UpdateStats();
    }

    private void UpdateStats()
    {
        var t = CurrentTarget();
        string tuning = _preset == TuningPreset.Off440 ? "440 Hz (off)" : $"{t.TargetHz:0.##} Hz";
        _tray.Text = Truncate($"Frequency Customizer: {(_engine.IsRunning ? (_bypass.Checked ? "bypassed" : tuning) : "stopped")}", 63);

        _stats.Text = _engine.IsRunning
            ? $"Added delay ≈ {_engine.LatencyMs:0} ms   ·   {_engine.FormatInfo}   ·   dropouts: {_engine.Underruns}" +
              (_bypass.Checked ? "   ·   BYPASSED" : "")
            : "";
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    // ================================================================ helpers

    private void AddPreset(FlowLayoutPanel host, TuningPreset p, string title, string sub)
    {
        var b = new Button();
        StyleButton(b, $"{title}\n{sub}", 100, 58);
        b.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        b.Margin = new Padding(0, 0, 10, 0);
        b.Click += (_, _) => SelectPreset(p);
        _presetButtons[p] = b;
        host.Controls.Add(b);
    }

    private static void StyleButton(Button b, string text, int w, int h)
    {
        b.Text = text;
        b.Size = new Size(w, h);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Border;
        b.BackColor = Panel;
        b.ForeColor = Fg;
        b.Cursor = Cursors.Hand;
        b.UseVisualStyleBackColor = false;
    }

    private static void StyleInput(Control c)
    {
        c.BackColor = Color.FromArgb(44, 48, 60);
        c.ForeColor = Fg;
        if (c is ComboBox cb) cb.FlatStyle = FlatStyle.Flat;
        if (c is NumericUpDown n) n.BorderStyle = BorderStyle.FixedSingle;
        c.Margin = new Padding(3, 5, 3, 5);
    }

    private static Panel MakeCard(int width) => new()
    {
        BackColor = Panel, Width = width, MinimumSize = new Size(width, 0), AutoSize = true, Padding = new Padding(14, 10, 14, 10),
        Margin = new Padding(0),
    };

    private static Label MakeLabel(string text, float size, FontStyle style, Color color, int bottom = 0) => new()
    {
        Text = text, AutoSize = true, ForeColor = color,
        Font = new Font("Segoe UI", size, style), Margin = new Padding(0, 0, 0, bottom),
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 8),
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        foreach (var c in controls) { if (c is Label l) l.Anchor = AnchorStyles.Left; f.Controls.Add(c); }
        return f;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
