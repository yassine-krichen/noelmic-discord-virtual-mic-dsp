using NoelMic.Dsp;

namespace NoelMic.Tests.Dsp;

public class BiquadCoefficientsTests
{
    [Theory]
    [InlineData(48000f, 90f)]
    [InlineData(48000f, 20f)]
    [InlineData(48000f, 300f)]
    [InlineData(44100f, 90f)]
    [InlineData(16000f, 90f)]
    public void HighPass_ProducesFiniteCoefficients_ForNormalCutoffRange(float sampleRate, float cutoffHz)
    {
        var coefficients = BiquadCoefficients.HighPass(sampleRate, cutoffHz);

        AssertAllFinite(coefficients);
    }

    [Theory]
    [InlineData(48000f, 0f)]
    [InlineData(48000f, -100f)]
    [InlineData(48000f, 5f)]
    [InlineData(48000f, 24000f)]
    [InlineData(48000f, 30000f)]
    [InlineData(48000f, 100000f)]
    [InlineData(44100f, 22050f)]
    public void HighPass_ClampsOutOfRangeCutoff_WithoutProducingNonFiniteCoefficients(
        float sampleRate,
        float cutoffHz)
    {
        var coefficients = BiquadCoefficients.HighPass(sampleRate, cutoffHz);

        AssertAllFinite(coefficients);
    }

    [Theory]
    [InlineData(48000f, 4000f, 3f, 1f)]
    [InlineData(48000f, 4000f, -12f, 1f)]
    [InlineData(48000f, 4000f, 12f, 0.5f)]
    [InlineData(48000f, 20f, 6f, 1f)]
    public void PeakingEq_ProducesFiniteCoefficients_ForNormalRange(
        float sampleRate,
        float centerHz,
        float gainDb,
        float q)
    {
        var coefficients = BiquadCoefficients.PeakingEq(sampleRate, centerHz, gainDb, q);

        AssertAllFinite(coefficients);
    }

    [Theory]
    [InlineData(48000f, 100000f, 3f)]
    [InlineData(48000f, 24000f, 3f)]
    [InlineData(48000f, 0f, 3f)]
    public void PeakingEq_ClampsOutOfRangeCenterFrequency_WithoutProducingNonFiniteCoefficients(
        float sampleRate,
        float centerHz,
        float gainDb)
    {
        var coefficients = BiquadCoefficients.PeakingEq(sampleRate, centerHz, gainDb);

        AssertAllFinite(coefficients);
    }

    private static void AssertAllFinite(BiquadCoefficients coefficients)
    {
        Assert.True(float.IsFinite(coefficients.B0), "B0 must be finite");
        Assert.True(float.IsFinite(coefficients.B1), "B1 must be finite");
        Assert.True(float.IsFinite(coefficients.B2), "B2 must be finite");
        Assert.True(float.IsFinite(coefficients.A1), "A1 must be finite");
        Assert.True(float.IsFinite(coefficients.A2), "A2 must be finite");
    }
}
