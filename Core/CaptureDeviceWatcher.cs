using NAudio.CoreAudioApi;

namespace NoelMic.Core;

/// <summary>
/// Watches for a capture device becoming available again (e.g. a replug).
/// Deliberately does not track removal — <see cref="AudioEngine.Failed"/>,
/// driven by the actual WASAPI stream erroring, is the single source of
/// truth for "the pipeline is down." This class only answers "is a device
/// available now," nothing else.
/// </summary>
public sealed class CaptureDeviceWatcher : IDisposable
{
    private readonly MMDeviceNotificationClient _notificationClient;

    public event EventHandler<string>? CaptureDeviceAvailable;

    public CaptureDeviceWatcher(MMDeviceEnumerator enumerator)
    {
        _notificationClient = enumerator.CreateNotificationClient(useSynchronizationContext: true);
        _notificationClient.DeviceStateChanged += OnDeviceStateChanged;
    }

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        // Matches MouseHook.HookCallback's policy: a callback exception must
        // never escape into COM/message-loop dispatch.
        try
        {
            if (e.NewState == DeviceState.Active)
            {
                CaptureDeviceAvailable?.Invoke(this, e.DeviceId);
            }
        }
        catch
        {
            // Swallow — see method comment.
        }
    }

    public void Dispose()
    {
        _notificationClient.DeviceStateChanged -= OnDeviceStateChanged;
        _notificationClient.Dispose();
    }
}
