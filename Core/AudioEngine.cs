using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NoelMic.Core;

/// <summary>
/// Captures audio from a physical input device and renders it straight through
/// to a render device (VB-CABLE's input endpoint) with no processing.
/// </summary>
public sealed class AudioEngine
{
    private readonly MMDevice _captureDevice;
    private readonly MMDevice _renderDevice;

    private WasapiCapture? _capture;
    private WasapiOut? _output;

    public AudioEngine(MMDevice captureDevice, MMDevice renderDevice)
    {
        _captureDevice = captureDevice;
        _renderDevice = renderDevice;
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

        _capture.StartRecording();
        _output.Play();
    }

    public void Stop()
    {
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
    private static IWaveProvider BuildOutputProvider(BufferedWaveProvider buffer, WaveFormat renderFormat)
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

        // Phase 3 seam: the DSP chain (HPF, gate, EQ, compressor, limiter, mute gate)
        // will be inserted here, right before the signal reaches the render device.
        sampleProvider = ApplyDsp(sampleProvider);

        return sampleProvider.ToWaveProvider();
    }

    private static ISampleProvider ApplyDsp(ISampleProvider input) => input;
}
