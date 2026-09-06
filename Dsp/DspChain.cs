using NAudio.Wave;
using NoelMic.Config;

namespace NoelMic.Dsp;

/// <summary>
/// Composes the full DSP pipeline in the order the spec requires:
/// input gain -> level meter -> HPF -> gate -> low shelf -> mud-cut EQ ->
/// presence EQ -> air shelf -> de-esser -> compressor -> saturation ->
/// limiter. A thin delegator, same idiom as MuteGateSampleProvider — no new
/// abstraction beyond composition.
/// </summary>
public sealed class DspChain : ISampleProvider
{
    private readonly ISampleProvider _inner;
    private readonly LevelMeter _levelMeter;

    public DspChain(ISampleProvider source, AppConfig config)
    {
        var p = DspParameters.Resolve(config);
        var sampleRate = source.WaveFormat.SampleRate;

        // Every stage is always constructed, even for presets where a
        // parameter is a no-op (e.g. Raw's 0dB shelves/mud-cut) — no
        // conditional branching, so every preset runs the exact same chain
        // topology and only the coefficients differ.
        ISampleProvider chain = new InputGain(source, p.InputGainDb);

        _levelMeter = new LevelMeter(chain);
        chain = _levelMeter;

        chain = new Biquad(chain, BiquadCoefficients.HighPass(sampleRate, p.HighpassHz));
        chain = new NoiseGate(chain, p.GateThresholdDb,
            attackMs: p.GateAttackMs, releaseMs: p.GateReleaseMs,
            hysteresisDb: p.GateHysteresisDb, holdMs: p.GateHoldMs);

        chain = new Biquad(chain, BiquadCoefficients.LowShelf(sampleRate, p.LowShelfHz, p.LowShelfDb));

        // Mud-cut sits post-gate, not pre-gate like the HPF: the HPF removes
        // rumble that would otherwise mislead the gate's envelope detector,
        // but this mild narrow cut doesn't carry that same justification —
        // and stacking it onto the detection path would compound two
        // simultaneous changes (gate ballistics + new EQ) during this retest.
        chain = new Biquad(chain, BiquadCoefficients.PeakingEq(sampleRate, p.MudCutHz, p.MudCutDb, p.MudCutQ));
        chain = new Biquad(chain, BiquadCoefficients.PeakingEq(sampleRate, p.PresenceHz, p.PresenceDb, p.PresenceQ));
        chain = new Biquad(chain, BiquadCoefficients.HighShelf(sampleRate, p.AirHz, p.AirDb));

        chain = new DeEsser(chain, p.DeEsserHz, p.DeEsserThresholdDb, p.DeEsserRatio, p.DeEsserMaxReductionDb);

        chain = new Compressor(chain, p.CompThresholdDb, p.CompRatio,
            attackMs: p.CompAttackMs, releaseMs: p.CompReleaseMs,
            makeupGainDb: p.CompMakeupGainDb, mixPercent: p.CompMixPercent);

        chain = new Saturation(chain, p.SaturationDrive);

        chain = new Limiter(chain, ceilingDb: p.LimiterCeilingDb,
            attackMs: p.LimiterAttackMs, releaseMs: p.LimiterReleaseMs);

        _inner = chain;
    }

    public WaveFormat WaveFormat => _inner.WaveFormat;

    public float CurrentInputLevelDb => _levelMeter.CurrentDb;

    public int Read(Span<float> buffer) => _inner.Read(buffer);
}
