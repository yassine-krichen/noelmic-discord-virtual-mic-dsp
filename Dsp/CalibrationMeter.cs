using NAudio.Wave;
using NoelMic.Config;

namespace NoelMic.Dsp;

/// <summary>
/// Non-mutating pass-through stage that runs a one-shot 5-second calibration
/// pass: measures the mic's average speech level and computes the input gain
/// needed to bring it to a -18 dBFS target, so the user doesn't have to guess
/// a gain value by ear. Placed right after the input <see cref="LevelMeter"/>
/// in the chain, same idiom as that stage.
/// </summary>
public sealed class CalibrationMeter : ISampleProvider
{
    private const float TargetRmsDb = -18f;
    private const float SpeechPresenceThresholdDb = -40f;
    private const float CalibrationDurationSeconds = 5f;

    // Sized generously beyond what 5 seconds of even unusually small buffers
    // could produce — allocated once at construction, never per-Read, so
    // oversizing here is cheap and avoids ever growing or bounds-failing in
    // the audio hot path.
    private const int MaxQualifyingFrames = 50_000;

    private readonly ISampleProvider _source;
    private readonly DspParameterStore _store;
    private readonly int _channels;
    private readonly long _targetFrameCount;

    private readonly float[] _qualifyingFramesDb = new float[MaxQualifyingFrames];

    private volatile bool _isRunning;
    private volatile bool _isDone;
    private int _qualifyingFrameCount;
    private long _framesElapsed;
    private float _gainAtStart;
    private volatile float _computedGainDb;

    public CalibrationMeter(ISampleProvider source, DspParameterStore store)
    {
        _source = source;
        _store = store;
        _channels = source.WaveFormat.Channels;
        _targetFrameCount = (long)(CalibrationDurationSeconds * source.WaveFormat.SampleRate);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool IsRunning => _isRunning;

    public bool IsDone => _isDone;

    public int QualifyingFrameCount => _qualifyingFrameCount;

    public float ComputedGainDb => _computedGainDb;

    // UI-triggered, called from the Settings dialog — resets the
    // pre-allocated collection buffer's write position and the elapsed-frame
    // counter rather than allocating anything new.
    //
    // _isRunning/_isDone are volatile and act as release/acquire guards
    // around the non-volatile _qualifyingFrameCount/_framesElapsed/
    // _gainAtStart: this method writes them last, and Read()/FinishCalibration()
    // check/write them first, so the plain fields are always safely
    // published across the UI/audio thread boundary. The write ordering
    // here and in FinishCalibration() is therefore load-bearing — do not
    // reorder it.
    public void StartCalibration()
    {
        _qualifyingFrameCount = 0;
        _framesElapsed = 0;
        _gainAtStart = _store.Current.InputGainDb;
        _isDone = false;
        _isRunning = true;
    }

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);

        if (_isRunning)
        {
            CollectBuffer(buffer, samplesRead);
        }

        return samplesRead;
    }

    private void CollectBuffer(Span<float> buffer, int samplesRead)
    {
        var sumOfSquares = 0f;
        for (var i = 0; i < samplesRead; i++)
        {
            sumOfSquares += buffer[i] * buffer[i];
        }

        var rms = samplesRead > 0 ? MathF.Sqrt(sumOfSquares / samplesRead) : 0f;

        // Same mandatory epsilon floor as every other dB conversion in this
        // codebase — otherwise silence produces -Infinity here.
        var rmsDb = 20f * MathF.Log10(MathF.Max(rms, 1e-9f));

        if (rmsDb >= SpeechPresenceThresholdDb && _qualifyingFrameCount < MaxQualifyingFrames)
        {
            _qualifyingFramesDb[_qualifyingFrameCount] = rmsDb;
            _qualifyingFrameCount++;
        }

        _framesElapsed += samplesRead / _channels;

        if (_framesElapsed >= _targetFrameCount)
        {
            FinishCalibration();
        }
    }

    private void FinishCalibration()
    {
        _isRunning = false;

        var sumDb = 0f;
        for (var i = 0; i < _qualifyingFrameCount; i++)
        {
            sumDb += _qualifyingFramesDb[i];
        }

        // Mean, not a percentile — the "-18 dBFS average" target describes a
        // mean level, and these frames are already speech-filtered, so a
        // percentile of them would run hotter than the intended target.
        var meanSpeechDb = _qualifyingFrameCount > 0 ? sumDb / _qualifyingFrameCount : SpeechPresenceThresholdDb;

        _computedGainDb = _gainAtStart + (TargetRmsDb - meanSpeechDb);
        _isDone = true;
    }
}
