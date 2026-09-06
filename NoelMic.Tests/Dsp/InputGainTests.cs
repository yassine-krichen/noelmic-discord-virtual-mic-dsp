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
    public void UpdateGainDb_MidStream_SmoothlyConvergesToTheNewTargetInsteadOfJumpingInstantly()
    {
        const float value = 0.2f;
        const float initialGainDb = 0f;
        const float newGainDb = 12f;
        var expectedNewLinearGain = MathF.Pow(10f, newGainDb / 20f);

        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var gain = new InputGain(source, gainDb: initialGainDb);

        gain.ReadAll(sampleCount: 256); // settle at the initial (unity) gain.
        gain.UpdateGainDb(newGainDb);

        // ~8ms smoothing time constant at 8000Hz is 64 samples -- the very
        // next sample must still be close to the old target, not already at
        // the new one.
        var immediatelyAfterUpdate = gain.ReadAll(sampleCount: 1);
        Assert.True(
            MathF.Abs(immediatelyAfterUpdate[0] - value) < value * 0.1f,
            $"Expected the sample right after UpdateGainDb to still be near the pre-update level ({value}), " +
            $"got {immediatelyAfterUpdate[0]} -- suggests the gain jumped instantly instead of smoothing.");

        // Read far beyond several smoothing time constants so it has time to converge.
        var converged = gain.ReadAll(sampleCount: 4000);
        var convergedSample = converged[^1];
        var expectedConvergedValue = value * expectedNewLinearGain;

        Assert.True(
            MathF.Abs(convergedSample - expectedConvergedValue) < expectedConvergedValue * 0.02f,
            $"Expected gain to converge to ~{expectedConvergedValue} (a +{newGainDb}dB target) well after the " +
            $"smoothing window, got {convergedSample}.");

        // No sample along the ramp should overshoot the new target -- a
        // one-pole approach toward a higher target rises monotonically and
        // never clicks past it.
        Assert.All(converged, sample => Assert.True(
            sample <= expectedConvergedValue * 1.02f,
            $"Sample {sample} overshot the new target gain's output ({expectedConvergedValue}) during the ramp."));
    }

    [Fact]
    public void UpdateGainDb_MidStream_NeverProducesNaNOrInfinityWhileRampingDown()
    {
        const float value = 0.5f;
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var gain = new InputGain(source, gainDb: 18f);

        gain.ReadAll(sampleCount: 256);
        gain.UpdateGainDb(-24f);

        var afterUpdate = gain.ReadAll(sampleCount: 2000);

        Assert.All(afterUpdate, sample => Assert.True(float.IsFinite(sample), $"Sample {sample} was non-finite after UpdateGainDb."));
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
