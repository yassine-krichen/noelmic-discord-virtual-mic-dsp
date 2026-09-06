using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class DeEsserTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void Read_WithLowFrequencyContentBelowSidechainBand_PassesThroughEssentiallyUnchanged()
    {
        const float amplitude = 0.5f;
        var inputRms = amplitude / MathF.Sqrt(2f);

        var source = new SineWaveSampleProvider(SampleRate, channels: 1, frequencyHz: 500f, amplitude);
        var deEsser = new DeEsser(source, deEsserHz: 6500f, thresholdDb: -30, ratio: 4.0, maxReductionDb: 8f);

        deEsser.ReadAll(sampleCount: 20000);
        var settled = deEsser.ReadAll(sampleCount: 5000);

        Assert.True(
            settled.Rms() > inputRms * 0.9f,
            $"Expected a 500Hz tone (well below the ~6kHz sidechain band) to pass through " +
            $"essentially unchanged (RMS~{inputRms}), got {settled.Rms()}.");
    }

    [Fact]
    public void Read_WithStrongSibilantContentAboveThreshold_MeasurablyReducesHighBand()
    {
        const float amplitude = 0.5f;
        var inputRms = amplitude / MathF.Sqrt(2f);

        var source = new SineWaveSampleProvider(SampleRate, channels: 1, frequencyHz: 9000f, amplitude);
        var deEsser = new DeEsser(source, deEsserHz: 6500f, thresholdDb: -30, ratio: 4.0, maxReductionDb: 10f);

        deEsser.ReadAll(sampleCount: 20000);
        var settled = deEsser.ReadAll(sampleCount: 5000);

        Assert.True(
            settled.Rms() < inputRms * 0.85f,
            $"Expected a loud 9kHz tone above threshold to be measurably reduced below its " +
            $"unprocessed RMS ({inputRms}), got {settled.Rms()}.");
    }

    [Fact]
    public void Read_WithExtremeSibilantSignal_NeverAttenuatesBeyondMaxReductionDb()
    {
        const float amplitude = 0.9f;
        const float maxReductionDb = 8f;
        var inputRms = amplitude / MathF.Sqrt(2f);

        // Tone frequency is far above the shelf corner (deEsserHz) so the
        // reduction shelf sits on its asymptotic plateau, and threshold/ratio
        // are chosen so the *uncapped* reduction formula would call for far
        // more than maxReductionDb — this isolates the Math.Clamp bound.
        var source = new SineWaveSampleProvider(SampleRate, channels: 1, frequencyHz: 15000f, amplitude);
        var deEsser = new DeEsser(source, deEsserHz: 1000f, thresholdDb: -60, ratio: 20.0, maxReductionDb);

        deEsser.ReadAll(sampleCount: 30000);
        var settled = deEsser.ReadAll(sampleCount: 5000);

        var attenuationDb = 20f * MathF.Log10(inputRms / settled.Rms());

        Assert.True(
            attenuationDb <= maxReductionDb + 2f,
            $"Expected attenuation to stay within maxReductionDb ({maxReductionDb}dB, +2dB margin " +
            $"for shelf shape), got {attenuationDb}dB.");
    }

    [Fact]
    public void Read_WithPersistentSilence_NeverProducesNaNOrInfinity()
    {
        var source = new ConstantSampleProvider(SampleRate, channels: 1, value: 0f);
        var deEsser = new DeEsser(source, deEsserHz: 6500f, thresholdDb: -30, ratio: 4.0, maxReductionDb: 8f);
        var buffer = new float[2048];

        for (var read = 0; read < 200; read++)
        {
            deEsser.Read(buffer);

            foreach (var sample in buffer)
            {
                Assert.True(float.IsFinite(sample), $"Sample at read #{read} was non-finite: {sample}");
            }
        }
    }

    [Fact]
    public void Read_WithSustainedLoudSibilantInput_NeverProducesNaNOrInfinity()
    {
        var source = new SineWaveSampleProvider(SampleRate, channels: 1, frequencyHz: 9000f, amplitude: 0.99f);
        var deEsser = new DeEsser(source, deEsserHz: 6500f, thresholdDb: -30, ratio: 4.0, maxReductionDb: 8f);
        var buffer = new float[2048];

        for (var read = 0; read < 200; read++)
        {
            deEsser.Read(buffer);

            foreach (var sample in buffer)
            {
                Assert.True(float.IsFinite(sample), $"Sample at read #{read} was non-finite: {sample}");
            }
        }
    }

    [Fact]
    public void CurrentReductionDb_IsNonNegativeAndFiniteRegardlessOfSibilanceLevel()
    {
        var source = new SineWaveSampleProvider(SampleRate, channels: 1, frequencyHz: 9000f, amplitude: 0.5f);
        var deEsser = new DeEsser(source, deEsserHz: 6500f, thresholdDb: -30, ratio: 4.0, maxReductionDb: 8f);

        deEsser.ReadAll(sampleCount: 20000);

        Assert.True(float.IsFinite(deEsser.CurrentReductionDb));
        Assert.True(deEsser.CurrentReductionDb >= 0f, $"Expected a non-negative reduction amount, got {deEsser.CurrentReductionDb}dB.");
    }

    [Fact]
    public void UpdateParameters_MidStream_LoweringThresholdIncreasesReductionWithoutClickingOrNaN()
    {
        const float amplitude = 0.5f;
        var inputRms = amplitude / MathF.Sqrt(2f);
        var source = new SineWaveSampleProvider(SampleRate, channels: 1, frequencyHz: 9000f, amplitude);
        var deEsser = new DeEsser(source, deEsserHz: 6500f, thresholdDb: -20, ratio: 2.0, maxReductionDb: 10f);

        deEsser.ReadAll(sampleCount: 20000);
        var reductionBefore = deEsser.CurrentReductionDb;

        deEsser.UpdateParameters(deEsserHz: 6500f, thresholdDb: -50, ratio: 8.0, maxReductionDb: 10f);

        var buffer = new float[64];
        deEsser.Read(buffer);
        Assert.All(buffer, sample => Assert.True(float.IsFinite(sample), $"Sample {sample} was non-finite right after UpdateParameters."));

        // Sidechain envelope and shelf filter memory are preserved across
        // UpdateParameters, so nothing right after the update should spike
        // past a modest multiple of the input amplitude.
        Assert.All(buffer, sample => Assert.True(
            MathF.Abs(sample) < amplitude * 3f,
            $"Sample {sample} spiked well beyond the input amplitude ({amplitude}) right after UpdateParameters."));

        deEsser.ReadAll(sampleCount: 20000);
        var reductionAfter = deEsser.CurrentReductionDb;
        var settledOutputRms = deEsser.ReadAll(sampleCount: 5000).Rms();

        Assert.True(
            reductionAfter > reductionBefore,
            $"Expected a much lower threshold and higher ratio to increase the reduction amount, " +
            $"got before={reductionBefore}dB, after={reductionAfter}dB.");
        Assert.True(
            settledOutputRms < inputRms * 0.85f,
            $"Expected the new parameters to measurably reduce output below the unprocessed RMS ({inputRms}), got {settledOutputRms}.");
    }
}
