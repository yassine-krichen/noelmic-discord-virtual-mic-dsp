namespace NoelMic.Config;

/// <summary>
/// Cross-thread publication point for a fully-resolved <see cref="DspParameters"/>
/// snapshot: the UI thread calls <see cref="Update"/> after a settings change,
/// the audio thread reads <see cref="Current"/> once per buffer. A single
/// volatile reference swap avoids the torn-state risk of ~30 individually
/// volatile fields, and avoids locks, which are forbidden in the audio path.
/// </summary>
public sealed class DspParameterStore
{
    private volatile DspParameters _current;

    public DspParameterStore(DspParameters initial)
    {
        _current = initial;
    }

    public DspParameters Current => _current;

    public void Update(DspParameters next)
    {
        _current = next;
    }
}
