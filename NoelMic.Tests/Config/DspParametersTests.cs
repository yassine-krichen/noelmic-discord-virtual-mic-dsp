using NoelMic.Config;

namespace NoelMic.Tests.Config;

public class DspParametersTests
{
    [Fact]
    public void Resolve_WithNoOverrides_MatchesPresetValuesExactly()
    {
        var config = new AppConfig { Preset = "Warm" };

        var resolved = DspParameters.Resolve(config);

        Assert.Equal(PresetDefinitions.Get("Warm"), resolved);
    }

    [Fact]
    public void Resolve_WithExplicitInputGainOverride_PrefersConfigValueAndLeavesOtherFieldsAtPresetValues()
    {
        var config = new AppConfig { Preset = "Broadcast", InputGainDb = 99f };
        var presetBaseline = PresetDefinitions.Get("Broadcast");

        var resolved = DspParameters.Resolve(config);

        Assert.Equal(99f, resolved.InputGainDb);
        Assert.Equal(presetBaseline with { InputGainDb = 99f }, resolved);
    }

    [Fact]
    public void Resolve_WithExplicitEqOverride_PrefersConfigValueAndLeavesOtherEqFieldsAtPresetValues()
    {
        var config = new AppConfig { Preset = "Natural", Eq = { LowShelfDb = 11f } };
        var presetBaseline = PresetDefinitions.Get("Natural");

        var resolved = DspParameters.Resolve(config);

        Assert.Equal(11f, resolved.LowShelfDb);
        Assert.Equal(presetBaseline with { LowShelfDb = 11f }, resolved);
    }

    [Fact]
    public void Resolve_WithExplicitDeEsserMaxReductionOverride_PrefersConfigValueAndLeavesOtherDeEsserFieldsAtPresetValues()
    {
        var config = new AppConfig { Preset = "Natural", DeEsser = { MaxReductionDb = 20f } };
        var presetBaseline = PresetDefinitions.Get("Natural");

        var resolved = DspParameters.Resolve(config);

        Assert.Equal(20f, resolved.DeEsserMaxReductionDb);
        Assert.Equal(presetBaseline with { DeEsserMaxReductionDb = 20f }, resolved);
    }

    [Fact]
    public void Resolve_WithExplicitCompressorMixPercentOverride_DoesNotAlsoOverrideMakeupGain()
    {
        var config = new AppConfig { Preset = "Bright", Compressor = { MixPercent = 33.0 } };
        var presetBaseline = PresetDefinitions.Get("Bright");

        var resolved = DspParameters.Resolve(config);

        Assert.Equal(33.0, resolved.CompMixPercent);
        Assert.Equal(presetBaseline.CompMakeupGainDb, resolved.CompMakeupGainDb);
        Assert.Equal(presetBaseline with { CompMixPercent = 33.0 }, resolved);
    }

    [Fact]
    public void Resolve_WithAllEqFieldsUnset_FallsThroughToPresetValuesForEveryField()
    {
        var config = new AppConfig { Preset = "Bright", Eq = new EqConfig() };

        var resolved = DspParameters.Resolve(config);

        Assert.Equal(PresetDefinitions.Get("Bright"), resolved);
    }

    [Theory]
    [InlineData("NotARealPreset")]
    [InlineData("")]
    [InlineData("xyz123")]
    public void Resolve_WithUnknownPresetName_FallsBackToBroadcast(string presetName)
    {
        var config = new AppConfig { Preset = presetName };

        var resolved = DspParameters.Resolve(config);

        Assert.Equal(PresetDefinitions.Get("Broadcast"), resolved);
    }

    [Theory]
    [InlineData("broadcast")]
    [InlineData("BROADCAST")]
    [InlineData("Broadcast")]
    public void Resolve_WithCaseVariantBroadcastPresetName_ResolvesToSameValuesRegardlessOfCase(string presetName)
    {
        var config = new AppConfig { Preset = presetName };

        var resolved = DspParameters.Resolve(config);

        Assert.Equal(PresetDefinitions.Get("Broadcast"), resolved);
    }

