using NAudio.Wave;

namespace NoelMic.Dsp;

/// <summary>
/// Non-mutating pass-through stage that tracks the signal's current level in
/// dBFS, so the Settings UI can show the user a live readout to dial input
/// gain against instead of guessing. Placed right after <see cref="InputGain"/>
/// in the chain.
/// </summary>
public sealed class LevelMeter : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;

    // Per-channel envelope state, sized once at construction — this is the
    // only allocation for this stage, never per-Read.
    private readonly float[] _envelope;

    // Polled by the Settings UI on a Timer rather than pushed via
    // SynchronizationContext.Post — this field is written from the audio
    // capture thread on every Read() call, far too often to marshal onto the
    // UI thread each time, and a slightly stale reading is harmless for a
    // level meter. volatile is the minimal guarantee the UI thread needs to
    // see the latest write without a full lock.
    private volatile float _currentDb = -100f;

    public LevelMeter(ISampleProvider source, float attackMs = 10f, float releaseMs = 300f)
    {
        _source = source;

        var sampleRate = source.WaveFormat.SampleRate;
        _attackCoeff = MathF.Exp(-1f / (attackMs / 1000f * sampleRate));
        _releaseCoeff = MathF.Exp(-1f / (releaseMs / 1000f * sampleRate));

        _envelope = new float[source.WaveFormat.Channels];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float CurrentDb => _currentDb;

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);
        var channels = _envelope.Length;
        var peakEnvelope = 0f;

        for (var i = 0; i < samplesRead; i++)
        {
            var ch = i % channels;
            var peak = MathF.Abs(buffer[i]);

            var coeff = peak > _envelope[ch] ? _attackCoeff : _releaseCoeff;
            _envelope[ch] = coeff * _envelope[ch] + (1f - coeff) * peak;

            if (_envelope[ch] > peakEnvelope)
            {
                peakEnvelope = _envelope[ch];
            }
        }

        // Same mandatory epsilon floor as every other dB conversion in this
        // codebase — otherwise silence produces -Infinity here.
        _currentDb = 20f * MathF.Log10(MathF.Max(peakEnvelope, 1e-9f));

        return samplesRead;
    }
}
