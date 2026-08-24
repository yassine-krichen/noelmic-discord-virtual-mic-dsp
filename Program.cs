using System.Threading;
using NAudio.CoreAudioApi;
using NoelMic.Config;
using NoelMic.Core;
using NoelMic.Input;
using NoelMic.Ui;

namespace NoelMic;

internal static class Program
{
    private const string VbCableFriendlyNameSubstring = "CABLE Input";

    private const string VbCableNotFoundMessage =
        "VB-CABLE is required — it provides the virtual microphone endpoint " +
        "that Discord, games, and OBS pick up as your mic.\n\n" +
        "Install it from https://vb-audio.com/Cable/, then restart NoelMic.";

    // NAudio's WASAPI/Core Audio wrappers are COM-based; running on a non-STA
    // thread causes intermittent device activation failures that only show up
    // at runtime, not at compile time.
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // NotifyIcon isn't a Control and has no .Invoke() — this is the only
        // reliable way to marshal background-thread callbacks (WASAPI
        // Stopped events, Core Audio device notifications) onto the UI
        // thread, so it must be set up before any of that wiring below.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var uiContext = SynchronizationContext.Current!;

        var config = AppConfig.Load();

        // Reconciles the registry with config.json as the source of truth —
        // covers a hand-edited config or a Run entry removed via Task Manager.
        StartupRegistration.SetEnabled(config.StartWithWindows);

        var enumerator = new MMDeviceEnumerator();

        var captureDevice = ResolveCaptureDevice(enumerator, config);
        if (captureDevice is null)
        {
            MessageBox.Show("No active capture devices found.", "NoelMic", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var renderDevice = FindVbCableInput(enumerator);
        if (renderDevice is null)
        {
            MessageBox.Show(VbCableNotFoundMessage, "NoelMic", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var muteController = new MuteController();

        if (!Enum.TryParse<ToggleMouseButton>(config.ToggleButton, ignoreCase: true, out var toggleButton))
        {
            toggleButton = ToggleMouseButton.XButton1;
        }

        var mouseHook = new MouseHook(toggleButton);
        mouseHook.ToggleRequested += (_, _) => muteController.Toggle();

        var engine = new AudioEngine(captureDevice, renderDevice, muteController, config);
        var currentCaptureDeviceId = captureDevice.ID;
        var engineDown = false;

        TrayIcon? trayIcon = null;

        // Shared by both a manual Settings device pick and auto-recovery on
        // replug — swaps the running engine over to newCaptureDevice.
        bool SwitchCaptureDevice(MMDevice newCaptureDevice)
        {
            // Resolved before touching the current engine: if VB-CABLE isn't
            // found, the old engine must be left running untouched rather than
            // stopped-and-stranded, which would otherwise get Stop()'d a second
            // time (and throw) during shutdown.
            var newRenderDevice = FindVbCableInput(enumerator);
            if (newRenderDevice is null)
            {
                MessageBox.Show(VbCableNotFoundMessage, "NoelMic", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // AudioEngine uses explicit Stop() (not IDisposable), and its
            // devices cannot be reused after Stop, so switching input requires
            // a fresh AudioEngine over a freshly resolved render device.
            engine.Stop();

            engine = new AudioEngine(newCaptureDevice, newRenderDevice, muteController, config);
            engine.Failed += (_, ex) => uiContext.Post(_ => HandleEngineFailed(ex), null);
            engine.Start();
            currentCaptureDeviceId = newCaptureDevice.ID;

            // Reset here (not at each call site) so both the manual Settings
            // device pick and auto-recovery clear the stuck-down tray state
            // uniformly on any successful switch.
            engineDown = false;
            trayIcon?.ShowDeviceRecovered();
            return true;
        }

        void HandleEngineFailed(Exception? ex)
        {
            // Defensive: this runs off a marshaled background-thread callback
            // and must never crash the message loop.
            try
            {
                engineDown = true;
                trayIcon?.ShowDeviceLost();
            }
            catch
            {
                // Swallow — see method comment.
            }
        }

        engine.Failed += (_, ex) => uiContext.Post(_ => HandleEngineFailed(ex), null);
        engine.Start();

        trayIcon = new TrayIcon(
            muteController,
            mouseHook,
            config,
            enumerator,
            SwitchCaptureDevice,
            Application.Exit);

        var deviceWatcher = new CaptureDeviceWatcher(enumerator);
        deviceWatcher.CaptureDeviceAvailable += (_, deviceId) =>
        {
            // Only auto-recover onto the exact device we were using — a
            // different device ID (e.g. a different USB port) stays a
            // manual Settings pick, consistent with the existing
            // no-guessing behavior in device resolution.
            if (!engineDown || deviceId != currentCaptureDeviceId)
            {
                return;
            }

            var recoveredDevice = enumerator.GetDevice(deviceId);
            SwitchCaptureDevice(recoveredDevice);
        };

        // WH_MOUSE_LL callbacks deliver synchronously on the thread that
        // called SetWindowsHookEx, during that thread's message pump
        // (GetMessage/DispatchMessage). Application.Run() is such a pump, so
        // the hook must be installed before it starts.
        mouseHook.Install();

        Application.Run();

        mouseHook.Uninstall();
        engine.Stop();

        // Must run before enumerator.Dispose() — MMDeviceNotificationClient's
        // Dispose() unregisters its callback against the enumerator, which
        // requires the enumerator to still be alive.
        deviceWatcher.Dispose();
        enumerator.Dispose();
    }

    private static MMDevice? ResolveCaptureDevice(MMDeviceEnumerator enumerator, AppConfig config)
    {
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        if (devices.Count == 0)
        {
            return null;
        }

        var configured = devices.FirstOrDefault(d => d.ID == config.InputDeviceId);
        if (configured is not null)
        {
            return configured;
        }

        // First run, or the previously configured device was unplugged/renamed —
        // fall back to the system default and persist it as the new choice.
        var fallback = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        config.InputDeviceId = fallback.ID;
        config.Save();
        return fallback;
    }

    private static MMDevice? FindVbCableInput(MMDeviceEnumerator enumerator)
    {
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        return devices.FirstOrDefault(
            d => d.FriendlyName.Contains(VbCableFriendlyNameSubstring, StringComparison.OrdinalIgnoreCase));
    }
}
