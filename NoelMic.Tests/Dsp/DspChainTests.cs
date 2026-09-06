using NoelMic.Config;
using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class DspChainTests
{
    [Fact]
    public void Constructor_WithDefaultConfig_DoesNotThrow()
    {
        var source = new SineWaveSampleProvider(48000, channels: 2, frequencyHz: 440f);

        var exception = Record.Exception(() => new DspChain(source, new AppConfig()));

        Assert.Null(exception);
    }

    [Fact]
    public void WaveFormat_MatchesSourceWaveFormat()
    {
        var source = new SineWaveSampleProvider(44100, channels: 2, frequencyHz: 440f);
        var chain = new DspChain(source, new AppConfig());

        Assert.Equal(source.WaveFormat.SampleRate, chain.WaveFormat.SampleRate);
        Assert.Equal(source.WaveFormat.Channels, chain.WaveFormat.Channels);
    }

    [Fact]
    public void Read_WithDefaultConfig_ReturnsRequestedSampleCountWithoutThrowing()
    {
        var source = new SineWaveSampleProvider(48000, channels: 2, frequencyHz: 440f);
        var chain = new DspChain(source, new AppConfig());
        var buffer = new float[2048];

        var samplesRead = chain.Read(buffer);

        Assert.Equal(buffer.Length, samplesRead);
        Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
    }

    [Fact]
    public void Read_AcrossManyCalls_NeverProducesNonFiniteOutput()
    {
        var source = new SineWaveSampleProvider(48000, channels: 1, frequencyHz: 220f);
        var chain = new DspChain(source, new AppConfig());
        var buffer = new float[1024];

        for (var i = 0; i < 50; i++)
        {
            chain.Read(buffer);
            Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
        }
    }

    [Fact]
    public void UpdateParameters_MidStream_WithAHigherInputGain_MeasurablyRaisesSubsequentOutput()
    {
        var source = new SineWaveSampleProvider(48000, channels: 1, frequencyHz: 1000f, amplitude: 0.01f);
        var chain = new DspChain(source, new AppConfig { Preset = "Raw", InputGainDb = 0f });

        chain.ReadAll(sampleCount: 20000); // settle every stage at the initial gain.
        var beforeUpdate = chain.ReadAll(sampleCount: 4000).Rms();

        chain.UpdateParameters(new AppConfig { Preset = "Raw", InputGainDb = 20f });

        chain.ReadAll(sampleCount: 20000); // settle InputGain's smoothing + downstream envelopes at the new gain.
        var afterUpdate = chain.ReadAll(sampleCount: 4000).Rms();

        Assert.True(
            afterUpdate > beforeUpdate * 3f,
            $"Expected UpdateParameters with a +20dB higher InputGainDb to measurably raise output, " +
            $"got before={beforeUpdate}, after={afterUpdate} -- suggests the ReferenceEquals gate or " +
            "ApplyParameters isn't actually wiring the new parameters through.");
    }

    [Fact]
    public void UpdateParameters_WithAStructurallyEquivalentConfig_LeavesSubsequentOutputEffectivelyUnchanged()
    {
        var source = new SineWaveSampleProvider(48000, channels: 1, frequencyHz: 500f, amplitude: 0.05f);
        var chain = new DspChain(source, new AppConfig { Preset = "Broadcast" });

        chain.ReadAll(sampleCount: 20000);
        var beforeUpdate = chain.ReadAll(sampleCount: 4000).Rms();

        // A brand-new AppConfig/resolved DspParameters instance, but with
        // identical field values -- exercises ApplyParameters recomputing
        // every stage's coefficients from scratch without corrupting state.
        chain.UpdateParameters(new AppConfig { Preset = "Broadcast" });

        var afterUpdate = chain.ReadAll(sampleCount: 4000);

        Assert.All(afterUpdate, sample => Assert.True(float.IsFinite(sample)));
        Assert.True(
            MathF.Abs(afterUpdate.Rms() - beforeUpdate) < beforeUpdate * 0.05f,
            $"Expected re-applying an equivalent config to leave output effectively unchanged, " +
            $"got before={beforeUpdate}, after={afterUpdate.Rms()}.");
    }

    [Fact]
    public void Read_AcrossConsecutiveBuffersWithNoInterveningUpdate_ProducesStableSettledOutput()
    {
        var source = new SineWaveSampleProvider(48000, channels: 1, frequencyHz: 500f, amplitude: 0.1f);
        var chain = new DspChain(source, new AppConfig { Preset = "Broadcast" });

        chain.ReadAll(sampleCount: 40000); // let every stage's envelope settle.

        var firstBufferRms = chain.ReadAll(sampleCount: 4000).Rms();
        var secondBufferRms = chain.ReadAll(sampleCount: 4000).Rms();

        Assert.True(
            MathF.Abs(firstBufferRms - secondBufferRms) < firstBufferRms * 0.02f,
            $"Expected settled output to stay stable across consecutive buffers with no parameter update " +
            $"in between, got first={firstBufferRms}, second={secondBufferRms}.");
    }

    [Fact]
    public void GetMeteringSnapshot_WithANormalSignal_ReturnsFiniteValuesForEveryField()
    {
        var source = new SineWaveSampleProvider(48000, channels: 1, frequencyHz: 500f, amplitude: 0.3f);
        var chain = new DspChain(source, new AppConfig { Preset = "Broadcast" });

        chain.ReadAll(sampleCount: 20000);
        var snapshot = chain.GetMeteringSnapshot();

        Assert.True(float.IsFinite(snapshot.InputLevelDb), "InputLevelDb was not finite.");
        Assert.True(float.IsFinite(snapshot.OutputLevelDb), "OutputLevelDb was not finite.");
        Assert.True(float.IsFinite(snapshot.GateReductionDb), "GateReductionDb was not finite.");
        Assert.True(float.IsFinite(snapshot.CompressorReductionDb), "CompressorReductionDb was not finite.");
        Assert.True(float.IsFinite(snapshot.DeEsserReductionDb), "DeEsserReductionDb was not finite.");
    }
}
