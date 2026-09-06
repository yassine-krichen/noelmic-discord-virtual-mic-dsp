using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class SaturationTests
{
    private const int SampleRate = 8000;

    [Fact]
    public void Read_WithUnityDriveAndSmallSignal_StaysWithinSameOrderOfMagnitudeAsInput()
    {
        const float value = 0.05f;
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var saturation = new Saturation(source, drive: 1.0f);

        var output = saturation.ReadAll(sampleCount: 512);

        // drive=1.0 is deliberately not an exact identity — tanh(1) is not 1,
        // so the normalizer applies a small fixed gain even at low levels.
        // "Near-unity" here means the same order of magnitude, not exact
        // passthrough.
        Assert.All(output, sample => Assert.True(
            sample is > 0f && sample < value * 2f,
            $"Expected drive=1.0 to keep a small signal ({value}) near-unity, got {sample}."));
    }

    [Fact]
    public void Read_WithHighDriveAndAmplitudeBeyondUnity_CompressesPeakBelowInputWhileStayingBounded()
    {
        const float value = 1.8f;
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var saturation = new Saturation(source, drive: 6f);

        var output = saturation.ReadAll(sampleCount: 512);

        Assert.All(output, sample =>
        {
            Assert.True(sample < value, $"Expected saturation to compress a peak of {value} below itself, got {sample}.");
            Assert.True(MathF.Abs(sample) <= 1f + 1e-3f, $"Expected saturated output to stay bounded near +-1, got {sample}.");
        });
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    [InlineData(2.5f)]
    [InlineData(-2.5f)]
    public void Read_WithInputAtOrBeyondUnity_NeverProducesNaNOrInfinity(float value)
    {
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var saturation = new Saturation(source, drive: 3f);

        var output = saturation.ReadAll(sampleCount: 512);

        Assert.All(output, sample => Assert.True(float.IsFinite(sample), $"Sample {sample} was non-finite for input {value}."));
    }

    [Fact]
    public void UpdateDrive_MidStream_IncreasesCompressionOfAPeakWithoutProducingNaNOrInfinity()
    {
        const float value = 1.8f;
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value);
        var saturation = new Saturation(source, drive: 1.0f);

        var beforeUpdate = saturation.ReadAll(sampleCount: 256);

        saturation.UpdateDrive(6f);
        var afterUpdate = saturation.ReadAll(sampleCount: 256);

        Assert.All(afterUpdate, sample => Assert.True(float.IsFinite(sample), $"Sample {sample} was non-finite after UpdateDrive."));
        Assert.True(
            afterUpdate[0] < beforeUpdate[^1],
            $"Expected raising drive to compress the {value} peak further, got before={beforeUpdate[^1]}, after={afterUpdate[0]}.");
    }
}
