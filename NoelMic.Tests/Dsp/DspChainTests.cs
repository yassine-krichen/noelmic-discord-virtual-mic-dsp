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
}
