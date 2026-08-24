using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NoelMic.Config;
using NoelMic.Dsp;

namespace NoelMic.Core;

/// <summary>
/// Captures audio from a physical input device and renders it straight through
/// to a render device (VB-CABLE's input endpoint) with no processing.
/// </summary>
public sealed class AudioEngine
{
    private readonly MMDevice _captureDevice;
    private readonly MMDevice _renderDevice;
    private readonly MuteController _muteController;
    private readonly AppConfig _config;

    private WasapiCapture? _capture;
    private WasapiOut? _output;

    // Plain bools aren't safe here: RecordingStopped/PlaybackStopped fire on
    // NAudio's own capture/render callback threads, and a near-simultaneous
    // dual failure could let both threads read "not yet stopped/reported"
    // before either writes it, racing on disposing the same WASAPI/COM
    // objects. CompareExchange makes the check-and-set atomic.
    private int _stoppedFlag;
    private int _failureReportedFlag;

    // Raised when the capture or render stream dies out from under us (device
    // unplugged, driver reset, etc.) — null on a clean, self-initiated Stop().
    public event EventHandler<Exception?>? Failed;

    public AudioEngine(MMDevice captureDevice, MMDevice renderDevice, MuteController muteController, AppConfig config)
    {
        _captureDevice = captureDevice;
        _renderDevice = renderDevice;
        _muteController = muteController;
        _config = config;
    }

    public void Start()
    {
        _capture = new WasapiCapture(_captureDevice);

        var buffer = new BufferedWaveProvider(_capture.WaveFormat, TimeSpan.FromMilliseconds(250))
        {
            DiscardOnBufferOverflow = true,
        };
        _capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        var renderFormat = _renderDevice.CreateAudioClient().MixFormat;
        var outputProvider = BuildOutputProvider(buffer, renderFormat);

        _output = new WasapiOut(_renderDevice, AudioClientShareMode.Shared, true, 100);
        _output.Init(outputProvider);

        // Subscribed before starting either stream so a failure during
        // startup itself is still caught, not just failures after Play().
        _capture.RecordingStopped += HandleStreamStopped;
        _output.PlaybackStopped += HandleStreamStopped;

        _capture.StartRecording();
        _output.Play();
    }

    // Fires on both a clean stop (Exception == null, e.g. our own Stop() call)
    // and a device error/unplug (Exception != null) — standard NAudio idiom.
    private void HandleStreamStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null)
        {
            return;
        }

        // Capture and render can both fault around the same time; only the
        // first failure should trigger teardown and notification.
        if (Interlocked.CompareExchange(ref _failureReportedFlag, 1, 0) != 0)
        {
            return;
        }

        Stop();
        Failed?.Invoke(this, e.Exception);
    }

    public void Stop()
    {
        // Callers may end up invoking Stop() twice on the same instance (e.g.
        // a failed device switch followed by app shutdown); re-disposing
        // already-disposed WASAPI/MMDevice objects is likely to throw.
        if (Interlocked.CompareExchange(ref _stoppedFlag, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _output?.Stop();
            _capture?.StopRecording();
        }
        finally
        {
            // A device left "in use" after a crash forces a reboot to free it again,
            // so cleanup runs even if Stop/StopRecording above throws.
            _output?.Dispose();
            _capture?.Dispose();
            _captureDevice.Dispose();
            _renderDevice.Dispose();
        }
    }

    // Capture and render endpoints rarely agree on sample rate or channel count
    // (e.g. a mono 48kHz mic vs. VB-CABLE's stereo 44.1kHz mix format), so both
    // conversions are applied only when the formats actually diverge.
    private IWaveProvider BuildOutputProvider(BufferedWaveProvider buffer, WaveFormat renderFormat)
    {
        IWaveProvider waveProvider = buffer;

        if (waveProvider.WaveFormat.SampleRate != renderFormat.SampleRate)
        {
            var resampledFormat = WaveFormat.CreateIeeeFloatWaveFormat(
                renderFormat.SampleRate,
                waveProvider.WaveFormat.Channels);
            waveProvider = new MediaFoundationResampler(waveProvider, resampledFormat);
        }

        ISampleProvider sampleProvider = waveProvider.ToSampleProvider();

        if (sampleProvider.WaveFormat.Channels != renderFormat.Channels)
        {
            sampleProvider = sampleProvider.WaveFormat.Channels == 1 && renderFormat.Channels == 2
                ? new MonoToStereoSampleProvider(sampleProvider)
                : new StereoToMonoSampleProvider(sampleProvider);
        }

        sampleProvider = ApplyDsp(sampleProvider);

        return sampleProvider.ToWaveProvider();
    }

    private ISampleProvider ApplyDsp(ISampleProvider input)
    {
        // The mute gate must stay outermost/last regardless of DspEnabled: it's
        // how the toggle hotkey mutes the mic, not a DSP quality knob, so
        // bypassing the DSP chain for A/B testing must never bypass muting too.
        ISampleProvider processed = _config.DspEnabled ? new DspChain(input, _config) : input;
        return new MuteGateSampleProvider(processed, _muteController);
    }
}
