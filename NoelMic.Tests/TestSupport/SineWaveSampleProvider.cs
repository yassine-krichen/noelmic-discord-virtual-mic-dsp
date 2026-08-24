using NAudio.Wave;

namespace NoelMic.Tests.TestSupport;

/// <summary>
/// Generates an infinite sine tone on demand, identical across channels, so
/// DSP stages under test can be fed a known, arbitrarily long signal without
/// pre-allocating large sample arrays.
/// </summary>
public sealed class SineWaveSampleProvider : ISampleProvider
{
    private readonly float _frequencyHz;
    private readonly float _amplitude;
    private double _phase;

    public SineWaveSampleProvider(int sampleRate, int channels, float frequencyHz, float amplitude = 1f)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _frequencyHz = frequencyHz;
        _amplitude = amplitude;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        var channels = WaveFormat.Channels;
        var frames = buffer.Length / channels;
        var phaseStep = 2 * Math.PI * _frequencyHz / WaveFormat.SampleRate;

        for (var frame = 0; frame < frames; frame++)
        {
            var sample = _amplitude * (float)Math.Sin(_phase);
            for (var ch = 0; ch < channels; ch++)
            {
                buffer[(frame * channels) + ch] = sample;
            }

            _phase += phaseStep;
            if (_phase > 2 * Math.PI)
            {
                _phase -= 2 * Math.PI;
            }
        }

        return frames * channels;
    }
}
