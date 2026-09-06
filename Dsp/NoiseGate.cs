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
    private readonly double _hysteresisDb;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;
    private readonly int _holdSamples;

    // Per-channel envelope and gain state, sized once at construction — this
    // is the only allocation for this stage, never per-Read.
    private readonly float[] _envelope;
    private readonly float[] _gain;
    private readonly bool[] _open;
    private readonly int[] _holdCounter;

    public NoiseGate(ISampleProvider source, double thresholdDb, float attackMs = 5f,
        float releaseMs = 150f, double hysteresisDb = 6.0, float holdMs = 120f)
    {
        _source = source;
        _thresholdDb = thresholdDb;
        _hysteresisDb = hysteresisDb;

        var sampleRate = source.WaveFormat.SampleRate;
        _attackCoeff = MathF.Exp(-1f / (attackMs / 1000f * sampleRate));
        _releaseCoeff = MathF.Exp(-1f / (releaseMs / 1000f * sampleRate));
        _holdSamples = (int)(holdMs / 1000f * sampleRate);

        _envelope = new float[source.WaveFormat.Channels];
        _gain = new float[source.WaveFormat.Channels];
        _open = new bool[source.WaveFormat.Channels];
        _holdCounter = new int[source.WaveFormat.Channels];
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

            if (levelDb >= _thresholdDb)
            {
                _open[ch] = true;
                _holdCounter[ch] = _holdSamples;
            }
            else if (_holdCounter[ch] > 0)
            {
                _holdCounter[ch]--;
            }
            else if (levelDb < _thresholdDb - _hysteresisDb)
            {
                _open[ch] = false;
            }
            // else: hold expired but level sits in the hysteresis dead zone
            // between (threshold - hysteresisDb) and threshold — hold current
            // open/closed state, this dead zone is what prevents chatter
            // right at the threshold edge.

            var targetGain = _open[ch] ? 1f : 0f;
            var gainCoeff = targetGain < _gain[ch] ? _attackCoeff : _releaseCoeff;
            _gain[ch] = gainCoeff * _gain[ch] + (1f - gainCoeff) * targetGain;

            buffer[i] = x * _gain[ch];
        }

        return samplesRead;
    }
}
