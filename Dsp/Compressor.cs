using NAudio.Wave;

namespace NoelMic.Dsp;

/// <summary>
/// Simple envelope-follower compressor: reduces gain above the threshold by
/// the configured ratio. No auto makeup gain in v1.
/// </summary>
public sealed class Compressor : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _sampleRate;
    private double _thresholdDb;
    private double _ratio;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _makeupGainLinear;
    private float _mix;

    // Per-channel envelope and gain state, sized once at construction — this
    // is the only allocation for this stage, never per-Read.
    private readonly float[] _envelope;
    private readonly float[] _gain;

    // Polled by the Settings UI on a Timer, written from the audio thread on
    // every Read() — same volatile-not-lock idiom as LevelMeter.CurrentDb.
    private volatile float _currentReductionDb;

    // NoiseGate has its own envelope follower with a different curve (binary
    // open/close) and purpose (silence removal vs. dynamic range control) —
    // the two don't share enough to earn a common base class.
    public Compressor(ISampleProvider source, double thresholdDb, double ratio,
        float attackMs = 5f, float releaseMs = 100f, double makeupGainDb = 0.0, double mixPercent = 100.0)
    {
        _source = source;
        _sampleRate = source.WaveFormat.SampleRate;

        SetCoefficients(thresholdDb, ratio, attackMs, releaseMs, makeupGainDb, mixPercent);

        _envelope = new float[source.WaveFormat.Channels];
        _gain = new float[source.WaveFormat.Channels];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float CurrentReductionDb => _currentReductionDb;

    // Shared by the constructor and UpdateParameters — only derived
    // coefficient fields are touched here, never the _envelope/_gain state
    // arrays, so a live parameter change never resets compressor state and
    // clicks.
    private void SetCoefficients(double thresholdDb, double ratio, float attackMs, float releaseMs,
        double makeupGainDb, double mixPercent)
    {
        _thresholdDb = thresholdDb;
        _ratio = ratio;
        _attackCoeff = MathF.Exp(-1f / (attackMs / 1000f * _sampleRate));
        _releaseCoeff = MathF.Exp(-1f / (releaseMs / 1000f * _sampleRate));
        _makeupGainLinear = MathF.Pow(10f, (float)makeupGainDb / 20f);
        _mix = (float)(mixPercent / 100.0);
    }

    public void UpdateParameters(double thresholdDb, double ratio, float attackMs, float releaseMs,
        double makeupGainDb, double mixPercent)
    {
        SetCoefficients(thresholdDb, ratio, attackMs, releaseMs, makeupGainDb, mixPercent);
    }

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

            // Makeup gain is a fixed post-multiply, not folded into _gain —
            // feeding it through the envelope-driven smoothing would slow
            // down attack/release ballistics it has nothing to do with.
            var wet = x * _gain[ch] * _makeupGainLinear;
            buffer[i] = x * (1f - _mix) + wet * _mix;

            if (ch == 0)
            {
                // Pre-makeup reduction, so metering reflects dynamics control
                // only and isn't inflated/masked by the makeup gain knob.
                _currentReductionDb = 20f * MathF.Log10(MathF.Max(_gain[ch], 1e-9f));
            }
        }

        return samplesRead;
    }
}
