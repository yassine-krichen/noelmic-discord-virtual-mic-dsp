using NAudio.Wave;

namespace NoelMic.Tests.TestSupport;

/// <summary>
/// Emits <paramref name="firstValue"/>-equivalent constant samples for the
/// first <c>firstValueSampleCount</c> samples requested, then switches to
/// <paramref name="secondValue"/> forever after. Used to simulate a signal
/// that starts at one level and changes partway through a fixed-duration
/// measurement window (e.g. silence-then-speech for calibration), without
/// needing a real audio source or a second provider wired in mid-test.
/// </summary>
public sealed class SwitchingConstantSampleProvider : ISampleProvider
{
    private readonly float _firstValue;
    private readonly float _secondValue;
    private readonly long _firstValueSampleCount;
    private long _samplesEmitted;

    public SwitchingConstantSampleProvider(
        int sampleRate, int channels, float firstValue, long firstValueSampleCount, float secondValue)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _firstValue = firstValue;
        _firstValueSampleCount = firstValueSampleCount;
        _secondValue = secondValue;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = _samplesEmitted < _firstValueSampleCount ? _firstValue : _secondValue;
            _samplesEmitted++;
        }

        return buffer.Length;
    }
}
