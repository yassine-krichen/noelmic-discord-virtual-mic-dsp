using NAudio.Wave;

namespace NoelMic.Dsp;

/// <summary>
/// Tanh soft-clip saturation stage — adds warmth/harmonic thickness ahead of
/// the limiter. Stateless (memoryless), unlike the envelope-follower stages.
/// </summary>
public sealed class Saturation : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _drive;
    private readonly float _normalizer;

    public Saturation(ISampleProvider source, float drive)
    {
        _source = source;

        // Floored to avoid a division by zero below; a drive this close to 0
        // is indistinguishable from off anyway.
        _drive = MathF.Max(drive, 1e-4f);

        // At drive=1.0 this normalizer is ~1.31, not exactly 1 — tanh(1) is
        // not 1, so this stage is never a true no-op at "unity" drive. Per
        // spec, not a bug.
        _normalizer = 1f / MathF.Tanh(_drive);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);

        for (var i = 0; i < samplesRead; i++)
        {
            buffer[i] = MathF.Tanh(buffer[i] * _drive) * _normalizer;
        }

        return samplesRead;
    }
}
