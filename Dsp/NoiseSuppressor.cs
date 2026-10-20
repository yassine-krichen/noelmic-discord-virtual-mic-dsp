using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RNNoise.NET;

namespace NoelMic.Dsp;

/// <summary>
/// RNNoise-based noise suppression. RNNoise only operates at a fixed 48kHz /
/// 480-sample-frame native rate, but the chain's actual sample rate is
/// whatever the render device (VB-CABLE) is configured to — 44100 or 48000
/// in practice, see DspChainTests. When the chain isn't already 48kHz, this
/// stage resamples around itself (up before denoising, down after) rather
/// than bypassing suppression, so it never silently goes inert on a non-48k
/// setup.
/// </summary>
public sealed class NoiseSuppressor : ISampleProvider, IDisposable
{
    private const int NativeSampleRate = 48000;

    private readonly ISampleProvider _source;
    private readonly ISampleProvider _pipeline;

    // One Denoiser per channel, constructed unconditionally regardless of
    // the enabled toggle — RNNoise is deterministic with no cross-channel
    // state, so duplicated-mono channels stay identical and genuinely
    // stereo input is handled correctly either way. Constructed once here
    // (never on toggle) because the Settings dialog re-applies parameters
    // live on every preset selection change; tearing native state down and
    // rebuilding it on every one of those would be wasteful and racy.
    private readonly Denoiser[] _denoisers;

    private readonly NativeRateStage? _core;

    // One-time process-wide probe for whether the native rnnoise library
    // actually loaded. A missing/quarantined DLL must degrade this stage to
    // silent passthrough, never crash the app.
    public static bool IsNativeAvailable { get; } = ProbeNativeAvailability();

    private static bool ProbeNativeAvailability()
    {
        try
        {
            using var probe = new Denoiser();
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException
            or EntryPointNotFoundException or TypeInitializationException)
        {
            return false;
        }
    }

