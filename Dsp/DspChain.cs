using NAudio.Wave;
using NoelMic.Config;
using NoelMic.Core;

namespace NoelMic.Dsp;

/// <summary>
/// Composes the full DSP pipeline in the order the spec requires:
/// input gain -> level meter -> calibration meter -> HPF -> gate -> low shelf
/// -> mud-cut EQ -> presence EQ -> air shelf -> de-esser -> compressor ->
/// saturation -> limiter -> output level meter. A thin delegator, same idiom
/// as MuteGateSampleProvider — no new abstraction beyond composition.
/// </summary>
public sealed class DspChain : ISampleProvider, IDisposable
{
    private readonly ISampleProvider _inner;
    private readonly DspParameterStore _store;

    private readonly InputGain _inputGain;
    private readonly NoiseSuppressor _noiseSuppressor;
    private readonly LevelMeter _inputLevelMeter;
    private readonly CalibrationMeter _calibrationMeter;
    private readonly Biquad _highpass;
    private readonly NoiseGate _gate;
    private readonly Biquad _lowShelf;
    private readonly Biquad _mudCut;
    private readonly Biquad _presence;
    private readonly Biquad _airShelf;
    private readonly DeEsser _deEsser;
    private readonly Compressor _compressor;
    private readonly Saturation _saturation;
    private readonly Limiter _limiter;
    private readonly LevelMeter _outputLevelMeter;

    private readonly int _sampleRate;

    // Reference, not structural, equality — see the comment on the gate in
    // Read() for why.
    private DspParameters? _lastApplied;

    public DspChain(ISampleProvider source, AppConfig config)
    {
        var p = DspParameters.Resolve(config);
        _sampleRate = source.WaveFormat.SampleRate;
        _store = new DspParameterStore(p);

        // Every stage is always constructed, even for presets where a
        // parameter is a no-op (e.g. Raw's 0dB shelves/mud-cut) — no
        // conditional branching, so every preset runs the exact same chain
        // topology and only the coefficients differ.
        _inputGain = new InputGain(source, p.InputGainDb);
        ISampleProvider chain = _inputGain;

        // RNNoise wants a properly gain-staged input, and downstream meters/
        // calibration should see the denoised signal the user actually
        // hears — so this sits right after input gain and before anything
        // else observes or shapes the signal.
        _noiseSuppressor = new NoiseSuppressor(chain, p.NoiseSuppressionEnabled);
        chain = _noiseSuppressor;

        _inputLevelMeter = new LevelMeter(chain);
        chain = _inputLevelMeter;

        _calibrationMeter = new CalibrationMeter(chain, _store);
        chain = _calibrationMeter;

        _highpass = new Biquad(chain, BiquadCoefficients.HighPass(_sampleRate, p.HighpassHz));
        chain = _highpass;

        _gate = new NoiseGate(chain, p.GateThresholdDb,
            attackMs: p.GateAttackMs, releaseMs: p.GateReleaseMs,
            hysteresisDb: p.GateHysteresisDb, holdMs: p.GateHoldMs);
        chain = _gate;

        _lowShelf = new Biquad(chain, BiquadCoefficients.LowShelf(_sampleRate, p.LowShelfHz, p.LowShelfDb));
        chain = _lowShelf;

        // Mud-cut sits post-gate, not pre-gate like the HPF: the HPF removes
        // rumble that would otherwise mislead the gate's envelope detector,
        // but this mild narrow cut doesn't carry that same justification —
        // and stacking it onto the detection path would compound two
        // simultaneous changes (gate ballistics + new EQ) during this retest.
        _mudCut = new Biquad(chain, BiquadCoefficients.PeakingEq(_sampleRate, p.MudCutHz, p.MudCutDb, p.MudCutQ));
        chain = _mudCut;

        _presence = new Biquad(chain, BiquadCoefficients.PeakingEq(_sampleRate, p.PresenceHz, p.PresenceDb, p.PresenceQ));
        chain = _presence;

        _airShelf = new Biquad(chain, BiquadCoefficients.HighShelf(_sampleRate, p.AirHz, p.AirDb));
        chain = _airShelf;

        _deEsser = new DeEsser(chain, p.DeEsserHz, p.DeEsserThresholdDb, p.DeEsserRatio, p.DeEsserMaxReductionDb);
        chain = _deEsser;

        _compressor = new Compressor(chain, p.CompThresholdDb, p.CompRatio,
            attackMs: p.CompAttackMs, releaseMs: p.CompReleaseMs,
            makeupGainDb: p.CompMakeupGainDb, mixPercent: p.CompMixPercent);
        chain = _compressor;

        _saturation = new Saturation(chain, p.SaturationDrive);
        chain = _saturation;

        _limiter = new Limiter(chain, ceilingDb: p.LimiterCeilingDb,
            attackMs: p.LimiterAttackMs, releaseMs: p.LimiterReleaseMs);
        chain = _limiter;

        _outputLevelMeter = new LevelMeter(chain);
        chain = _outputLevelMeter;

        _inner = chain;
        _lastApplied = p;
    }

