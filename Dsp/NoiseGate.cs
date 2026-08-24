using NAudio.Wave;

namespace NoelMic.Dsp;

/// <summary>
/// Threshold-based noise gate: attenuates the signal toward silence when its
/// envelope drops below the threshold, kills silence-floor hiss.
/// </summary>
public sealed class NoiseGate : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly double _thresholdDb;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;

    // Per-channel envelope and gain state, sized once at construction — this
    // is the only allocation for this stage, never per-Read.
    private readonly float[] _envelope;
    private readonly float[] _gain;

    public NoiseGate(ISampleProvider source, double thresholdDb, float attackMs = 5f, float releaseMs = 150f)
    {
        _source = source;
        _thresholdDb = thresholdDb;

        var sampleRate = source.WaveFormat.SampleRate;
        _attackCoeff = MathF.Exp(-1f / (attackMs / 1000f * sampleRate));
        _releaseCoeff = MathF.Exp(-1f / (releaseMs / 1000f * sampleRate));

        _envelope = new float[source.WaveFormat.Channels];
        _gain = new float[source.WaveFormat.Channels];
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

            var power = x * x;
            var coeff = power > _envelope[ch] ? _attackCoeff : _releaseCoeff;
            _envelope[ch] = coeff * _envelope[ch] + (1f - coeff) * power;

            // Without this floor, silence drives envelope toward 0, producing
            // -Infinity/NaN here. NaN never recovers through further one-pole
            // smoothing, permanently poisoning _gain for the rest of the session.
            var levelDb = 10f * MathF.Log10(MathF.Max(_envelope[ch], 1e-9f));

            var targetGain = levelDb < _thresholdDb ? 0f : 1f;
            var gainCoeff = targetGain < _gain[ch] ? _attackCoeff : _releaseCoeff;
            _gain[ch] = gainCoeff * _gain[ch] + (1f - gainCoeff) * targetGain;

            buffer[i] = x * _gain[ch];
        }

        return samplesRead;
    }
}
