using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class LevelMeterTests
{
    private const int SampleRate = 8000;

    [Fact]
    public void Read_PassesSamplesThroughWithoutModification()
    {
        const float value = 0.37f;
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var meter = new LevelMeter(source);

        var output = meter.ReadAll(sampleCount: 1024);

        Assert.All(output, sample => Assert.Equal(value, sample));
    }

    [Fact]
    public void CurrentDb_WithPersistentSilence_IsEpsilonFlooredRatherThanNegativeInfinity()
    {
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: 0f);
        var meter = new LevelMeter(source);

        meter.ReadAll(sampleCount: 20000);

        Assert.True(float.IsFinite(meter.CurrentDb), $"Expected CurrentDb to be finite on silence, got {meter.CurrentDb}.");
    }

    [Fact]
    public void CurrentDb_WithLouderInput_ReportsHigherReadingThanQuieterInput()
    {
        var quietMeter = new LevelMeter(new ConstantSampleProvider(SampleRate, channels: 1, value: 0.01f));
        var loudMeter = new LevelMeter(new ConstantSampleProvider(SampleRate, channels: 1, value: 0.5f));

        quietMeter.ReadAll(sampleCount: 5000);
        loudMeter.ReadAll(sampleCount: 5000);

        Assert.True(
            loudMeter.CurrentDb > quietMeter.CurrentDb,
            $"Expected louder input ({loudMeter.CurrentDb}dB) to report a higher reading than quieter input ({quietMeter.CurrentDb}dB).");
    }

    [Fact]
    public void CurrentDb_WithFullScaleInput_SettlesToPlausibleDbfsNearZero()
    {
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: 1f);
        var meter = new LevelMeter(source);

        meter.ReadAll(sampleCount: 20000);

        Assert.True(
            meter.CurrentDb is > -1f and <= 0.5f,
            $"Expected a full-scale constant signal to settle near 0dBFS, got {meter.CurrentDb}dB.");
    }
}
