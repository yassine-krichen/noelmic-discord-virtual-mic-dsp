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

    /// <summary>
    /// RBJ Audio EQ Cookbook low shelf filter.
    /// </summary>
    public static BiquadCoefficients LowShelf(float sampleRate, float f0, float dBgain, float shelfSlope = 1f)
    {
        f0 = Math.Clamp(f0, 20f, sampleRate * 0.45f);

        // Shelf gain uses A = 10^(dBgain/40), NOT /20 like PeakingEq — the RBJ
        // cookbook defines shelf "A" as the square root of the peaking "A",
        // hand-simplifying this to /20 silently halves every shelf's dB gain.
        var a = MathF.Pow(10f, dBgain / 40f);
        var w0 = 2f * MathF.PI * f0 / sampleRate;
        var cosW0 = MathF.Cos(w0);
        var sinW0 = MathF.Sin(w0);
        var alpha = sinW0 / 2f * MathF.Sqrt((a + 1f / a) * (1f / shelfSlope - 1f) + 2f);
        var sqrtAAlpha2 = 2f * MathF.Sqrt(a) * alpha;

        var a0 = (a + 1f) + (a - 1f) * cosW0 + sqrtAAlpha2;
        var b0 = a * ((a + 1f) - (a - 1f) * cosW0 + sqrtAAlpha2) / a0;
        var b1 = 2f * a * ((a - 1f) - (a + 1f) * cosW0) / a0;
        var b2 = a * ((a + 1f) - (a - 1f) * cosW0 - sqrtAAlpha2) / a0;
        var a1 = -2f * ((a - 1f) + (a + 1f) * cosW0) / a0;
        var a2 = ((a + 1f) + (a - 1f) * cosW0 - sqrtAAlpha2) / a0;

        return new BiquadCoefficients { B0 = b0, B1 = b1, B2 = b2, A1 = a1, A2 = a2 };
    }

    /// <summary>
    /// RBJ Audio EQ Cookbook high shelf filter.
    /// </summary>
    public static BiquadCoefficients HighShelf(float sampleRate, float f0, float dBgain, float shelfSlope = 1f)
    {
        f0 = Math.Clamp(f0, 20f, sampleRate * 0.45f);

        // Same /40 (not /20) shelf-gain convention as LowShelf — see the
        // comment there.
        var a = MathF.Pow(10f, dBgain / 40f);
        var w0 = 2f * MathF.PI * f0 / sampleRate;
        var cosW0 = MathF.Cos(w0);
        var sinW0 = MathF.Sin(w0);
        var alpha = sinW0 / 2f * MathF.Sqrt((a + 1f / a) * (1f / shelfSlope - 1f) + 2f);
        var sqrtAAlpha2 = 2f * MathF.Sqrt(a) * alpha;

        var a0 = (a + 1f) - (a - 1f) * cosW0 + sqrtAAlpha2;
        var b0 = a * ((a + 1f) + (a - 1f) * cosW0 + sqrtAAlpha2) / a0;
        var b1 = -2f * a * ((a - 1f) + (a + 1f) * cosW0) / a0;
        var b2 = a * ((a + 1f) + (a - 1f) * cosW0 - sqrtAAlpha2) / a0;
        var a1 = 2f * ((a - 1f) - (a + 1f) * cosW0) / a0;
        var a2 = ((a + 1f) - (a - 1f) * cosW0 - sqrtAAlpha2) / a0;

        return new BiquadCoefficients { B0 = b0, B1 = b1, B2 = b2, A1 = a1, A2 = a2 };
    }

    /// <summary>
    /// One sample of direct-form-II-transposed biquad processing. Shared by
    /// <see cref="Biquad"/>, the de-esser's sidechain detection HPF, and its
    /// reduction shelf — the same per-sample math, three call sites.
    /// </summary>
    public static float Apply(in BiquadCoefficients c, float x, ref float z1, ref float z2)
    {
        var y = c.B0 * x + z1;
        z1 = c.B1 * x - c.A1 * y + z2;
        z2 = c.B2 * x - c.A2 * y;
        return y;
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

        for (var i = 0; i < samplesRead; i++)
        {
            var ch = i % channels;
            buffer[i] = BiquadCoefficients.Apply(in _coefficients, buffer[i], ref _z1[ch], ref _z2[ch]);
        }

        return samplesRead;
    }
}
