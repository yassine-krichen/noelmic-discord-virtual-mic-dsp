using NAudio.Wave;

namespace NoelMic.Tests.TestSupport;

/// <summary>
/// Emits the same fixed sample value forever. Used in place of a sine tone
/// for envelope-follower stages (gate/compressor/limiter) where a steady
/// magnitude gives a deterministic, ripple-free settled value to assert on.
/// </summary>
public sealed class ConstantSampleProvider : ISampleProvider
{
    private readonly float _value;

    public ConstantSampleProvider(int sampleRate, int channels, float value)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _value = value;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        buffer.Fill(_value);
        return buffer.Length;
    }
}
