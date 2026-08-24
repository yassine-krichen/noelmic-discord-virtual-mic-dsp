using NAudio.CoreAudioApi;
using NoelMic.Config;
using NoelMic.Core;
using NoelMic.Input;

namespace NoelMic.Ui;

/// <summary>
/// Owns the tray <see cref="NotifyIcon"/> and its right-click menu: toggle
/// mute, open settings, exit.
/// </summary>
public sealed class TrayIcon
{
    private readonly MuteController _muteController;
    private readonly MouseHook _mouseHook;
    private readonly AppConfig _config;
    private readonly MMDeviceEnumerator _enumerator;
    private readonly Func<MMDevice, bool> _onInputDeviceSelected;
    private readonly Action _onExitRequested;
    private readonly IconFactory _iconFactory;
    private readonly NotifyIcon _notifyIcon;

    public TrayIcon(
        MuteController muteController,
        MouseHook mouseHook,
        AppConfig config,
        MMDeviceEnumerator enumerator,
        Func<MMDevice, bool> onInputDeviceSelected,
        Action onExitRequested)
    {
        _muteController = muteController;
        _mouseHook = mouseHook;
        _config = config;
        _enumerator = enumerator;
        _onInputDeviceSelected = onInputDeviceSelected;
        _onExitRequested = onExitRequested;

        _iconFactory = new IconFactory();

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Toggle Mute", null, (_, _) => _muteController.Toggle());
        contextMenu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        contextMenu.Items.Add("Exit", null, (_, _) => Exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = _muteController.IsMuted ? _iconFactory.MicOffIcon : _iconFactory.MicOnIcon,
            Text = "NoelMic",
            ContextMenuStrip = contextMenu,
            Visible = true,
        };

        // Toggle() is only ever invoked from the hook callback or this menu
        // click, both on the STA/UI thread, so no Invoke/marshaling is needed
        // to update the icon here.
        _muteController.MuteStateChanged += (_, isMuted) =>
            _notifyIcon.Icon = isMuted ? _iconFactory.MicOffIcon : _iconFactory.MicOnIcon;
    }

    private void ShowSettings()
    {
        using var settingsForm = new SettingsForm(_config, _enumerator, _mouseHook, _onInputDeviceSelected);
        settingsForm.ShowDialog();
    }

    private void Exit()
    {
        // Must be set before Application.Exit() — otherwise the icon lingers
        // in the tray until moused-over after the message loop ends.
        _notifyIcon.Visible = false;
        _iconFactory.Dispose();
        _onExitRequested();
    }
}
