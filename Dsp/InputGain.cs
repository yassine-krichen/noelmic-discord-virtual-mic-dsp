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
    private readonly float _sampleRate;
    private readonly float _smoothingCoeff;
    private float _targetGainLinear;

    // Smoothed toward _targetGainLinear per-sample rather than applied
    // instantly — Calibrate and live preset/parameter swaps produce discrete
    // gain jumps that would otherwise click audibly.
    private float _smoothedGainLinear;

    // ~8ms one-pole time constant: fast enough that a gain change settles
    // well within a single UI-driven update, slow enough to never be an
    // audible step.
    private const float SmoothingMs = 8f;

    public InputGain(ISampleProvider source, float gainDb)
    {
        _source = source;
        _sampleRate = source.WaveFormat.SampleRate;
        _smoothingCoeff = MathF.Exp(-1f / (SmoothingMs / 1000f * _sampleRate));
        _targetGainLinear = MathF.Pow(10f, gainDb / 20f);
        _smoothedGainLinear = _targetGainLinear;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void UpdateGainDb(float gainDb)
    {
        _targetGainLinear = MathF.Pow(10f, gainDb / 20f);
    }

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);

        for (var i = 0; i < samplesRead; i++)
        {
            _smoothedGainLinear = _smoothingCoeff * _smoothedGainLinear + (1f - _smoothingCoeff) * _targetGainLinear;
            buffer[i] *= _smoothedGainLinear;
        }

        return samplesRead;
    }
}
