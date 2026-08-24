using NAudio.Wave;

namespace NoelMic.Dsp;

/// <summary>
/// Hard-ceiling peak limiter: the final DSP stage before the mute gate,
/// prevents clipping into VB-CABLE.
/// </summary>
public sealed class Limiter : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _ceilingLinear;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;

    // Per-channel peak envelope and gain state, sized once at construction —
    // this is the only allocation for this stage, never per-Read.
    private readonly float[] _envelope;
    private readonly float[] _gain;

    public Limiter(ISampleProvider source, float ceilingDb = -1f, float attackMs = 2f, float releaseMs = 50f)
    {
        _source = source;
        _ceilingLinear = MathF.Pow(10f, ceilingDb / 20f);

        var sampleRate = source.WaveFormat.SampleRate;
        _attackCoeff = MathF.Exp(-1f / (attackMs / 1000f * sampleRate));
        _releaseCoeff = MathF.Exp(-1f / (releaseMs / 1000f * sampleRate));

        _envelope = new float[source.WaveFormat.Channels];
        _gain = new float[source.WaveFormat.Channels];
        Array.Fill(_gain, 1f);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);
        var channels = _envelope.Length;

        for (var i = 0; i < samplesRead; i++)
        {
            var ch = i % channels;
            var x = buffer[i];

            var peak = MathF.Abs(x);
            var envCoeff = peak > _envelope[ch] ? _attackCoeff : _releaseCoeff;
            _envelope[ch] = envCoeff * _envelope[ch] + (1f - envCoeff) * peak;

            var targetGain = _envelope[ch] > _ceilingLinear ? _ceilingLinear / _envelope[ch] : 1f;
            var gainCoeff = targetGain < _gain[ch] ? _attackCoeff : _releaseCoeff;
            _gain[ch] = gainCoeff * _gain[ch] + (1f - gainCoeff) * targetGain;

            // The smoothed envelope-based reduction alone cannot guarantee
            // zero overs on instantaneous transients without lookahead — this
            // hard clamp is what actually delivers the "prevents clipping"
            // guarantee for single-sample spikes the envelope hasn't caught up to.
            buffer[i] = Math.Clamp(x * _gain[ch], -_ceilingLinear, _ceilingLinear);
        }

        return samplesRead;
    }
}
