using NAudio.Wave;

namespace NoelMic.Dsp;

/// <summary>
/// Sibilance tamer: a sidechain-detected dynamic high shelf that pulls down
/// harshness around the de-ess frequency only when it's actually loud,
/// instead of a static cut that would dull every "s" and "sh".
/// </summary>
public sealed class DeEsser : ISampleProvider
{
    private const float SidechainHpfHz = 6000f;
    private const float SidechainAttackMs = 1f;
    private const float SidechainReleaseMs = 40f;

    private readonly ISampleProvider _source;
    private readonly float _sampleRate;
    private float _deEsserHz;
    private double _thresholdDb;
    private double _ratio;
    private float _maxReductionDb;
    private readonly BiquadCoefficients _sidechainHpf;
    private readonly float _sidechainAttackCoeff;
    private readonly float _sidechainReleaseCoeff;

    // Per-channel state, sized once at construction — this is the only
    // allocation for this stage, never per-Read.
    private readonly float[] _sidechainZ1;
    private readonly float[] _sidechainZ2;
    private readonly float[] _sidechainEnvelope;
    private readonly float[] _shelfZ1;
    private readonly float[] _shelfZ2;

    // One reduction-shelf coefficient set per channel, overwritten in place
    // once per buffer (never reallocated) — see the per-buffer-not-per-sample
    // comment in Read().
    private readonly BiquadCoefficients[] _reductionShelf;

    // Polled by the Settings UI on a Timer, written from the audio thread on
    // every Read() — same volatile-not-lock idiom as LevelMeter.CurrentDb.
    private volatile float _currentReductionDb;

    public DeEsser(ISampleProvider source, float deEsserHz, double thresholdDb, double ratio, float maxReductionDb)
    {
        _source = source;
        _sampleRate = source.WaveFormat.SampleRate;
        _deEsserHz = deEsserHz;
        _thresholdDb = thresholdDb;
        _ratio = ratio;
        _maxReductionDb = maxReductionDb;

        _sidechainHpf = BiquadCoefficients.HighPass(_sampleRate, SidechainHpfHz);
        _sidechainAttackCoeff = MathF.Exp(-1f / (SidechainAttackMs / 1000f * _sampleRate));
        _sidechainReleaseCoeff = MathF.Exp(-1f / (SidechainReleaseMs / 1000f * _sampleRate));

        var channels = source.WaveFormat.Channels;
        _sidechainZ1 = new float[channels];
        _sidechainZ2 = new float[channels];
        _sidechainEnvelope = new float[channels];
        _shelfZ1 = new float[channels];
        _shelfZ2 = new float[channels];

        _reductionShelf = new BiquadCoefficients[channels];
        for (var ch = 0; ch < channels; ch++)
        {
            _reductionShelf[ch] = BiquadCoefficients.HighShelf(_sampleRate, _deEsserHz, 0f);
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float CurrentReductionDb => _currentReductionDb;

    // It already recomputes its reduction shelf every buffer from these
    // fields, so no extra coefficient invalidation is needed beyond just
    // assigning them — unlike the other stages there's no separate
    // SetCoefficients helper to share with the constructor.
    public void UpdateParameters(float deEsserHz, double thresholdDb, double ratio, float maxReductionDb)
    {
        _deEsserHz = deEsserHz;
        _thresholdDb = thresholdDb;
        _ratio = ratio;
        _maxReductionDb = maxReductionDb;
    }

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);
        var channels = _sidechainEnvelope.Length;
        var maxReductionDbThisBuffer = 0f;

        // The reduction shelf is rebuilt once per buffer (not per sample):
        // rebuilding RBJ coefficients from scratch every sample would be
        // wasteful, and de-essing reacts on a syllable timescale (tens of
        // milliseconds) — a buffer's worth of latency in updating the amount
        // of reduction is inaudible.
        for (var ch = 0; ch < channels; ch++)
        {
            var sidechainDb = 20f * MathF.Log10(MathF.Max(_sidechainEnvelope[ch], 1e-9f));

            var reductionDb = 0f;
            if (sidechainDb > _thresholdDb)
            {
                reductionDb = (float)((sidechainDb - _thresholdDb) * (1 - 1 / _ratio));
            }

            reductionDb = Math.Clamp(reductionDb, 0f, _maxReductionDb);

            if (reductionDb > maxReductionDbThisBuffer)
            {
                maxReductionDbThisBuffer = reductionDb;
            }

            _reductionShelf[ch] = BiquadCoefficients.HighShelf(_sampleRate, _deEsserHz, -reductionDb);
        }

        _currentReductionDb = maxReductionDbThisBuffer;

        for (var i = 0; i < samplesRead; i++)
        {
            var ch = i % channels;
            var x = buffer[i];

            // Sidechain HPF is detection-only — it never touches the output
            // signal, only feeds the envelope follower that decides how much
            // to reduce.
            var sidechainSample = BiquadCoefficients.Apply(in _sidechainHpf, x, ref _sidechainZ1[ch], ref _sidechainZ2[ch]);
            var sidechainPeak = MathF.Abs(sidechainSample);

            var envCoeff = sidechainPeak > _sidechainEnvelope[ch] ? _sidechainAttackCoeff : _sidechainReleaseCoeff;
            _sidechainEnvelope[ch] = envCoeff * _sidechainEnvelope[ch] + (1f - envCoeff) * sidechainPeak;

            buffer[i] = BiquadCoefficients.Apply(in _reductionShelf[ch], x, ref _shelfZ1[ch], ref _shelfZ2[ch]);
        }

        return samplesRead;
    }
}
