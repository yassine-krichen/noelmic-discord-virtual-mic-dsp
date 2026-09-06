namespace NoelMic.Config;

/// <summary>
/// Flat, fully-resolved set of DSP parameters — every field here is
/// non-nullable, because by the time <see cref="DspChain"/> reads it, "use
/// the preset value" has already been decided. <see cref="AppConfig"/> and
/// its nested *Config classes are the nullable, partially-overridden
/// user-facing shape; this is the resolved shape DSP stages actually
/// construct from.
/// </summary>
public sealed record DspParameters(
    float InputGainDb,
    float HighpassHz,
    float LowShelfHz,
    float LowShelfDb,
    float MudCutHz,
    float MudCutDb,
    float MudCutQ,
    float PresenceHz,
    float PresenceDb,
    float PresenceQ,
    float AirHz,
    float AirDb,
    float DeEsserHz,
    double DeEsserThresholdDb,
    double DeEsserRatio,
    float DeEsserMaxReductionDb,
    double GateThresholdDb,
    double GateHysteresisDb,
    float GateHoldMs,
    float GateAttackMs,
    float GateReleaseMs,
    double CompThresholdDb,
    double CompRatio,
    float CompAttackMs,
    float CompReleaseMs,
    double CompMakeupGainDb,
    double CompMixPercent,
    float SaturationDrive,
    float LimiterCeilingDb,
    float LimiterAttackMs,
    float LimiterReleaseMs)
{
    // Every override below is applied by name via `with`, never positionally
    // — with ~30 same-typed (float/double) fields, a positional constructor
    // call here would be a silent-bug factory the moment two fields swap
    // order.
    public static DspParameters Resolve(AppConfig config)
    {
        // config.Preset is only null for an AppConfig built directly (e.g.
        // in tests) without going through AppConfig.Load, which always
        // normalizes it to a concrete preset name before returning.
        var preset = PresetDefinitions.Get(config.Preset ?? "Broadcast");

        return preset with
        {
            InputGainDb = config.InputGainDb ?? preset.InputGainDb,
            HighpassHz = config.Eq.HighpassHz ?? preset.HighpassHz,
            LowShelfHz = config.Eq.LowShelfHz ?? preset.LowShelfHz,
            LowShelfDb = config.Eq.LowShelfDb ?? preset.LowShelfDb,
            MudCutHz = config.Eq.MudCutHz ?? preset.MudCutHz,
            MudCutDb = config.Eq.MudCutDb ?? preset.MudCutDb,
            MudCutQ = config.Eq.MudCutQ ?? preset.MudCutQ,
            PresenceHz = config.Eq.PresenceHz ?? preset.PresenceHz,
            PresenceDb = config.Eq.PresenceBoostDb ?? preset.PresenceDb,
            PresenceQ = config.Eq.PresenceQ ?? preset.PresenceQ,
            AirHz = config.Eq.AirHz ?? preset.AirHz,
            AirDb = config.Eq.AirDb ?? preset.AirDb,
            DeEsserHz = config.DeEsser.Hz ?? preset.DeEsserHz,
            DeEsserThresholdDb = config.DeEsser.ThresholdDb ?? preset.DeEsserThresholdDb,
            DeEsserRatio = config.DeEsser.Ratio ?? preset.DeEsserRatio,
            DeEsserMaxReductionDb = config.DeEsser.MaxReductionDb ?? preset.DeEsserMaxReductionDb,
            GateThresholdDb = config.Gate.ThresholdDb ?? preset.GateThresholdDb,
            GateHysteresisDb = config.Gate.HysteresisDb ?? preset.GateHysteresisDb,
            GateHoldMs = config.Gate.HoldMs ?? preset.GateHoldMs,
            GateAttackMs = config.Gate.AttackMs ?? preset.GateAttackMs,
            GateReleaseMs = config.Gate.ReleaseMs ?? preset.GateReleaseMs,
            CompThresholdDb = config.Compressor.ThresholdDb ?? preset.CompThresholdDb,
            CompRatio = config.Compressor.Ratio ?? preset.CompRatio,
            CompAttackMs = config.Compressor.AttackMs ?? preset.CompAttackMs,
            CompReleaseMs = config.Compressor.ReleaseMs ?? preset.CompReleaseMs,
            CompMakeupGainDb = config.Compressor.MakeupGainDb ?? preset.CompMakeupGainDb,
            CompMixPercent = config.Compressor.MixPercent ?? preset.CompMixPercent,
            SaturationDrive = config.SaturationDrive ?? preset.SaturationDrive,
            LimiterCeilingDb = config.Limiter.CeilingDb ?? preset.LimiterCeilingDb,
            LimiterAttackMs = config.Limiter.AttackMs ?? preset.LimiterAttackMs,
            LimiterReleaseMs = config.Limiter.ReleaseMs ?? preset.LimiterReleaseMs,
        };
    }
}
