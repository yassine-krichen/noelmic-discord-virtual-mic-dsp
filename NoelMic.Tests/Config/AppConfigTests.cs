using NoelMic.Config;

namespace NoelMic.Tests.Config;

public class AppConfigTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public AppConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NoelMic.Tests-" + Guid.NewGuid());
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllConfiguredValues()
    {
        var original = new AppConfig
        {
            InputDeviceId = "device-123",
            ToggleButton = "XButton2",
            StartWithWindows = false,
            PlaySoundOnToggle = false,
            DspEnabled = true,
            Preset = "Warm",
            Eq = { HighpassHz = 120f, PresenceBoostDb = 5f },
            Compressor = { Ratio = 6.0, ThresholdDb = -22 },
            Gate = { ThresholdDb = -50 },
        };

        original.Save(_configPath);
        var loaded = AppConfig.Load(_configPath);

        Assert.Equal(original.InputDeviceId, loaded.InputDeviceId);
        Assert.Equal(original.ToggleButton, loaded.ToggleButton);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(original.PlaySoundOnToggle, loaded.PlaySoundOnToggle);
        Assert.Equal(original.DspEnabled, loaded.DspEnabled);
        Assert.Equal(original.Eq.HighpassHz, loaded.Eq.HighpassHz);
        Assert.Equal(original.Eq.PresenceBoostDb, loaded.Eq.PresenceBoostDb);
        Assert.Equal(original.Compressor.Ratio, loaded.Compressor.Ratio);
        Assert.Equal(original.Compressor.ThresholdDb, loaded.Compressor.ThresholdDb);
        Assert.Equal(original.Gate.ThresholdDb, loaded.Gate.ThresholdDb);
    }

    [Fact]
    public void Load_WithMissingFile_ReturnsDefaultsAndWritesThemToDisk()
    {
        Assert.False(File.Exists(_configPath));

        var loaded = AppConfig.Load(_configPath);
        var defaults = new AppConfig();

        Assert.Equal(defaults.Eq.HighpassHz, loaded.Eq.HighpassHz);
        Assert.Equal(defaults.Compressor.Ratio, loaded.Compressor.Ratio);
        Assert.Equal(defaults.Gate.ThresholdDb, loaded.Gate.ThresholdDb);
        Assert.True(File.Exists(_configPath), "Load() should persist newly created defaults to the given path.");
    }

    [Fact]
    public void Load_WithCorruptJson_FallsBackToDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, "{ this is not valid json ][");

        var exception = Record.Exception(() => AppConfig.Load(_configPath));

        Assert.Null(exception);

        var loaded = AppConfig.Load(_configPath);
        var defaults = new AppConfig();

        Assert.Equal(defaults.Eq.HighpassHz, loaded.Eq.HighpassHz);
        Assert.Equal(defaults.Compressor.Ratio, loaded.Compressor.Ratio);
        Assert.Equal(defaults.Gate.ThresholdDb, loaded.Gate.ThresholdDb);
    }

    [Fact]
    public void Load_WithEmptyJsonObject_FallsBackToPropertyDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, "{}");

        var loaded = AppConfig.Load(_configPath);
        var defaults = new AppConfig();

        Assert.Equal(defaults.InputDeviceId, loaded.InputDeviceId);
        Assert.Equal(defaults.Eq.HighpassHz, loaded.Eq.HighpassHz);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsExplicitlySetNullableDspOverrideFields()
    {
        var original = new AppConfig
        {
            Preset = "Warm",
            InputGainDb = 9.5f,
            SaturationDrive = 2.2f,
            Eq = { LowShelfDb = 3f, AirHz = 9000f },
            DeEsser = { Hz = 7000f, ThresholdDb = -25, Ratio = 3.5, MaxReductionDb = 6f },
            Limiter = { CeilingDb = -0.5f, AttackMs = 3f, ReleaseMs = 60f },
        };

        original.Save(_configPath);
        var loaded = AppConfig.Load(_configPath);

        Assert.Equal(original.Preset, loaded.Preset);
        Assert.Equal(original.InputGainDb, loaded.InputGainDb);
        Assert.Equal(original.SaturationDrive, loaded.SaturationDrive);
        Assert.Equal(original.Eq.LowShelfDb, loaded.Eq.LowShelfDb);
        Assert.Equal(original.Eq.AirHz, loaded.Eq.AirHz);
        Assert.Equal(original.DeEsser.Hz, loaded.DeEsser.Hz);
        Assert.Equal(original.DeEsser.ThresholdDb, loaded.DeEsser.ThresholdDb);
        Assert.Equal(original.DeEsser.Ratio, loaded.DeEsser.Ratio);
        Assert.Equal(original.DeEsser.MaxReductionDb, loaded.DeEsser.MaxReductionDb);
        Assert.Equal(original.Limiter.CeilingDb, loaded.Limiter.CeilingDb);
        Assert.Equal(original.Limiter.AttackMs, loaded.Limiter.AttackMs);
        Assert.Equal(original.Limiter.ReleaseMs, loaded.Limiter.ReleaseMs);
    }

    [Fact]
    public void Save_WithUnsetNullableFields_OmitsThemFromWrittenJson()
    {
        var config = new AppConfig();

        config.Save(_configPath);
        var json = File.ReadAllText(_configPath);

        Assert.DoesNotContain("inputGainDb", json);
        Assert.DoesNotContain("saturationDrive", json);
        Assert.DoesNotContain("highpassHz", json);
    }

    [Fact]
    public void SaveThenLoad_WithUnsetNullableFields_ReturnsNullRatherThanADefaultValue()
    {
        var config = new AppConfig();
        config.Save(_configPath);

        var loaded = AppConfig.Load(_configPath);

        Assert.Null(loaded.InputGainDb);
        Assert.Null(loaded.SaturationDrive);
        Assert.Null(loaded.Eq.HighpassHz);
        Assert.Null(loaded.DeEsser.Hz);
    }

    [Fact]
    public void Load_WithDspEnabledFalse_MigratesPresetToRaw()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, """{ "dspEnabled": false, "preset": "Bright" }""");

        var loaded = AppConfig.Load(_configPath);

        Assert.Equal("Raw", loaded.Preset);
    }

    [Fact]
    public void Load_WithDspEnabledFalse_ClearsDspEnabledSoMigrationIsOneShot()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, """{ "dspEnabled": false, "preset": "Bright" }""");

        var loaded = AppConfig.Load(_configPath);

        Assert.Equal("Raw", loaded.Preset);
        Assert.True(loaded.DspEnabled);

        // Simulates the user picking a different preset in Settings after
        // the one-shot migration ran, then saving.
        loaded.Preset = "Bright";
        loaded.Save(_configPath);

        var reloaded = AppConfig.Load(_configPath);

        Assert.Equal("Bright", reloaded.Preset);
    }

    [Fact]
    public void Load_WithPrePresetConfig_ClearsLegacyDspOverridesAndDefaultsToBroadcast()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, """
            {
              "dspEnabled": true,
              "eq": { "highpassHz": 90, "mudCutHz": 300, "mudCutDb": -3, "presenceHz": 4000, "presenceBoostDb": 3 },
              "compressor": { "ratio": 3, "thresholdDb": -18, "makeupGainDb": 3 },
              "gate": { "thresholdDb": -45, "hysteresisDb": 6, "holdMs": 120 }
            }
            """);

        var loaded = AppConfig.Load(_configPath);

        Assert.Equal("Broadcast", loaded.Preset);
        Assert.Null(loaded.Eq.HighpassHz);
        Assert.Null(loaded.Eq.MudCutHz);
        Assert.Null(loaded.Eq.MudCutDb);
        Assert.Null(loaded.Eq.PresenceHz);
        Assert.Null(loaded.Eq.PresenceBoostDb);
        Assert.Null(loaded.Compressor.Ratio);
        Assert.Null(loaded.Compressor.ThresholdDb);
        Assert.Null(loaded.Compressor.MakeupGainDb);
        Assert.Null(loaded.Gate.ThresholdDb);
        Assert.Null(loaded.Gate.HysteresisDb);
        Assert.Null(loaded.Gate.HoldMs);
    }

    [Fact]
    public void Load_WithNewModelConfig_LeavesExplicitOverridesUntouched()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, """
            {
              "preset": "Warm",
              "eq": { "airDb": 4.5 }
            }
            """);

        var loaded = AppConfig.Load(_configPath);

        Assert.Equal("Warm", loaded.Preset);
        Assert.Equal(4.5f, loaded.Eq.AirDb);
    }

    [Fact]
    public void Load_WithPresetPresentAndNoOverrides_KeepsAllDspFieldsNullAndPresetIntact()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, """{ "preset": "Bright" }""");

        var loaded = AppConfig.Load(_configPath);

        Assert.Equal("Bright", loaded.Preset);
        Assert.Null(loaded.Eq.HighpassHz);
        Assert.Null(loaded.Eq.MudCutHz);
        Assert.Null(loaded.Eq.MudCutDb);
        Assert.Null(loaded.Eq.PresenceHz);
        Assert.Null(loaded.Eq.PresenceBoostDb);
        Assert.Null(loaded.Compressor.Ratio);
        Assert.Null(loaded.Compressor.ThresholdDb);
        Assert.Null(loaded.Compressor.MakeupGainDb);
        Assert.Null(loaded.Gate.ThresholdDb);
        Assert.Null(loaded.Gate.HysteresisDb);
        Assert.Null(loaded.Gate.HoldMs);
    }

    [Fact]
    public void Load_WithDspEnabledTrue_DoesNotOverridePreset()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, """{ "dspEnabled": true, "preset": "Bright" }""");

        var loaded = AppConfig.Load(_configPath);

        Assert.Equal("Bright", loaded.Preset);
    }

    [Fact]
    public void Load_WithDspEnabledAbsentFromJson_DoesNotOverridePreset()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, """{ "preset": "Bright" }""");

        var loaded = AppConfig.Load(_configPath);

        Assert.Equal("Bright", loaded.Preset);
    }

    [Fact]
    public void Load_WithOldShapeConfigMissingAllNewFields_LoadsWithoutThrowingAndLeavesNewFieldsNull()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_configPath, """
            {
              "inputDeviceId": "old-device",
              "toggleButton": "XButton1",
              "startWithWindows": true,
              "playSoundOnToggle": true,
              "dspEnabled": true
            }
            """);

        var exception = Record.Exception(() => AppConfig.Load(_configPath));

        Assert.Null(exception);

        var loaded = AppConfig.Load(_configPath);

        Assert.Equal("old-device", loaded.InputDeviceId);
        Assert.Null(loaded.InputGainDb);
        Assert.Null(loaded.SaturationDrive);
        Assert.Null(loaded.Eq.HighpassHz);
        Assert.Null(loaded.Compressor.Ratio);
        Assert.Null(loaded.Gate.ThresholdDb);
        Assert.Null(loaded.Limiter.CeilingDb);
        Assert.Null(loaded.DeEsser.Hz);
    }
}
