using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class BiquadTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void HighPass_AttenuatesLowFrequencyToneRelativeToPassbandTone()
    {
        const int settleSamples = 2000;
        const int measureSamples = 48000;

        var lowFreqRms = MeasureFilteredRms(20f, settleSamples, measureSamples);
        var passbandRms = MeasureFilteredRms(1000f, settleSamples, measureSamples);

        Assert.True(
            lowFreqRms < passbandRms * 0.1f,
            $"Expected 20Hz output (RMS={lowFreqRms}) to be attenuated well below " +
            $"1000Hz passband output (RMS={passbandRms}) through a 300Hz high-pass filter.");
    }

    private static float MeasureFilteredRms(float toneHz, int settleSamples, int measureSamples)
    {
        var source = new SineWaveSampleProvider(SampleRate, channels: 1, toneHz);
        var filter = new Biquad(source, BiquadCoefficients.HighPass(SampleRate, cutoffHz: 300f));

        filter.ReadAll(settleSamples);
        var measured = filter.ReadAll(measureSamples);

        return measured.Rms();
    }
}
