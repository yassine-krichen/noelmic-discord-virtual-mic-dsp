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
}
