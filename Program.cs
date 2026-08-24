using NAudio.CoreAudioApi;
using NoelMic.Config;
using NoelMic.Core;
using NoelMic.Input;
using NoelMic.Ui;

namespace NoelMic;

internal static class Program
{
    private const string VbCableFriendlyNameSubstring = "CABLE Input";

    // NAudio's WASAPI/Core Audio wrappers are COM-based; running on a non-STA
    // thread causes intermittent device activation failures that only show up
    // at runtime, not at compile time.
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

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
            MessageBox.Show(
                "VB-CABLE not found — install it from https://vb-audio.com/Cable/ and retry.",
                "NoelMic",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
        engine.Start();

        bool OnInputDeviceSelected(MMDevice newCaptureDevice)
        {
            // Resolved before touching the current engine: if VB-CABLE isn't
            // found, the old engine must be left running untouched rather than
            // stopped-and-stranded, which would otherwise get Stop()'d a second
            // time (and throw) during shutdown.
            var newRenderDevice = FindVbCableInput(enumerator);
            if (newRenderDevice is null)
            {
                MessageBox.Show(
                    "VB-CABLE not found — install it from https://vb-audio.com/Cable/ and retry.",
                    "NoelMic",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            // AudioEngine uses explicit Stop() (not IDisposable), and its
            // devices cannot be reused after Stop, so switching input requires
            // a fresh AudioEngine over a freshly resolved render device.
            engine.Stop();

            engine = new AudioEngine(newCaptureDevice, newRenderDevice, muteController, config);
            engine.Start();
            return true;
        }

        _ = new TrayIcon(
            muteController,
            mouseHook,
            config,
            enumerator,
            OnInputDeviceSelected,
            Application.Exit);

        // WH_MOUSE_LL callbacks deliver synchronously on the thread that
        // called SetWindowsHookEx, during that thread's message pump
        // (GetMessage/DispatchMessage). Application.Run() is such a pump, so
        // the hook must be installed before it starts.
        mouseHook.Install();

        Application.Run();

        mouseHook.Uninstall();
        engine.Stop();
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
