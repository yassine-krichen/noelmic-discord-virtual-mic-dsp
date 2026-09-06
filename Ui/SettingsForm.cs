using NAudio.CoreAudioApi;
using NoelMic.Config;
using NoelMic.Core;
using NoelMic.Input;

namespace NoelMic.Ui;

/// <summary>
/// Modal settings dialog: input device, toggle button binding, sound preset,
/// live level/dynamics metering, gain calibration, and start-with-Windows.
/// Shown via <see cref="Form.ShowDialog()"/>.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly record struct DeviceOption(string DisplayName, string Id);

    // Clamp bounds for a calibration result — a computed value outside this
    // range is either a measurement fluke or a hardware problem software
    // gain can't fix well (see the warning shown near the ceiling below).
    private const float MinCalibratedGainDb = 0f;
    private const float MaxCalibratedGainDb = 40f;

    // Below this many qualifying frames, the user likely never spoke during
    // the calibration window and the result shouldn't be trusted.
    private const int MinQualifyingFramesForTrust = 20;

    private readonly AppConfig _config;
    private readonly MouseHook _mouseHook;
    private readonly Func<MMDevice, bool> _onInputDeviceSelected;
    private readonly Func<MeteringSnapshot> _getMeteringSnapshot;
    private readonly Action _onParametersChanged;
    private readonly Action _startCalibration;
    private readonly Func<(bool IsRunning, bool IsDone, int QualifyingFrameCount, float ComputedGainDb)> _getCalibrationStatus;
    private readonly MMDeviceEnumerator _enumerator;

    // Captured when the dialog opens so FormClosing can revert a live preset
    // preview if the user cancels/escapes/X-closes instead of saving.
    private readonly string? _originalPreset;

    private readonly ComboBox _deviceComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _buttonComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _presetComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _startWithWindowsCheckBox = new() { Text = "Start with Windows", AutoSize = true };
    private readonly Label _levelLabel = new() { Text = "-- dBFS", AutoSize = true };
    private readonly Label _outputLevelLabel = new() { Text = "-- dBFS", AutoSize = true };
    private readonly Label _gateLabel = new() { Text = "--", AutoSize = true };
    private readonly Label _compressorLabel = new() { Text = "--", AutoSize = true };
    private readonly Label _deEsserLabel = new() { Text = "--", AutoSize = true };
    private readonly Button _calibrateButton = new() { Text = "Calibrate…", AutoSize = true };
    private readonly Label _calibrationStatusLabel = new() { Text = "", AutoSize = true, ForeColor = SystemColors.GrayText };

    // ~100ms is frequent enough to feel "live" without redrawing the form on
    // every audio buffer — the underlying meters themselves update far more
    // often than this timer polls them.
    private readonly System.Windows.Forms.Timer _levelTimer = new() { Interval = 100 };

    // Separate from _levelTimer so calibration progress polling isn't tied
    // to (or skipped by) whatever the metering timer's interval happens to be.
    private readonly System.Windows.Forms.Timer _calibrationTimer = new() { Interval = 150 };

    // CalibrationMeter.IsDone stays true until the next StartCalibration(),
    // so without this the apply/save block below would re-run on every
    // single timer tick for as long as the dialog stays open. Reset to
    // false when a new calibration run starts.
    private bool _calibrationResultConsumed;

    public SettingsForm(
        AppConfig config,
        MMDeviceEnumerator enumerator,
        MouseHook mouseHook,
        Func<MMDevice, bool> onInputDeviceSelected,
        Func<MeteringSnapshot> getMeteringSnapshot,
        Action onParametersChanged,
        Action startCalibration,
        Func<(bool IsRunning, bool IsDone, int QualifyingFrameCount, float ComputedGainDb)> getCalibrationStatus)
    {
        _config = config;
        _enumerator = enumerator;
        _mouseHook = mouseHook;
        _onInputDeviceSelected = onInputDeviceSelected;
        _getMeteringSnapshot = getMeteringSnapshot;
        _onParametersChanged = onParametersChanged;
        _startCalibration = startCalibration;
        _getCalibrationStatus = getCalibrationStatus;
        _originalPreset = config.Preset;

        Text = "NoelMic Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var layout = BuildLayout();
        Controls.Add(layout);

        PopulateDeviceComboBox();
        PopulateButtonComboBox();
        PopulatePresetComboBox();
        _startWithWindowsCheckBox.Checked = _config.StartWithWindows;

        _levelTimer.Tick += (_, _) => RefreshMetering();
        _levelTimer.Start();

        _calibrationTimer.Tick += (_, _) => RefreshCalibrationStatus();
        _calibrationTimer.Start();

        FormClosing += OnFormClosing;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _levelTimer.Stop();
            _levelTimer.Dispose();
            _calibrationTimer.Stop();
            _calibrationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private TableLayoutPanel BuildLayout()
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var row = 0;

        table.Controls.Add(new Label { Text = "Input device:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(_deviceComboBox, 1, row++);

        table.Controls.Add(new Label { Text = "Toggle button:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(_buttonComboBox, 1, row++);

        table.Controls.Add(new Label { Text = "Sound preset:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(_presetComboBox, 1, row++);

        table.Controls.Add(new Label { Text = "Input level:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(_levelLabel, 1, row++);

        table.Controls.Add(new Label { Text = "Output level:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(_outputLevelLabel, 1, row++);

        table.Controls.Add(new Label { Text = "Gate:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(_gateLabel, 1, row++);

        table.Controls.Add(new Label { Text = "Compressor:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(_compressorLabel, 1, row++);

        table.Controls.Add(new Label { Text = "De-esser:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(_deEsserLabel, 1, row++);

        table.Controls.Add(
            new Label
            {
                Text = "Aim for about -18 dBFS while speaking normally.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
            },
            1, row++);

        table.Controls.Add(_calibrateButton, 1, row++);
        table.Controls.Add(_calibrationStatusLabel, 1, row++);

        table.Controls.Add(_startWithWindowsCheckBox, 1, row++);

        _presetComboBox.SelectedIndexChanged += (_, _) => OnPresetChanged();
        _calibrateButton.Click += (_, _) => OnCalibrateClicked();

        var saveButton = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        saveButton.Click += (_, _) => ApplySettings();

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        table.Controls.Add(buttonPanel, 1, row);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        return table;
    }

    private void PopulateDeviceComboBox()
    {
        var options = new List<DeviceOption>();

        // Copy the fields we need and dispose the live MMDevice handles
        // immediately — the form's lifetime shouldn't hold devices open.
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            options.Add(new DeviceOption(device.FriendlyName, device.ID));
            device.Dispose();
        }

        _deviceComboBox.DisplayMember = nameof(DeviceOption.DisplayName);
        _deviceComboBox.DataSource = options;

        var selectedIndex = options.FindIndex(o => o.Id == _config.InputDeviceId);
        if (selectedIndex >= 0)
        {
            _deviceComboBox.SelectedIndex = selectedIndex;
        }
    }

    private void PopulateButtonComboBox()
    {
        _buttonComboBox.DataSource = Enum.GetValues<ToggleMouseButton>();

        if (Enum.TryParse<ToggleMouseButton>(_config.ToggleButton, ignoreCase: true, out var current))
        {
            _buttonComboBox.SelectedItem = current;
        }
    }

    private void PopulatePresetComboBox()
    {
        _presetComboBox.DataSource = PresetDefinitions.Names;
        _presetComboBox.SelectedItem = _config.Preset;
    }

    // Live preview: applied immediately on selection, not deferred to Save —
    // the running DspChain picks up the new preset's coefficients on its next
    // audio buffer via DspParameterStore, no engine restart involved. Cancel
    // reverts this via OnFormClosing below.
    private void OnPresetChanged()
    {
        _config.Preset = (string)_presetComboBox.SelectedItem!;
        _onParametersChanged();
    }

    // Covers Cancel, Escape, and the title-bar X uniformly — all three raise
    // FormClosing with DialogResult != OK, so a preset preview left dangling
    // by any of them gets reverted the same way.
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK && _config.Preset != _originalPreset)
        {
            _config.Preset = _originalPreset;
            _onParametersChanged();
        }
    }

    private void OnCalibrateClicked()
    {
        _calibrateButton.Enabled = false;
        _calibrationStatusLabel.Text = "Calibrating… speak normally for 5 seconds.";
        _calibrationResultConsumed = false;
        _calibrationTimer.Start();
        _startCalibration();
    }

    private void RefreshCalibrationStatus()
    {
        var status = _getCalibrationStatus();

        if (status.IsRunning)
        {
            _calibrationStatusLabel.Text = "Calibrating… speak normally for 5 seconds.";
            return;
        }

        if (!status.IsDone || _calibrationResultConsumed)
        {
            return;
        }

        // Nothing left to poll until the next Calibrate click restarts this.
        _calibrationTimer.Stop();
        _calibrationResultConsumed = true;

        _calibrateButton.Enabled = true;

        if (status.QualifyingFrameCount < MinQualifyingFramesForTrust)
        {
            _calibrationStatusLabel.Text = "No speech detected — try again and talk normally throughout.";
            return;
        }

        var clampedGainDb = Math.Clamp(status.ComputedGainDb, MinCalibratedGainDb, MaxCalibratedGainDb);

        if (status.ComputedGainDb > MaxCalibratedGainDb)
        {
            _calibrationStatusLabel.Text =
                $"Mic is very quiet — applied the max {MaxCalibratedGainDb:F0} dB gain. " +
                "For better results, raise the Windows mic level or enable mic boost.";
        }
        else
        {
            _calibrationStatusLabel.Text = $"Done — set input gain to {clampedGainDb:F1} dB.";
        }

        _config.InputGainDb = clampedGainDb;
        _config.Save();
        _onParametersChanged();
    }

    private void RefreshMetering()
    {
        var snapshot = _getMeteringSnapshot();

        _levelLabel.Text = $"{snapshot.InputLevelDb:F1} dBFS";
        _outputLevelLabel.Text = $"{snapshot.OutputLevelDb:F1} dBFS";
        _gateLabel.Text = snapshot.GateIsOpen
            ? "Open"
            : $"Closed ({snapshot.GateReductionDb:F1} dB)";

        // Both GateReductionDb and CompressorReductionDb are dB-relative-to-
        // unity (0 = no reduction, negative = attenuated) — negate for
        // display so "reduction" reads as a positive magnitude, matching
        // DeEsserReductionDb, which is already stored that way.
        _compressorLabel.Text = $"{-snapshot.CompressorReductionDb:F1} dB";
        _deEsserLabel.Text = $"{snapshot.DeEsserReductionDb:F1} dB";
    }

    private void ApplySettings()
    {
        // Reachable if Settings is opened while zero capture devices are
        // active (e.g. mid-disconnect) — button/startup changes should still
        // save rather than crashing on a null device selection.
        var selectedDeviceOption = _deviceComboBox.SelectedItem as DeviceOption?;
        var selectedButton = (ToggleMouseButton)_buttonComboBox.SelectedItem!;

        // Preset is already applied live by OnPresetChanged — nothing more to
        // do for it here beyond letting it be persisted below by the
        // trailing _config.Save().

        // Only persisted on success — otherwise the old device keeps running
        // but config.json would falsely claim the new one is active, and the
        // next launch would resolve to a device the switch never actually used.
        var deviceSwitched = true;
        var deviceExplicitlyChanged = selectedDeviceOption is { } selectedDevice && selectedDevice.Id != _config.InputDeviceId;
        if (deviceExplicitlyChanged)
        {
            // Not disposed here — AudioEngine takes ownership and disposes it
            // as part of its own device lifecycle (see AudioEngine.Stop()).
            var device = _enumerator.GetDevice(((DeviceOption)selectedDeviceOption!).Id);
            deviceSwitched = _onInputDeviceSelected(device);
        }

        _mouseHook.SetToggleButton(selectedButton);
        StartupRegistration.SetEnabled(_startWithWindowsCheckBox.Checked);

        if (selectedDeviceOption is { } appliedDevice && deviceSwitched)
        {
            _config.InputDeviceId = appliedDevice.Id;
        }

        _config.ToggleButton = selectedButton.ToString();
        _config.StartWithWindows = _startWithWindowsCheckBox.Checked;
        _config.Save();
    }
}
