using NAudio.Wave;

namespace NoelMic.Dsp;

/// <summary>
/// Flat linear gain stage applied before anything else in the chain — a
/// typical quiet 3.5mm mic signal sits far enough below every downstream
/// stage's threshold (compressor, limiter) that those stages never engage
/// without this.
/// </summary>
public sealed class InputGain : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _gainLinear;

    public InputGain(ISampleProvider source, float gainDb)
    {
        _source = source;
        _gainLinear = MathF.Pow(10f, gainDb / 20f);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);

        for (var i = 0; i < samplesRead; i++)
        {
            buffer[i] *= _gainLinear;
        }

        return samplesRead;
    }
}