    public NoiseSuppressor(ISampleProvider source, bool enabled)
    {
        _source = source;

        if (!IsNativeAvailable)
        {
            // Pure passthrough — never touches native code, per
            // IsNativeAvailable's contract above.
            _denoisers = [];
            _pipeline = _source;
            return;
        }

        var channels = source.WaveFormat.Channels;
        _denoisers = new Denoiser[channels];
        for (var c = 0; c < channels; c++)
        {
            _denoisers[c] = new Denoiser();
        }

        var sourceRate = source.WaveFormat.SampleRate;

        // WdlResamplingSampleProvider already resamples an arbitrary number
        // of interleaved channels together in one pass (see its Read()),
        // so one resampler per direction covers every channel — no need for
        // one pair per channel.
        ISampleProvider upsampled = sourceRate == NativeSampleRate
            ? source
            : new WdlResamplingSampleProvider(source, NativeSampleRate);

        _core = new NativeRateStage(upsampled, channels, _denoisers);
        _core.UpdateEnabled(enabled);

        _pipeline = sourceRate == NativeSampleRate
            ? _core
            : new WdlResamplingSampleProvider(_core, sourceRate);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void UpdateEnabled(bool enabled) => _core?.UpdateEnabled(enabled);

    public int Read(Span<float> buffer) => _pipeline.Read(buffer);

    public void Dispose()
    {
        foreach (var denoiser in _denoisers)
        {
            denoiser.Dispose();
        }
    }

    /// <summary>
    /// Runs RNNoise at its fixed native 48kHz/480-sample-frame rate. Its
    /// source is guaranteed already-48kHz by the constructor above, so this
    /// class never deals with resampling — only with bridging RNNoise's
    /// fixed frame size against whatever arbitrary buffer size Read() is
    /// asked for.
    /// </summary>
    private sealed class NativeRateStage : ISampleProvider
    {
        // RNNoise's fixed native frame size — 480 samples == 10ms @ 48kHz.
        private const int NativeFrameSize = 480;

        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly Denoiser[] _denoisers;
        private readonly WaveFormat _waveFormat;

        private bool _enabled;

        // Interleaved accumulation buffer for exactly one native frame
        // across all channels — filled from _source before every denoise
        // pass, since RNNoise only ever runs on whole 480-sample frames.
        private readonly float[] _frameAccum;
        private int _frameAccumCount;

        // Per-channel scratch for de/re-interleaving one native frame —
        // Denoiser.Denoise only operates on a contiguous single-channel
        // buffer, but the rest of the chain (and this stage's own input and
        // output) is interleaved.
        private readonly float[][] _monoScratch;

        // A single interleaved output ring buffer, not one FIFO per
        // channel: every push and pop here always moves a whole native
        // frame with every channel advancing in lockstep, so per-channel
        // FIFOs would only duplicate the same head/tail bookkeeping N times
        // for no benefit. Sized to 1 second @ 48kHz — far larger than any
        // realistic single Read() request (this app's WASAPI buffers run at
        // ~100ms latency) — so it never needs to grow past construction.
        private readonly float[] _outputFifo;
        private int _fifoHead;
        private int _fifoCount;

        public NativeRateStage(ISampleProvider source, int channels, Denoiser[] denoisers)
        {
            _source = source;
            _channels = channels;
            _denoisers = denoisers;
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(NativeSampleRate, channels);

            _frameAccum = new float[NativeFrameSize * channels];
            _monoScratch = new float[channels][];
            for (var c = 0; c < channels; c++)
            {
                _monoScratch[c] = new float[NativeFrameSize];
            }

            _outputFifo = new float[NativeSampleRate * channels];

            // Pre-fill with one native frame (10ms) of silence — the array
            // above is already zeroed by allocation — so the very first
            // Read() has something to drain while the first real frame
            // accumulates, instead of returning short during warm-up.
            _fifoCount = NativeFrameSize * channels;
        }

        public WaveFormat WaveFormat => _waveFormat;

        public void UpdateEnabled(bool enabled) => _enabled = enabled;

        public int Read(Span<float> buffer)
        {
            var requested = buffer.Length;

            while (_fifoCount < requested)
            {
                var need = _frameAccum.Length - _frameAccumCount;
                var read = _source.Read(_frameAccum.AsSpan(_frameAccumCount, need));
                if (read == 0)
                {
                    // Source starved — should not happen for a live capture
                    // stream, but stop topping up rather than spin; the
                    // drain below pads any shortfall with silence instead
                    // of returning short.
                    break;
                }

                _frameAccumCount += read;
                if (_frameAccumCount < _frameAccum.Length)
                {
                    continue;
                }

                ProcessAccumulatedFrame();
                _frameAccumCount = 0;
            }

            var available = Math.Min(_fifoCount, requested);
            DequeueFifo(buffer[..available]);

            if (available < requested)
            {
                buffer[available..].Clear();
            }

            return requested;
        }

        private void ProcessAccumulatedFrame()
        {
            for (var c = 0; c < _channels; c++)
            {
                var mono = _monoScratch[c];
                for (var i = 0; i < NativeFrameSize; i++)
                {
                    mono[i] = _frameAccum[(i * _channels) + c];
                }

                if (_enabled)
                {
                    _denoisers[c].Denoise(mono);
                }
            }

            var writeIndex = (_fifoHead + _fifoCount) % _outputFifo.Length;
            for (var i = 0; i < NativeFrameSize; i++)
            {
                for (var c = 0; c < _channels; c++)
                {
                    _outputFifo[writeIndex] = _monoScratch[c][i];
                    writeIndex = (writeIndex + 1) % _outputFifo.Length;
                }
            }

            _fifoCount += NativeFrameSize * _channels;
        }

        private void DequeueFifo(Span<float> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] = _outputFifo[_fifoHead];
                _fifoHead = (_fifoHead + 1) % _outputFifo.Length;
            }

            _fifoCount -= destination.Length;
        }
    }
}
