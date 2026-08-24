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
    private readonly MMDeviceEnumerator _enumerator;

    private readonly ComboBox _deviceComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _buttonComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _startWithWindowsCheckBox = new() { Text = "Start with Windows", AutoSize = true };

    public SettingsForm(
        AppConfig config,
        MMDeviceEnumerator enumerator,
        MouseHook mouseHook,
        Func<MMDevice, bool> onInputDeviceSelected)
    {
        _config = config;
        _enumerator = enumerator;
        _mouseHook = mouseHook;
        _onInputDeviceSelected = onInputDeviceSelected;

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
        _startWithWindowsCheckBox.Checked = _config.StartWithWindows;
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

        table.Controls.Add(_startWithWindowsCheckBox, 1, 2);

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
        table.Controls.Add(buttonPanel, 1, 3);

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

    private void ApplySettings()
    {
        var selectedDevice = (DeviceOption)_deviceComboBox.SelectedItem!;
        var selectedButton = (ToggleMouseButton)_buttonComboBox.SelectedItem!;

        // Only persisted on success — otherwise the old device keeps running
        // but config.json would falsely claim the new one is active, and the
        // next launch would resolve to a device the switch never actually used.
        var deviceSwitched = true;
        if (selectedDevice.Id != _config.InputDeviceId)
        {
            // Not disposed here — AudioEngine takes ownership and disposes it
            // as part of its own device lifecycle (see AudioEngine.Stop()).
            var device = _enumerator.GetDevice(selectedDevice.Id);
            deviceSwitched = _onInputDeviceSelected(device);
        }

        _mouseHook.SetToggleButton(selectedButton);
        StartupRegistration.SetEnabled(_startWithWindowsCheckBox.Checked);

        if (deviceSwitched)
        {
            _config.InputDeviceId = selectedDevice.Id;
        }

        _config.ToggleButton = selectedButton.ToString();
        _config.StartWithWindows = _startWithWindowsCheckBox.Checked;
        _config.Save();
    }
}