    public WaveFormat WaveFormat => _inner.WaveFormat;

    public float CurrentInputLevelDb => _inputLevelMeter.CurrentDb;

    public float CurrentOutputLevelDb => _outputLevelMeter.CurrentDb;

    public void UpdateParameters(AppConfig config) => _store.Update(DspParameters.Resolve(config));

    // NoiseSuppressor owns unmanaged (native RNNoise) state, unlike every
    // other stage in this chain — see AudioEngine.Stop(), which disposes
    // the DspChain on every device switch/auto-recovery so that state
    // doesn't leak.
    public void Dispose() => _noiseSuppressor.Dispose();

    public void StartCalibration() => _calibrationMeter.StartCalibration();

    public (bool IsRunning, bool IsDone, int QualifyingFrameCount, float ComputedGainDb) GetCalibrationStatus() =>
        (_calibrationMeter.IsRunning, _calibrationMeter.IsDone, _calibrationMeter.QualifyingFrameCount, _calibrationMeter.ComputedGainDb);

    public MeteringSnapshot GetMeteringSnapshot() => new(
        InputLevelDb: _inputLevelMeter.CurrentDb,
        OutputLevelDb: _outputLevelMeter.CurrentDb,
        GateIsOpen: _gate.IsOpen,
        GateReductionDb: _gate.CurrentReductionDb,
        CompressorReductionDb: _compressor.CurrentReductionDb,
        DeEsserReductionDb: _deEsser.CurrentReductionDb);

    public int Read(Span<float> buffer)
    {
        var current = _store.Current;

        // A pointer compare, not the record's structural Equals — structural
        // equality would compare all ~30 fields on every single buffer for
        // the entire session. Parameter changes are rare and user-triggered
        // (a settings edit, a calibration result), so this only misses on
        // those rare occasions, and recomputing every stage's coefficients
        // then is effectively free.
        if (!ReferenceEquals(current, _lastApplied))
        {
            ApplyParameters(current);
            _lastApplied = current;
        }

        return _inner.Read(buffer);
    }

    // Called from Read() on the audio thread, never the UI thread — every
    // stage's UpdateXxx only ever runs here, so none of their internal
    // fields need volatile/Interlocked; only _store's published reference
    // needs that guarantee.
    private void ApplyParameters(DspParameters p)
    {
        _inputGain.UpdateGainDb(p.InputGainDb);
        _noiseSuppressor.UpdateEnabled(p.NoiseSuppressionEnabled);
        _highpass.UpdateCoefficients(BiquadCoefficients.HighPass(_sampleRate, p.HighpassHz));
        _gate.UpdateParameters(p.GateThresholdDb, p.GateAttackMs, p.GateReleaseMs, p.GateHysteresisDb, p.GateHoldMs);
        _lowShelf.UpdateCoefficients(BiquadCoefficients.LowShelf(_sampleRate, p.LowShelfHz, p.LowShelfDb));
        _mudCut.UpdateCoefficients(BiquadCoefficients.PeakingEq(_sampleRate, p.MudCutHz, p.MudCutDb, p.MudCutQ));
        _presence.UpdateCoefficients(BiquadCoefficients.PeakingEq(_sampleRate, p.PresenceHz, p.PresenceDb, p.PresenceQ));
        _airShelf.UpdateCoefficients(BiquadCoefficients.HighShelf(_sampleRate, p.AirHz, p.AirDb));
        _deEsser.UpdateParameters(p.DeEsserHz, p.DeEsserThresholdDb, p.DeEsserRatio, p.DeEsserMaxReductionDb);
        _compressor.UpdateParameters(p.CompThresholdDb, p.CompRatio, p.CompAttackMs, p.CompReleaseMs, p.CompMakeupGainDb, p.CompMixPercent);
        _saturation.UpdateDrive(p.SaturationDrive);
        _limiter.UpdateParameters(p.LimiterCeilingDb, p.LimiterAttackMs, p.LimiterReleaseMs);
    }
}
