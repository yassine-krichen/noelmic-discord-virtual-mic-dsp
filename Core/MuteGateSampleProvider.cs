using NAudio.Wave;

namespace NoelMic.Core;

/// <summary>
/// Final stage of the DSP chain: passes audio through unchanged, or writes
/// silence when muted.
/// </summary>
public sealed class MuteGateSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly MuteController _muteController;

    public MuteGateSampleProvider(ISampleProvider source, MuteController muteController)
    {
        _source = source;
        _muteController = muteController;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        // Must always pull from the source, even while muted — the upstream
        // BufferedWaveProvider discards on overflow, so starving it here would
        // let captured audio pile up and burst out stale/glitchy on unmute.
        var samplesRead = _source.Read(buffer);

        if (_muteController.IsMuted)
        {
            buffer[..samplesRead].Clear();
        }

        return samplesRead;
    }
}
