using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class CompressorTests
{
    private const int SampleRate = 8000;
    private const double ThresholdDb = -20;

    [Fact]
    public void Read_WithSignalBelowThreshold_PassesThroughAtApproximatelyUnityGain()
    {
        const float amplitude = 0.05f; // ~-26dB, below -20dB threshold
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var compressor = new Compressor(source, ThresholdDb, ratio: 4.0);

        compressor.ReadAll(sampleCount: 20000);
        var settled = compressor.ReadAll(sampleCount: 1000);

        Assert.True(
            settled.Rms() > amplitude * 0.9f,
            $"Expected below-threshold signal to pass near-unchanged, got RMS={settled.Rms()} for input {amplitude}.");
    }

    [Fact]
    public void Read_WithSignalAboveThreshold_ReducesOutputBelowInputAmplitude()
    {
        const float amplitude = 0.5f; // well above -20dB threshold
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var compressor = new Compressor(source, ThresholdDb, ratio: 4.0);

        compressor.ReadAll(sampleCount: 20000);
        var settled = compressor.ReadAll(sampleCount: 1000);

        Assert.True(
            settled.Rms() < amplitude * 0.9f,
            $"Expected above-threshold signal to be gain-reduced, got RMS={settled.Rms()} for input {amplitude}.");
    }

    [Fact]
    public void Read_WithHigherRatio_ReducesOutputMoreThanLowerRatio_ForSameOverThresholdSignal()
    {
        const float amplitude = 0.5f;

        var lowRatioOutput = SettledRms(amplitude, ratio: 2.0);
        var highRatioOutput = SettledRms(amplitude, ratio: 8.0);

        Assert.True(
            highRatioOutput < lowRatioOutput,
            $"Expected ratio 8 (RMS={highRatioOutput}) to reduce the signal more than ratio 2 (RMS={lowRatioOutput}).");
    }

    private static float SettledRms(float amplitude, double ratio)
    {
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var compressor = new Compressor(source, ThresholdDb, ratio);

        compressor.ReadAll(sampleCount: 20000);
        return compressor.ReadAll(sampleCount: 1000).Rms();
    }

    [Fact]
    public void Read_WithMixPercentAt100_ProducesSameOutputAsDefaultMix()
    {
        const float amplitude = 0.5f;
        var defaultOutput = SettledRms(amplitude, ratio: 4.0);

        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var compressor = new Compressor(source, ThresholdDb, ratio: 4.0, mixPercent: 100.0);
        compressor.ReadAll(sampleCount: 20000);
        var explicitOutput = compressor.ReadAll(sampleCount: 1000).Rms();

        Assert.Equal(defaultOutput, explicitOutput, precision: 5);
    }

    [Fact]
    public void Read_WithMixPercentAtZero_PassesInputThroughUnchangedBypassingReductionAndMakeupGain()
    {
        const float amplitude = 0.5f; // above threshold, so a wet signal would clearly differ
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var compressor = new Compressor(source, ThresholdDb, ratio: 4.0, makeupGainDb: 6.0, mixPercent: 0.0);

        var output = compressor.ReadAll(sampleCount: 1000);

        Assert.All(output, sample => Assert.Equal(amplitude, sample));
    }

    [Fact]
    public void Read_WithIntermediateMixPercent_LandsBetweenDryAndFullyWetOutput()
    {
        const float amplitude = 0.5f;

        var dryRms = MixedSettledRms(amplitude, mixPercent: 0.0);
        var wetRms = MixedSettledRms(amplitude, mixPercent: 100.0);
        var midRms = MixedSettledRms(amplitude, mixPercent: 75.0);

        Assert.True(
            midRms > Math.Min(dryRms, wetRms) && midRms < Math.Max(dryRms, wetRms),
            $"Expected mix=75 RMS ({midRms}) to land strictly between dry ({dryRms}) and fully wet ({wetRms}).");
    }

    private static float MixedSettledRms(float amplitude, double mixPercent)
    {
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var compressor = new Compressor(source, ThresholdDb, ratio: 4.0, makeupGainDb: 3.0, mixPercent: mixPercent);

        compressor.ReadAll(sampleCount: 20000);
        return compressor.ReadAll(sampleCount: 1000).Rms();
    }

    [Fact]
    public void CurrentReductionDb_IsAtOrBelowZeroAndNearZeroForASignalBelowThreshold()
    {
        const float amplitude = 0.05f; // below -20dB threshold
        var source = new ConstantSampleProvider(SampleRate, channels: 1, amplitude);
        var compressor = new Compressor(source, ThresholdDb, ratio: 4.0);

        compressor.ReadAll(sampleCount: 20000);

        Assert.True(float.IsFinite(compressor.CurrentReductionDb));
        Assert.True(
            compressor.CurrentReductionDb is <= 0f and > -1f,
            $"Expected near-zero reduction below threshold, got {compressor.CurrentReductionDb}dB.");
    }

    [Fact]
    public void UpdateParameters_MidStream_LoweringThresholdIncreasesReductionWithoutClickingOrNaN()
    {
        const float amplitude = 0.5f; // above -20dB threshold
        var source = new ConstantSampleProvider(SampleRate, channels: 1, amplitude);
        var compressor = new Compressor(source, ThresholdDb, ratio: 4.0);

        compressor.ReadAll(sampleCount: 20000);
        var reductionBefore = compressor.CurrentReductionDb;

        compressor.UpdateParameters(thresholdDb: -50, ratio: 4.0, attackMs: 5f, releaseMs: 100f, makeupGainDb: 0.0, mixPercent: 100.0);

        var buffer = new float[64];
        compressor.Read(buffer);
        Assert.All(buffer, sample => Assert.True(float.IsFinite(sample), $"Sample {sample} was non-finite right after UpdateParameters."));

        // Envelope/gain state is preserved across UpdateParameters, so the
        // compressor eases toward the new (lower) target gain via its
        // attack curve rather than snapping -- no sample right after the
        // update should exceed the pre-update input amplitude.
        Assert.All(buffer, sample => Assert.True(
            MathF.Abs(sample) <= amplitude * 1.05f,
            $"Sample {sample} exceeded the input amplitude ({amplitude}) right after UpdateParameters."));

        compressor.ReadAll(sampleCount: 20000);
        var reductionAfter = compressor.CurrentReductionDb;

        Assert.True(
            reductionAfter < reductionBefore,
            $"Expected a much lower threshold to increase reduction (a more negative dB value), " +
            $"got before={reductionBefore}dB, after={reductionAfter}dB.");
    }
}
