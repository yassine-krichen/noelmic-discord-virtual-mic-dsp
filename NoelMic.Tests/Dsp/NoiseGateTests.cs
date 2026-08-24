using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class NoiseGateTests
{
    private const int SampleRate = 8000;
    private const double ThresholdDb = -40;

    [Fact]
    public void Read_WithPersistentSilence_NeverProducesNaNOrInfinity()
    {
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: 0f);
        var gate = new NoiseGate(source, ThresholdDb);
        var buffer = new float[2048];

        // 200 reads over silence is enough real-world session time (~50s at
        // this sample rate) for the unfloored -Infinity/NaN bug to surface
        // and permanently poison every subsequent sample.
        for (var read = 0; read < 200; read++)
        {
            gate.Read(buffer);

            foreach (var sample in buffer)
            {
                Assert.True(float.IsFinite(sample), $"Sample at read #{read} was non-finite: {sample}");
            }
        }
    }

    [Fact]
    public void Read_WithSignalAboveThreshold_PassesThroughNearUnattenuatedAfterSettling()
    {
        const float amplitude = 0.5f; // well above -40dB threshold
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var gate = new NoiseGate(source, ThresholdDb);

        gate.ReadAll(sampleCount: 40000); // let the (slow, release-curve) gain-open settle
        var settled = gate.ReadAll(sampleCount: 1000);

        Assert.True(
            settled.Rms() > amplitude * 0.9f,
            $"Expected settled output RMS to approach input amplitude {amplitude}, got {settled.Rms()}.");
    }

    [Fact]
    public void Read_WithSignalBelowThreshold_AttenuatesTowardSilence()
    {
        const float amplitude = 0.0001f; // well below -40dB threshold
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: amplitude);
        var gate = new NoiseGate(source, ThresholdDb);

        gate.ReadAll(sampleCount: 2000); // gate-close settles fast (attack curve)
        var settled = gate.ReadAll(sampleCount: 1000);

        Assert.True(
            settled.Rms() < amplitude * 0.1f,
            $"Expected settled output RMS to be attenuated well below input amplitude {amplitude}, got {settled.Rms()}.");
    }

    [Fact]
    public void Constructor_SizesPerChannelStateToSourceChannelCount()
    {
        var source = new ConstantSampleProvider(SampleRate, channels: 2, value: 0.5f);
        var gate = new NoiseGate(source, ThresholdDb);

        Assert.Equal(2, gate.WaveFormat.Channels);
        var output = gate.ReadAll(sampleCount: 4);
        Assert.All(output, s => Assert.True(float.IsFinite(s)));
    }
}
