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

    [Fact]
    public void HighShelf_WithPositiveGain_RaisesHighFrequencyOutputAboveLowFrequencyOutput()
    {
        const int settleSamples = 2000;
        const int measureSamples = 48000;
        var coefficients = BiquadCoefficients.HighShelf(SampleRate, f0: 4000f, dBgain: 12f);

        var lowFreqRms = MeasureShelfRms(coefficients, toneHz: 200f, settleSamples, measureSamples);
        var highFreqRms = MeasureShelfRms(coefficients, toneHz: 10000f, settleSamples, measureSamples);

        Assert.True(
            highFreqRms > lowFreqRms * 1.5f,
            $"Expected 10kHz output (RMS={highFreqRms}) to be boosted well above 200Hz output " +
            $"(RMS={lowFreqRms}) through a +12dB high shelf at 4kHz.");
    }

    [Fact]
    public void LowShelf_WithPositiveGain_RaisesLowFrequencyOutputAboveHighFrequencyOutput()
    {
        const int settleSamples = 2000;
        const int measureSamples = 48000;
        var coefficients = BiquadCoefficients.LowShelf(SampleRate, f0: 200f, dBgain: 12f);

        var lowFreqRms = MeasureShelfRms(coefficients, toneHz: 50f, settleSamples, measureSamples);
        var highFreqRms = MeasureShelfRms(coefficients, toneHz: 5000f, settleSamples, measureSamples);

        Assert.True(
            lowFreqRms > highFreqRms * 1.5f,
            $"Expected 50Hz output (RMS={lowFreqRms}) to be boosted well above 5000Hz output " +
            $"(RMS={highFreqRms}) through a +12dB low shelf at 200Hz.");
    }

    private static float MeasureShelfRms(BiquadCoefficients coefficients, float toneHz, int settleSamples, int measureSamples)
    {
        var source = new SineWaveSampleProvider(SampleRate, channels: 1, toneHz);
        var filter = new Biquad(source, coefficients);

        filter.ReadAll(settleSamples);
        var measured = filter.ReadAll(measureSamples);

        return measured.Rms();
    }

    [Fact]
    public void UpdateCoefficients_MidStream_LetsThePreviouslyAttenuatedToneThroughOnceTheCutoffDrops()
    {
        const float amplitude = 0.5f;
        const float toneHz = 200f;
        var source = new SineWaveSampleProvider(SampleRate, channels: 1, toneHz, amplitude);
        var filter = new Biquad(source, BiquadCoefficients.HighPass(SampleRate, cutoffHz: 1000f));

        filter.ReadAll(2000); // settle with the 200Hz tone heavily attenuated by the 1kHz cutoff.
        var beforeUpdate = filter.ReadAll(4000);

        filter.UpdateCoefficients(BiquadCoefficients.HighPass(SampleRate, cutoffHz: 50f));

        filter.ReadAll(2000); // settle again at the new, much lower cutoff.
        var afterUpdate = filter.ReadAll(4000);

        Assert.True(
            beforeUpdate.Rms() < amplitude * 0.3f,
            $"Expected the 200Hz tone to be attenuated by the 1kHz cutoff before the update, got RMS={beforeUpdate.Rms()}.");
        Assert.True(
            afterUpdate.Rms() > beforeUpdate.Rms() * 3f,
            $"Expected the 200Hz tone to pass through mostly unattenuated after dropping the cutoff to 50Hz, " +
            $"got RMS={afterUpdate.Rms()} vs. {beforeUpdate.Rms()} before the update.");
        Assert.All(afterUpdate, sample => Assert.True(float.IsFinite(sample)));
    }

    [Fact]
    public void UpdateCoefficients_MidStream_NeverProducesNaNOrACorruptingDiscontinuitySpike()
    {
        const float amplitude = 0.6f;
        var source = new SineWaveSampleProvider(SampleRate, channels: 1, frequencyHz: 440f, amplitude);
        var filter = new Biquad(source, BiquadCoefficients.HighPass(SampleRate, cutoffHz: 300f));

        filter.ReadAll(2000);

        filter.UpdateCoefficients(BiquadCoefficients.PeakingEq(SampleRate, centerHz: 3000f, gainDb: 12f));

        var buffer = new float[64];
        filter.Read(buffer); // the first buffer straight after the coefficient swap.

        Assert.All(buffer, sample => Assert.True(float.IsFinite(sample), $"Sample {sample} was non-finite right after UpdateCoefficients."));

        // The filter's z1/z2 memory is deliberately preserved across
        // UpdateCoefficients (see the comment on that method) precisely so a
        // live parameter change can't click by resetting state -- so no
        // sample right after the swap should spike wildly beyond what a
        // stable filter driven by this amplitude could ever produce.
        Assert.All(buffer, sample => Assert.True(
            MathF.Abs(sample) < amplitude * 4f,
            $"Sample {sample} spiked to more than 4x the input amplitude ({amplitude}) right after UpdateCoefficients " +
            "-- suggests filter state was reset rather than preserved."));
    }
}
