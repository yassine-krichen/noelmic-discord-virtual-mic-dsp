using NoelMic.Config;
using NoelMic.Dsp;
using NoelMic.Tests.TestSupport;

namespace NoelMic.Tests.Dsp;

public class CalibrationMeterTests
{
    private const int SampleRate = 8000;
    private const int Channels = 1;
    private const float CalibrationDurationSeconds = 5f;
    private const long TargetFrameCount = (long)(CalibrationDurationSeconds * SampleRate);
    private const float TargetRmsDb = -18f;

    [Fact]
    public void Read_NeverMutatesTheSignal_EvenWhileCalibrationIsRunning()
    {
        const float value = 0.37f;
        var source = new ConstantSampleProvider(SampleRate, Channels, value);
        var meter = new CalibrationMeter(source, new DspParameterStore(RawWithGain(0f)));

        meter.StartCalibration();
        var output = meter.ReadAll(sampleCount: 4096);

        Assert.All(output, sample => Assert.Equal(value, sample));
    }

    [Fact]
    public void IsRunning_and_IsDone_BeforeStartCalibrationIsCalled_BothReportNotRunning()
    {
        var source = new ConstantSampleProvider(SampleRate, Channels, value: 0.1f);
        var meter = new CalibrationMeter(source, new DspParameterStore(RawWithGain(0f)));

        meter.ReadAll(sampleCount: 4096);

        Assert.False(meter.IsRunning);
        Assert.False(meter.IsDone);
    }

    [Fact]
    public void StartCalibration_AfterFullWindowOfKnownAmplitudeSpeech_ComputesGainThatWouldReachTargetLevel()
    {
        const float amplitude = 0.1f; // constant-signal RMS == amplitude, so its dBFS is exactly known.
        var measuredDb = 20f * MathF.Log10(amplitude);
        var expectedGainDb = TargetRmsDb - measuredDb; // gainAtStart is 0 here.

        var source = new ConstantSampleProvider(SampleRate, Channels, amplitude);
        var meter = new CalibrationMeter(source, new DspParameterStore(RawWithGain(0f)));

        meter.StartCalibration();
        meter.ReadAll(sampleCount: (int)TargetFrameCount);

        Assert.True(meter.IsDone, "Expected calibration to complete after a full 5-second window.");
        Assert.True(
            MathF.Abs(meter.ComputedGainDb - expectedGainDb) < 0.1f,
            $"Expected computed gain near {expectedGainDb}dB (bringing {amplitude} to {TargetRmsDb}dBFS), got {meter.ComputedGainDb}dB.");
    }

    [Fact]
    public void StartCalibration_WithSilenceForHalfTheWindow_ComputesGainFromTheSpeechOnly_NotTheAverageWithSilence()
    {
        const float speechAmplitude = 0.1f;
        var speechDb = 20f * MathF.Log10(speechAmplitude);
        var expectedGainDb = TargetRmsDb - speechDb; // gainAtStart is 0 here.

        var source = new SwitchingConstantSampleProvider(
            SampleRate, Channels, firstValue: 0f, firstValueSampleCount: TargetFrameCount / 2, secondValue: speechAmplitude);
        var meter = new CalibrationMeter(source, new DspParameterStore(RawWithGain(0f)));
        meter.StartCalibration();

        // Read in chunks aligned to the silence/speech boundary so no single
        // buffer's per-buffer RMS blends silence and speech together --
        // isolating "excluded frames don't get averaged in" from buffer-edge
        // blending artifacts.
        const int chunk = 2000; // (TargetFrameCount / 2) divides evenly by this.
        var buffer = new float[chunk];
        for (var i = 0; i < TargetFrameCount / chunk; i++)
        {
            meter.Read(buffer);
        }

        Assert.True(meter.IsDone, "Expected calibration to complete after a full 5-second window.");
        Assert.True(
            MathF.Abs(meter.ComputedGainDb - expectedGainDb) < 0.1f,
            $"Expected computed gain to reflect only the speech half ({expectedGainDb}dB), not an average " +
            $"dragged down by silence, got {meter.ComputedGainDb}dB.");
    }

    [Fact]
    public void QualifyingFrameCount_WithOnlySilenceForTheFullWindow_IsZero()
    {
        var source = new ConstantSampleProvider(SampleRate, Channels, value: 0f);
        var meter = new CalibrationMeter(source, new DspParameterStore(RawWithGain(0f)));

        meter.StartCalibration();
        meter.ReadAll(sampleCount: (int)TargetFrameCount);

        Assert.Equal(0, meter.QualifyingFrameCount);
    }

    [Fact]
    public void StartCalibration_WithOnlySilenceForTheFullWindow_NeverProducesNaNOrInfiniteComputedGain()
    {
        var source = new ConstantSampleProvider(SampleRate, Channels, value: 0f);
        var meter = new CalibrationMeter(source, new DspParameterStore(RawWithGain(0f)));

        meter.StartCalibration();
        meter.ReadAll(sampleCount: (int)TargetFrameCount);

        Assert.True(meter.IsDone);
        Assert.True(float.IsFinite(meter.ComputedGainDb), $"Expected a finite gain on all-silence input, got {meter.ComputedGainDb}.");
    }

    [Fact]
    public void ComputedGainDb_AccountsForTheStoresGainAtStart_SoTheImpliedTargetLevelIsConsistentAcrossDifferentStartingGains()
    {
        // CalibrationMeter measures the signal as it already sits (i.e. as
        // if already passed through whatever gain the store held when
        // StartCalibration was called), then reports the *absolute* new gain
        // that would bring it to target. Feeding the identical measured
        // signal at two different starting gains should therefore produce
        // computed gains that differ by exactly that starting-gain delta,
        // and each should imply the same -18dBFS target once you account for
        // what the pre-gain source level must have been in each case.
        const float measuredAmplitude = 0.1f;
        var measuredDb = 20f * MathF.Log10(measuredAmplitude);

        var gainAtLowStart = ComputeGain(measuredAmplitude, storeGainDb: 0f);
        var gainAtHighStart = ComputeGain(measuredAmplitude, storeGainDb: 10f);

        Assert.True(
            MathF.Abs((gainAtHighStart - gainAtLowStart) - 10f) < 0.1f,
            $"Expected the 10dB difference in starting gain to carry straight through into the computed " +
            $"gain, got low-start={gainAtLowStart}dB, high-start={gainAtHighStart}dB.");

        var impliedTargetAtLowStart = (measuredDb - 0f) + gainAtLowStart;
        var impliedTargetAtHighStart = (measuredDb - 10f) + gainAtHighStart;

        Assert.True(
            MathF.Abs(impliedTargetAtLowStart - TargetRmsDb) < 0.1f,
            $"Expected applying the computed gain to reach {TargetRmsDb}dBFS, got {impliedTargetAtLowStart}dB.");
        Assert.True(
            MathF.Abs(impliedTargetAtHighStart - TargetRmsDb) < 0.1f,
            $"Expected applying the computed gain to reach {TargetRmsDb}dBFS, got {impliedTargetAtHighStart}dB.");
    }

    private static float ComputeGain(float amplitude, float storeGainDb)
    {
        var source = new ConstantSampleProvider(SampleRate, Channels, amplitude);
        var meter = new CalibrationMeter(source, new DspParameterStore(RawWithGain(storeGainDb)));

        meter.StartCalibration();
        meter.ReadAll(sampleCount: (int)TargetFrameCount);

        return meter.ComputedGainDb;
    }

    private static DspParameters RawWithGain(float inputGainDb) =>
        PresetDefinitions.Get("Raw") with { InputGainDb = inputGainDb };
}