    public static IEnumerable<object[]> AllPresetNames =>
        PresetDefinitions.Names.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(AllPresetNames))]
    public void Resolve_ForEveryBuiltInPreset_DoesNotThrowAndProducesPlausibleValues(string presetName)
    {
        var config = new AppConfig { Preset = presetName };

        var resolved = DspParameters.Resolve(config);

        Assert.True(resolved.CompRatio >= 1.0, $"{presetName}: CompRatio should be >= 1, was {resolved.CompRatio}");
        Assert.True(resolved.DeEsserRatio >= 1.0, $"{presetName}: DeEsserRatio should be >= 1, was {resolved.DeEsserRatio}");
        Assert.True(resolved.GateThresholdDb < 0, $"{presetName}: GateThresholdDb should be negative, was {resolved.GateThresholdDb}");
        Assert.True(resolved.CompThresholdDb < 0, $"{presetName}: CompThresholdDb should be negative, was {resolved.CompThresholdDb}");
        Assert.True(resolved.LimiterCeilingDb < 0, $"{presetName}: LimiterCeilingDb should be negative, was {resolved.LimiterCeilingDb}");
        Assert.True(resolved.SaturationDrive > 0, $"{presetName}: SaturationDrive should be positive, was {resolved.SaturationDrive}");
        Assert.InRange(resolved.CompMixPercent, 0.0, 100.0);
        Assert.InRange(resolved.DeEsserMaxReductionDb, 0f, 30f);
    }

    [Fact]
    public void Resolve_BroadcastPreset_MatchesKnownAirDbFromSourceTable()
    {
        var resolved = DspParameters.Resolve(new AppConfig { Preset = "Broadcast" });

        Assert.Equal(3f, resolved.AirDb);
    }

    [Fact]
    public void Resolve_WarmPreset_MatchesKnownLowShelfDbFromSourceTable()
    {
        var resolved = DspParameters.Resolve(new AppConfig { Preset = "Warm" });

        Assert.Equal(4f, resolved.LowShelfDb);
    }

    [Fact]
    public void Resolve_RawPreset_MatchesKnownCompMixPercentFromSourceTable()
    {
        var resolved = DspParameters.Resolve(new AppConfig { Preset = "Raw" });

        Assert.Equal(0.0, resolved.CompMixPercent);
    }

    [Fact]
    public void Resolve_BrightPreset_MatchesKnownPresenceHzFromSourceTable()
    {
        var resolved = DspParameters.Resolve(new AppConfig { Preset = "Bright" });

        Assert.Equal(4000f, resolved.PresenceHz);
    }

    // Full-table comparisons below reconstruct every field of every preset,
    // independently and with named arguments, straight from PresetDefinitions
    // — this is the strongest available guard against a hand-typed named
    // argument silently landing on the wrong field (e.g. CompMixPercent and
    // CompMakeupGainDb swapped), since a partial/spot check could miss a swap
    // between two fields neither spot check happens to cover.
    public static IEnumerable<object[]> PresetsWithExpectedParameters()
    {
        yield return new object[]
        {
            "Raw",
            new DspParameters(
                InputGainDb: 12,
                HighpassHz: 80,
                LowShelfHz: 150,
                LowShelfDb: 0,
                MudCutHz: 300,
                MudCutDb: 0,
                MudCutQ: 1.0f,
                PresenceHz: 3200,
                PresenceDb: 0,
                PresenceQ: 1.0f,
                AirHz: 11000,
                AirDb: 0,
                DeEsserHz: 6500,
                DeEsserThresholdDb: -30,
                DeEsserRatio: 1.0,
                DeEsserMaxReductionDb: 0,
                GateThresholdDb: -50,
                GateHysteresisDb: 6,
                GateHoldMs: 150,
                GateAttackMs: 5,
                GateReleaseMs: 200,
                CompThresholdDb: -20,
                CompRatio: 1.0,
                CompAttackMs: 8,
                CompReleaseMs: 120,
                CompMakeupGainDb: 0,
                CompMixPercent: 0,
                SaturationDrive: 1.0f,
                LimiterCeilingDb: -1,
                LimiterAttackMs: 2,
                LimiterReleaseMs: 50,
                NoiseSuppressionEnabled: false),
        };

        yield return new object[]
        {
            "Natural",
            new DspParameters(
                InputGainDb: 12,
                HighpassHz: 80,
                LowShelfHz: 150,
                LowShelfDb: 0,
                MudCutHz: 200,
                MudCutDb: -1.5f,
                MudCutQ: 1.0f,
                PresenceHz: 3000,
                PresenceDb: 1.5f,
                PresenceQ: 0.9f,
                AirHz: 11000,
                AirDb: 1.5f,
                DeEsserHz: 6500,
                DeEsserThresholdDb: -28,
                DeEsserRatio: 3.0,
                DeEsserMaxReductionDb: 6,
                GateThresholdDb: -50,
                GateHysteresisDb: 6,
                GateHoldMs: 150,
                GateAttackMs: 5,
                GateReleaseMs: 200,
                CompThresholdDb: -20,
                CompRatio: 2.5,
                CompAttackMs: 8,
                CompReleaseMs: 120,
                CompMakeupGainDb: 2,
                CompMixPercent: 100,
                SaturationDrive: 1.0f,
                LimiterCeilingDb: -1,
                LimiterAttackMs: 2,
                LimiterReleaseMs: 50,
                NoiseSuppressionEnabled: true),
        };

        yield return new object[]
        {
            "Broadcast",
            new DspParameters(
                InputGainDb: 12,
                HighpassHz: 75,
                LowShelfHz: 150,
                LowShelfDb: 2.5f,
                MudCutHz: 280,
                MudCutDb: -2,
                MudCutQ: 1.2f,
                PresenceHz: 3200,
                PresenceDb: 3,
                PresenceQ: 0.9f,
                AirHz: 11000,
                AirDb: 3,
                DeEsserHz: 6500,
                DeEsserThresholdDb: -30,
                DeEsserRatio: 4.0,
                DeEsserMaxReductionDb: 8,
                GateThresholdDb: -48,
                GateHysteresisDb: 6,
                GateHoldMs: 150,
                GateAttackMs: 5,
                GateReleaseMs: 180,
                CompThresholdDb: -22,
                CompRatio: 4.0,
                CompAttackMs: 5,
                CompReleaseMs: 80,
                CompMakeupGainDb: 5,
                CompMixPercent: 75,
                SaturationDrive: 1.8f,
                LimiterCeilingDb: -1,
                LimiterAttackMs: 2,
                LimiterReleaseMs: 50,
                NoiseSuppressionEnabled: true),
        };

        yield return new object[]
        {
            "Warm",
            new DspParameters(
                InputGainDb: 12,
                HighpassHz: 70,
                LowShelfHz: 180,
                LowShelfDb: 4,
                MudCutHz: 350,
                MudCutDb: -2,
                MudCutQ: 1.4f,
                PresenceHz: 2800,
                PresenceDb: 2,
                PresenceQ: 0.8f,
                AirHz: 10000,
                AirDb: 2,
                DeEsserHz: 6000,
                DeEsserThresholdDb: -30,
                DeEsserRatio: 4.0,
                DeEsserMaxReductionDb: 8,
                GateThresholdDb: -48,
                GateHysteresisDb: 6,
                GateHoldMs: 150,
                GateAttackMs: 5,
                GateReleaseMs: 200,
                CompThresholdDb: -22,
                CompRatio: 3.5,
                CompAttackMs: 10,
                CompReleaseMs: 120,
                CompMakeupGainDb: 4,
                CompMixPercent: 80,
                SaturationDrive: 2.2f,
                LimiterCeilingDb: -1,
                LimiterAttackMs: 2,
                LimiterReleaseMs: 50,
                NoiseSuppressionEnabled: true),
        };

        yield return new object[]
        {
            "Bright",
            new DspParameters(
                InputGainDb: 14,
                HighpassHz: 90,
                LowShelfHz: 150,
                LowShelfDb: 0,
                MudCutHz: 300,
                MudCutDb: -3,
                MudCutQ: 1.2f,
                PresenceHz: 4000,
                PresenceDb: 4,
                PresenceQ: 1.0f,
                AirHz: 12000,
                AirDb: 3.5f,
                DeEsserHz: 6500,
                DeEsserThresholdDb: -32,
                DeEsserRatio: 5.0,
                DeEsserMaxReductionDb: 10,
                GateThresholdDb: -45,
                GateHysteresisDb: 6,
                GateHoldMs: 120,
                GateAttackMs: 5,
                GateReleaseMs: 150,
                CompThresholdDb: -20,
                CompRatio: 4.0,
                CompAttackMs: 4,
                CompReleaseMs: 70,
                CompMakeupGainDb: 5,
                CompMixPercent: 85,
                SaturationDrive: 1.5f,
                LimiterCeilingDb: -1,
                LimiterAttackMs: 2,
                LimiterReleaseMs: 50,
                NoiseSuppressionEnabled: true),
        };
    }

    [Theory]
    [MemberData(nameof(PresetsWithExpectedParameters))]
    public void Get_ForEachBuiltInPreset_MatchesExactSourceTableValues(string presetName, DspParameters expected)
    {
        var actual = PresetDefinitions.Get(presetName);

        Assert.Equal(expected, actual);
    }
}
