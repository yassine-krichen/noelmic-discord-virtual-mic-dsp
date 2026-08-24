using System.Text.Json;

namespace NoelMic.Config;

/// <summary>
/// Root application configuration, persisted as JSON at
/// <c>%APPDATA%\NoelMic\config.json</c>.
/// </summary>
public sealed class AppConfig
{
    public string InputDeviceId { get; set; } = "";

    public string ToggleButton { get; set; } = "XButton1";

    public bool StartWithWindows { get; set; } = true;

    public bool PlaySoundOnToggle { get; set; } = true;

    public EqConfig Eq { get; set; } = new();

    public CompressorConfig Compressor { get; set; } = new();

    public GateConfig Gate { get; set; } = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NoelMic",
        "config.json");

    // Optional path override exists so tests can round-trip against a temp
    // file instead of the real %APPDATA%\NoelMic\config.json — the default
    // matches production behavior exactly.
    public static AppConfig Load(string? path = null)
    {
        path ??= ConfigPath;

        if (!File.Exists(path))
        {
            var defaults = new AppConfig();

            try
            {
                defaults.Save(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Failing to persist defaults must never prevent startup —
                // continue with in-memory defaults instead of crashing.
                Console.WriteLine($"Failed to save default config.json ({ex.GetType().Name}: {ex.Message}). Using in-memory defaults.");
            }

            return defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or inaccessible config file must never prevent startup —
            // fall back to in-memory defaults instead of crashing.
            Console.WriteLine($"Failed to load config.json ({ex.GetType().Name}: {ex.Message}). Using defaults.");
            return new AppConfig();
        }
    }

    public void Save(string? path = null)
    {
        path ??= ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, SerializerOptions);
        File.WriteAllText(path, json);
    }
}

public sealed class EqConfig
{
    public float HighpassHz { get; set; } = 90;

    public float PresenceBoostDb { get; set; } = 3;
}

public sealed class CompressorConfig
{
    public double Ratio { get; set; } = 3.0;

    public double ThresholdDb { get; set; } = -18;
}

public sealed class GateConfig
{
    public double ThresholdDb { get; set; } = -45;
}
