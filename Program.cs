using NAudio.CoreAudioApi;
using NoelMic.Core;
using NoelMic.Input;

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
        using var enumerator = new MMDeviceEnumerator();

        var captureDevice = PickCaptureDevice(enumerator);
        if (captureDevice is null)
        {
            return;
        }

        var renderDevice = FindVbCableInput(enumerator);
        if (renderDevice is null)
        {
            Console.WriteLine("VB-CABLE not found — install it from https://vb-audio.com/Cable/ and retry.");
            return;
        }

        var muteController = new MuteController();
        muteController.MuteStateChanged += (_, isMuted) => Console.WriteLine(isMuted ? "Muted" : "Unmuted");

        var mouseHook = new MouseHook();
        mouseHook.ToggleRequested += (_, _) => muteController.Toggle();

        var engine = new AudioEngine(captureDevice, renderDevice, muteController);
        engine.Start();

        Console.WriteLine($"Passthrough running: \"{captureDevice.FriendlyName}\" -> \"{renderDevice.FriendlyName}\"");
        Console.WriteLine("Press XButton1 (side mouse button) to toggle mute. Press Enter to exit.");

        // WH_MOUSE_LL requires a real Win32 message pump on the thread that
        // installed the hook, so Console.ReadLine() moves to a background
        // thread and the main STA thread runs MouseHook.Run() instead.
        var exitListener = new Thread(() =>
        {
            Console.ReadLine();
            mouseHook.Stop();
        })
        {
            IsBackground = true,
        };
        exitListener.Start();

        mouseHook.Run();

        engine.Stop();
    }

    private static MMDevice? PickCaptureDevice(MMDeviceEnumerator enumerator)
    {
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        if (devices.Count == 0)
        {
            Console.WriteLine("No active capture devices found.");
            return null;
        }

        Console.WriteLine("Select input device:");
        for (var i = 0; i < devices.Count; i++)
        {
            Console.WriteLine($"  [{i}] {devices[i].FriendlyName}");
        }

        Console.Write("Index: ");
        var input = Console.ReadLine();
        if (!int.TryParse(input, out var index) || index < 0 || index >= devices.Count)
        {
            Console.WriteLine("Invalid selection.");
            return null;
        }

        return devices[index];
    }

    private static MMDevice? FindVbCableInput(MMDeviceEnumerator enumerator)
    {
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        return devices.FirstOrDefault(
            d => d.FriendlyName.Contains(VbCableFriendlyNameSubstring, StringComparison.OrdinalIgnoreCase));
    }
}
