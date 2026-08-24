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
            DspEnabled = false,
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
}
