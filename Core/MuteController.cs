namespace NoelMic.Core;

/// <summary>
/// Holds the mute flag flipped by the global mouse hook and read by the
/// DSP mute gate. Never touches the physical Core Audio endpoint.
/// </summary>
public sealed class MuteController
{
    // Written on the hook thread, read on the WASAPI render thread; a single
    // bool flag needs nothing stronger than volatile to be safe across threads.
    private volatile bool _isMuted;

    public bool IsMuted => _isMuted;

    public event EventHandler<bool>? MuteStateChanged;

    public void Toggle()
    {
        _isMuted = !_isMuted;
        MuteStateChanged?.Invoke(this, _isMuted);
    }
}
