using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class LimiterTests
{
    private const int SampleRate = 8000;
    private const float CeilingDb = -1f;

    [Fact]
    public void Read_WithSignalFarAboveCeiling_ClampsEverySampleWithinCeiling_IncludingBeforeEnvelopeSettles()
    {
        var ceilingLinear = MathF.Pow(10f, CeilingDb / 20f);
        const float amplitude = 10f * 0.891f; // ~10x the -1dB ceiling

        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var limiter = new Limiter(source, ceilingDb: CeilingDb);
        var buffer = new float[512];

        // Checked from the very first Read (not after a settle period): the
        // hard clamp must hold even while the smoothed gain envelope hasn't
        // caught up yet — that's the guarantee this stage exists to provide.
        for (var read = 0; read < 20; read++)
        {
            limiter.Read(buffer);

            foreach (var sample in buffer)
            {
                Assert.True(
                    MathF.Abs(sample) <= ceilingLinear + 1e-5f,
                    $"Sample {sample} at read #{read} exceeded ceiling {ceilingLinear}.");
            }
        }
    }

    [Fact]
    public void Read_WithNegativeSignalFarBelowNegativeCeiling_ClampsEverySample()
    {
        var ceilingLinear = MathF.Pow(10f, CeilingDb / 20f);
        const float amplitude = -10f * 0.891f;

        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var limiter = new Limiter(source, ceilingDb: CeilingDb);
        var output = limiter.ReadAll(sampleCount: 5000);

        Assert.All(output, sample => Assert.True(MathF.Abs(sample) <= ceilingLinear + 1e-5f));
    }

    [Fact]
    public void Read_WithSignalWellBelowCeiling_PassesThroughUnattenuatedAfterSettling()
    {
        const float amplitude = 0.1f;
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var limiter = new Limiter(source, ceilingDb: CeilingDb);

        limiter.ReadAll(sampleCount: 5000);
        var settled = limiter.ReadAll(sampleCount: 500);

        Assert.True(
            settled.Rms() > amplitude * 0.95f,
            $"Expected below-ceiling signal to pass through unchanged, got RMS={settled.Rms()} for input {amplitude}.");
    }

    [Fact]
    public void UpdateParameters_MidStream_TighterCeilingReducesOutputFurtherWithoutNaNAndNeverExceedsTheNewCeiling()
    {
        const float amplitude = 0.9f; // above both the old and new ceiling.
        var oldCeilingLinear = MathF.Pow(10f, CeilingDb / 20f);
        const float newCeilingDb = -12f;
        var newCeilingLinear = MathF.Pow(10f, newCeilingDb / 20f);

        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var limiter = new Limiter(source, ceilingDb: CeilingDb);

        var settledBefore = limiter.ReadAll(sampleCount: 5000);
        Assert.True(
            MathF.Abs(settledBefore[^1]) > newCeilingLinear,
            "Expected the pre-update settled output to sit above the tighter ceiling we're about to switch to.");

        limiter.UpdateParameters(ceilingDb: newCeilingDb, attackMs: 2f, releaseMs: 50f);

        // The hard clamp uses the ceiling field directly (not smoothed), so
        // it takes effect on literally the very next sample -- unlike the
        // envelope-driven gain, there's no transitional overshoot to allow for.
        var afterUpdate = limiter.ReadAll(sampleCount: 5000);
        Assert.All(afterUpdate, sample => Assert.True(float.IsFinite(sample), $"Sample {sample} was non-finite right after UpdateParameters."));
        Assert.All(afterUpdate, sample => Assert.True(
            MathF.Abs(sample) <= newCeilingLinear + 1e-5f,
            $"Sample {sample} exceeded the newly-updated ceiling ({newCeilingLinear})."));

        Assert.True(
            oldCeilingLinear > newCeilingLinear,
            "Sanity check: the new ceiling must actually be lower for this test to be meaningful.");
    }
}
