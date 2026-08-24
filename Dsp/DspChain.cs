using NAudio.Wave;
using NoelMic.Config;

namespace NoelMic.Dsp;

/// <summary>
/// Composes the full DSP pipeline (HPF -> gate -> EQ -> compressor -> limiter)
/// in the order the spec requires. A thin delegator, same idiom as
/// MuteGateSampleProvider — no new abstraction beyond composition.
/// </summary>
public sealed class DspChain : ISampleProvider
{
    private readonly ISampleProvider _inner;

    // Only one EQ presence band in v1: config.json exposes a single gain
    // knob, so an un-tunable 2nd band the user hasn't heard yet would be
    // premature. Biquad's reusability makes adding a 2nd band trivial later.
    public DspChain(ISampleProvider source, AppConfig config)
    {
        var sampleRate = source.WaveFormat.SampleRate;

        ISampleProvider chain = new Biquad(source, BiquadCoefficients.HighPass(sampleRate, config.Eq.HighpassHz));
        chain = new NoiseGate(chain, config.Gate.ThresholdDb);
        chain = new Biquad(chain, BiquadCoefficients.PeakingEq(sampleRate, 4000f, config.Eq.PresenceBoostDb));
        chain = new Compressor(chain, config.Compressor.ThresholdDb, config.Compressor.Ratio);
        chain = new Limiter(chain);

        _inner = chain;
    }

    public WaveFormat WaveFormat => _inner.WaveFormat;

    public int Read(Span<float> buffer) => _inner.Read(buffer);
}
