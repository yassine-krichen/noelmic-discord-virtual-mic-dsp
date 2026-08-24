using NAudio.Wave;

namespace NoelMic.Tests.TestSupport;

public static class SampleProviderExtensions
{
    /// <summary>
    /// Pulls exactly <paramref name="sampleCount"/> samples via repeated
    /// bounded Read() calls, mirroring how a real audio callback consumes an
    /// ISampleProvider in fixed-size chunks rather than one giant buffer.
    /// </summary>
    public static float[] ReadAll(this ISampleProvider provider, int sampleCount, int chunkSize = 2048)
    {
        var result = new float[sampleCount];
        var offset = 0;
        var chunk = new float[chunkSize];

        while (offset < sampleCount)
        {
            var toRead = Math.Min(chunkSize, sampleCount - offset);
            var span = chunk.AsSpan(0, toRead);
            var read = provider.Read(span);
            if (read == 0)
            {
                break;
            }

            span[..read].CopyTo(result.AsSpan(offset));
            offset += read;
        }

        return result;
    }

    public static float Rms(this ReadOnlySpan<float> samples)
    {
        double sumSquares = 0;
        foreach (var s in samples)
        {
            sumSquares += (double)s * s;
        }

        return (float)Math.Sqrt(sumSquares / samples.Length);
    }

    public static float Rms(this float[] samples) => Rms(samples.AsSpan());
}
