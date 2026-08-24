using NAudio.Wave;

namespace NoelMic.Dsp;

/// <summary>
/// Simple envelope-follower compressor: reduces gain above the threshold by
/// the configured ratio. No auto makeup gain in v1.
/// </summary>
public sealed class Compressor : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly double _thresholdDb;
    private readonly double _ratio;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;

    // Per-channel envelope and gain state, sized once at construction — this
    // is the only allocation for this stage, never per-Read.
    private readonly float[] _envelope;
    private readonly float[] _gain;

    // NoiseGate has its own envelope follower with a different curve (binary
    // open/close) and purpose (silence removal vs. dynamic range control) —
    // the two don't share enough to earn a common base class.
    public Compressor(ISampleProvider source, double thresholdDb, double ratio, float attackMs = 10f, float releaseMs = 100f)
    {
        _source = source;
        _thresholdDb = thresholdDb;
        _ratio = ratio;

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
            var envCoeff = power > _envelope[ch] ? _attackCoeff : _releaseCoeff;
            _envelope[ch] = envCoeff * _envelope[ch] + (1f - envCoeff) * power;

            // Same mandatory epsilon floor as NoiseGate — otherwise silence
            // produces -Infinity/NaN that never recovers through smoothing.
            var levelDb = 10f * MathF.Log10(MathF.Max(_envelope[ch], 1e-9f));

            float targetGain;
            if (levelDb > _thresholdDb)
            {
                var reductionDb = (levelDb - _thresholdDb) * (1 - 1 / _ratio);
                targetGain = MathF.Pow(10f, (float)-reductionDb / 20f);
            }
            else
            {
                targetGain = 1f;
            }

            var gainCoeff = targetGain < _gain[ch] ? _attackCoeff : _releaseCoeff;
            _gain[ch] = gainCoeff * _gain[ch] + (1f - gainCoeff) * targetGain;

            buffer[i] = x * _gain[ch];
        }

        return samplesRead;
    }
}
