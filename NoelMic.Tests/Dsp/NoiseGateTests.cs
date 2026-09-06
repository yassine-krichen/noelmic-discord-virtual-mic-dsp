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
    public void IsOpen_WithSignalAboveThreshold_ReportsTrueAfterSettling()
    {
        const float amplitude = 0.5f; // well above -40dB threshold
        var source = new ConstantSampleProvider(SampleRate, channels: 1, amplitude);
        var gate = new NoiseGate(source, ThresholdDb);

        gate.ReadAll(sampleCount: 40000);

        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void IsOpen_AfterSustainedSilenceLongerThanHoldTime_ReportsFalse()
    {
        const float amplitude = 0.0001f; // well below -40dB threshold
        var source = new ConstantSampleProvider(SampleRate, channels: 1, amplitude);
        var gate = new NoiseGate(source, ThresholdDb, holdMs: 50f);

        // Enough samples for the hold time (50ms = 400 samples at 8kHz) to
        // expire and then some, well beyond that.
        gate.ReadAll(sampleCount: 20000);

        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void CurrentReductionDb_IsAtOrBelowZeroAndFiniteRegardlessOfGateState()
    {
        var loudGate = new NoiseGate(new ConstantSampleProvider(SampleRate, channels: 1, value: 0.5f), ThresholdDb);
        var quietGate = new NoiseGate(new ConstantSampleProvider(SampleRate, channels: 1, value: 0.0001f), ThresholdDb);

        loudGate.ReadAll(sampleCount: 40000);
        quietGate.ReadAll(sampleCount: 40000);

        Assert.True(float.IsFinite(loudGate.CurrentReductionDb));
        Assert.True(float.IsFinite(quietGate.CurrentReductionDb));
        Assert.True(loudGate.CurrentReductionDb <= 0f);
        Assert.True(quietGate.CurrentReductionDb <= 0f);
    }

    [Fact]
    public void CurrentReductionDb_WhenGateIsClosed_IsMoreNegativeThanWhenOpen()
    {
        var loudGate = new NoiseGate(new ConstantSampleProvider(SampleRate, channels: 1, value: 0.5f), ThresholdDb);
        var quietGate = new NoiseGate(new ConstantSampleProvider(SampleRate, channels: 1, value: 0.0001f), ThresholdDb);

        loudGate.ReadAll(sampleCount: 40000);
        quietGate.ReadAll(sampleCount: 40000);

        Assert.True(
            quietGate.CurrentReductionDb < loudGate.CurrentReductionDb,
            $"Expected a closed gate's reduction ({quietGate.CurrentReductionDb}dB) to be more negative " +
            $"(more attenuation) than an open gate's ({loudGate.CurrentReductionDb}dB).");
    }

    [Fact]
    public void UpdateParameters_MidStream_RaisingThresholdAboveTheSignalClosesAnAlreadyOpenGateWithoutClickingOrNaN()
    {
        const float amplitude = 0.1f; // ~-20dBFS
        var source = new ConstantSampleProvider(SampleRate, channels: 1, amplitude);
        var gate = new NoiseGate(source, thresholdDb: -40); // -20dBFS is well above -40dB, so it settles open.

        gate.ReadAll(sampleCount: 40000);
        Assert.True(gate.IsOpen, "Expected the gate to settle open before the parameter update.");

        gate.UpdateParameters(thresholdDb: -10, attackMs: 5f, releaseMs: 150f, hysteresisDb: 6.0, holdMs: 0f);

        var buffer = new float[64];
        gate.Read(buffer);
        Assert.All(buffer, sample => Assert.True(float.IsFinite(sample), $"Sample {sample} was non-finite right after UpdateParameters."));

        // Envelope/gain state is preserved across UpdateParameters (see the
        // comment on SetCoefficients), so the gate fades toward closed via
        // its normal release curve rather than snapping -- no sample right
        // after the update should spike past the input amplitude.
        Assert.All(buffer, sample => Assert.True(
            MathF.Abs(sample) <= amplitude * 1.05f,
            $"Sample {sample} exceeded the input amplitude ({amplitude}) right after UpdateParameters."));

        gate.ReadAll(sampleCount: 40000);
        Assert.False(gate.IsOpen, "Expected the new, much higher threshold to take effect and close the gate.");
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
