using NAudio.Wave;

namespace NoelMic.Dsp;

/// <summary>
/// Normalized biquad filter coefficients (a0 is pre-divided out).
/// </summary>
public readonly struct BiquadCoefficients
{
    public float B0 { get; init; }

    public float B1 { get; init; }

    public float B2 { get; init; }

    public float A1 { get; init; }

    public float A2 { get; init; }

    /// <summary>
    /// RBJ Audio EQ Cookbook high-pass filter.
    /// </summary>
    public static BiquadCoefficients HighPass(float sampleRate, float cutoffHz, float q = 0.707f)
    {
        // An unclamped cutoff at or beyond Nyquist produces a silently
        // unstable filter (garbage audio output, no exception thrown).
        cutoffHz = Math.Clamp(cutoffHz, 20f, sampleRate * 0.45f);

        var w0 = 2f * MathF.PI * cutoffHz / sampleRate;
        var cosW0 = MathF.Cos(w0);
        var sinW0 = MathF.Sin(w0);
        var alpha = sinW0 / (2f * q);

        var a0 = 1f + alpha;
        var b0 = (1f + cosW0) / 2f / a0;
        var b1 = -(1f + cosW0) / a0;
        var b2 = (1f + cosW0) / 2f / a0;
        var a1 = -2f * cosW0 / a0;
        var a2 = (1f - alpha) / a0;

        return new BiquadCoefficients { B0 = b0, B1 = b1, B2 = b2, A1 = a1, A2 = a2 };
    }

    /// <summary>
    /// RBJ Audio EQ Cookbook peaking EQ filter.
    /// </summary>
    public static BiquadCoefficients PeakingEq(float sampleRate, float centerHz, float gainDb, float q = 1.0f)
    {
        centerHz = Math.Clamp(centerHz, 20f, sampleRate * 0.45f);

        var a = MathF.Pow(10f, gainDb / 40f);
        var w0 = 2f * MathF.PI * centerHz / sampleRate;
        var cosW0 = MathF.Cos(w0);
        var sinW0 = MathF.Sin(w0);
        var alpha = sinW0 / (2f * q);

        var a0 = 1f + alpha / a;
        var b0 = (1f + alpha * a) / a0;
        var b1 = -2f * cosW0 / a0;
        var b2 = (1f - alpha * a) / a0;
        var a1 = -2f * cosW0 / a0;
        var a2 = (1f - alpha / a) / a0;

        return new BiquadCoefficients { B0 = b0, B1 = b1, B2 = b2, A1 = a1, A2 = a2 };
    }
}

/// <summary>
/// A single reusable direct-form-II-transposed biquad filter, used for both
/// the high-pass stage and the presence EQ band.
/// </summary>
public sealed class Biquad : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiquadCoefficients _coefficients;

    // Per-channel filter state, sized once at construction — this is the only
    // allocation for this stage, never per-Read.
    private readonly float[] _z1;
    private readonly float[] _z2;

    public Biquad(ISampleProvider source, BiquadCoefficients coefficients)
    {
        _source = source;
        _coefficients = coefficients;
        _z1 = new float[source.WaveFormat.Channels];
        _z2 = new float[source.WaveFormat.Channels];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);
        var channels = _z1.Length;
        var (b0, b1, b2, a1, a2) = (_coefficients.B0, _coefficients.B1, _coefficients.B2, _coefficients.A1, _coefficients.A2);

        for (var i = 0; i < samplesRead; i++)
        {
            var ch = i % channels;
            var x = buffer[i];

            var y = b0 * x + _z1[ch];
            _z1[ch] = b1 * x - a1 * y + _z2[ch];
            _z2[ch] = b2 * x - a2 * y;

            buffer[i] = y;
        }

        return samplesRead;
    }
}
