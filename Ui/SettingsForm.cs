using NAudio.CoreAudioApi;
using NoelMic.Config;
using NoelMic.Input;

namespace NoelMic.Ui;

/// <summary>
/// Modal settings dialog: input device, toggle button binding, and
/// start-with-Windows. Shown via <see cref="Form.ShowDialog()"/>.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly record struct DeviceOption(string DisplayName, string Id);

    private readonly AppConfig _config;
    private readonly MouseHook _mouseHook;
    private readonly Func<MMDevice, bool> _onInputDeviceSelected;
    private readonly Func<float> _getInputLevelDb;
    private readonly MMDeviceEnumerator _enumerator;

    private readonly ComboBox _deviceComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _buttonComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _presetComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _startWithWindowsCheckBox = new() { Text = "Start with Windows", AutoSize = true };
    private readonly Label _levelLabel = new() { Text = "-- dBFS", AutoSize = true };

    // ~100ms is frequent enough to feel "live" without redrawing the form on
    // every audio buffer — the underlying LevelMeter itself updates far more
    // often than this timer polls it.
    private readonly System.Windows.Forms.Timer _levelTimer = new() { Interval = 100 };

    public SettingsForm(
        AppConfig config,
        MMDeviceEnumerator enumerator,
        MouseHook mouseHook,
        Func<MMDevice, bool> onInputDeviceSelected,
        Func<float> getInputLevelDb)
    {
        _config = config;
        _enumerator = enumerator;
        _mouseHook = mouseHook;
        _onInputDeviceSelected = onInputDeviceSelected;
        _getInputLevelDb = getInputLevelDb;

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

        _levelTimer.Tick += (_, _) => _levelLabel.Text = $"{_getInputLevelDb():F1} dBFS";
        _levelTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _levelTimer.Stop();
            _levelTimer.Dispose();
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

        table.Controls.Add(new Label { Text = "Input device:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_deviceComboBox, 1, 0);

        table.Controls.Add(new Label { Text = "Toggle button:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_buttonComboBox, 1, 1);

        table.Controls.Add(new Label { Text = "Sound preset:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        table.Controls.Add(_presetComboBox, 1, 2);

        table.Controls.Add(new Label { Text = "Input level:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        table.Controls.Add(_levelLabel, 1, 3);

        table.Controls.Add(
            new Label
            {
                Text = "Aim for about -18 dBFS while speaking normally.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
            },
            1, 4);

        table.Controls.Add(_startWithWindowsCheckBox, 1, 5);

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
        table.Controls.Add(buttonPanel, 1, 6);

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

    private void ApplySettings()
    {
        // Reachable if Settings is opened while zero capture devices are
        // active (e.g. mid-disconnect) — button/startup changes should still
        // save rather than crashing on a null device selection.
        var selectedDeviceOption = _deviceComboBox.SelectedItem as DeviceOption?;
        var selectedButton = (ToggleMouseButton)_buttonComboBox.SelectedItem!;
        var selectedPreset = (string)_presetComboBox.SelectedItem!;

        // Captured before _config.Preset is mutated below, and applied to
        // _config before any device-restart callback runs — the restarted
        // engine builds its DspChain from _config.Preset, so the new preset
        // must already be in place by the time that happens.
        var presetChanged = selectedPreset != _config.Preset;
        _config.Preset = selectedPreset;

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
        else if (presetChanged && _config.InputDeviceId != "")
        {
            // A preset-only change doesn't already trigger a device restart,
            // but the running AudioEngine still needs to rebuild its DspChain
            // against the new preset — forcing the same proven
            // Stop/recreate/Start path used for a real device switch is the
            // simplest way to get there without a second, separate restart
            // mechanism.
            var currentDevice = _enumerator.GetDevice(_config.InputDeviceId);
            _onInputDeviceSelected(currentDevice);
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
