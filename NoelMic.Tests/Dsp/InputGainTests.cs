using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class InputGainTests
{
    private const int SampleRate = 8000;

    [Fact]
    public void Read_WithZeroDbGain_PassesInputThroughAtUnity()
    {
        const float value = 0.37f;
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var gain = new InputGain(source, gainDb: 0f);

        var output = gain.ReadAll(sampleCount: 512);

        Assert.All(output, sample => Assert.Equal(value, sample));
    }

    [Fact]
    public void Read_WithPositiveSixDbGain_ApproximatelyDoublesAmplitude()
    {
        const float value = 0.2f;
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var gain = new InputGain(source, gainDb: 6f);

        var output = gain.ReadAll(sampleCount: 512);

        Assert.All(output, sample => Assert.True(
            MathF.Abs(sample - value * 2f) < value * 0.02f,
            $"Expected +6dB gain to approximately double {value} to ~{value * 2f}, got {sample}."));
    }

    [Fact]
    public void Read_WithNegativeSixDbGain_ApproximatelyHalvesAmplitude()
    {
        const float value = 0.2f;
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var gain = new InputGain(source, gainDb: -6f);

        var output = gain.ReadAll(sampleCount: 512);

        Assert.All(output, sample => Assert.True(
            MathF.Abs(sample - value * 0.5f) < value * 0.02f,
            $"Expected -6dB gain to approximately halve {value} to ~{value * 0.5f}, got {sample}."));
    }

    [Fact]
    public void Read_AcrossManyCalls_AppliesConsistentGainWithoutCorruptingState()
    {
        const float value = 0.1f;
        var expected = value * MathF.Pow(10f, 6f / 20f);
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var gain = new InputGain(source, gainDb: 6f);
        var buffer = new float[256];

        for (var read = 0; read < 100; read++)
        {
            gain.Read(buffer);

            foreach (var sample in buffer)
            {
                Assert.True(
                    MathF.Abs(sample - expected) < 1e-4f,
                    $"Sample at read #{read} was {sample}, expected consistent gain of {expected}.");
            }
        }
    }
}
