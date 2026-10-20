using System.Text.Json;
using System.Text.Json.Serialization;

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

    // Deprecated / migration-only: the old A/B bypass toggle, superseded by
    // Preset == "Raw". Load() migrates any config that still has this set to
    // false onto the "Raw" preset the first time it's loaded — kept only so
    // that migration has something to read.
    public bool DspEnabled { get; set; } = true;

    // "Raw" | "Natural" | "Broadcast" | "Warm" | "Bright" — see
    // PresetDefinitions.Names. Unknown/garbage values fall back to
    // "Broadcast" at resolve time, not here, so a hand-typo'd config.json
    // never blocks startup.
    //
    // Nullable with no initializer so Load() can tell "preset key absent
    // from JSON" (a pre-preset config, null) apart from "explicitly set" —
    // a default initializer would mask the absent case, since
    // System.Text.Json leaves properties missing from the JSON at their
    // constructed default rather than resetting them to null. Load()
    // always normalizes this to a non-null value before returning, so
    // nothing downstream ever has to handle null.
    public string? Preset { get; set; }

    // Nullable so that "not set" (use the preset's value) is distinguishable
    // from "explicitly set to the preset's value" — a non-nullable field with
    // a default initializer can't tell those apart.
    public float? InputGainDb { get; set; }

    public float? SaturationDrive { get; set; }

    // Nullable for the same "unset means use the preset's value" reason as
    // InputGainDb/SaturationDrive above.
    public bool? NoiseSuppressionEnabled { get; set; }

    public EqConfig Eq { get; set; } = new();

    public CompressorConfig Compressor { get; set; } = new();

    public GateConfig Gate { get; set; } = new();

    public LimiterConfig Limiter { get; set; } = new();

    public DeEsserConfig DeEsser { get; set; } = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
            // A brand-new install, not a migration — write the preset
            // explicitly so this config never gets mistaken for a
            // pre-preset legacy file on a later load.
            var defaults = new AppConfig { Preset = "Broadcast" };

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
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
            var migrated = false;

            // Migrates old configs that used the DspEnabled bypass flag onto
            // the new preset system. Also flips DspEnabled back to true, so
            // this is one-shot: without it, the condition would still be
            // true on every future load and would keep stomping the user's
            // Preset choice back to "Raw" forever.
            if (!loaded.DspEnabled)
            {
                loaded.DspEnabled = true;
                loaded.Preset = "Raw";
                migrated = true;
            }

            // A config with DSP values but no "preset" key was written by
            // the pre-preset version, which auto-wrote concrete EQ/
            // compressor/gate values for every user. Those aren't a
            // deliberate override — under the new override-beats-preset
            // semantics they'd permanently pin the old (bad) curve on top
            // of whatever preset the user picks. Clear them so the preset
            // drives instead. Skipped if the DspEnabled branch above
            // already assigned "Raw", since Raw bypasses DSP entirely and
            // that branch's outcome wins.
            if (loaded.Preset is null)
            {
                loaded.Eq.HighpassHz = null;
                loaded.Eq.MudCutHz = null;
                loaded.Eq.MudCutDb = null;
                loaded.Eq.PresenceHz = null;
                loaded.Eq.PresenceBoostDb = null;
                loaded.Compressor.Ratio = null;
                loaded.Compressor.ThresholdDb = null;
                loaded.Compressor.MakeupGainDb = null;
                loaded.Gate.ThresholdDb = null;
                loaded.Gate.HysteresisDb = null;
                loaded.Gate.HoldMs = null;
                loaded.Preset = "Broadcast";
                migrated = true;
            }

            if (migrated)
            {
                try
                {
                    loaded.Save(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Failing to persist the migration must never prevent
                    // startup — the in-memory migration above still applies
                    // for this run, it just won't be sticky yet.
                    Console.WriteLine($"Failed to persist migrated config.json ({ex.GetType().Name}: {ex.Message}). Continuing with in-memory migration.");
                }
            }

            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or inaccessible config file must never prevent startup —
            // fall back to in-memory defaults instead of crashing.
            Console.WriteLine($"Failed to load config.json ({ex.GetType().Name}: {ex.Message}). Using defaults.");
            return new AppConfig { Preset = "Broadcast" };
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
    public float? HighpassHz { get; set; }

    public float? LowShelfHz { get; set; }

    public float? LowShelfDb { get; set; }

    public float? MudCutHz { get; set; }

    public float? MudCutDb { get; set; }

    public float? MudCutQ { get; set; }

    public float? PresenceHz { get; set; }

    public float? PresenceBoostDb { get; set; }

    public float? PresenceQ { get; set; }

    public float? AirHz { get; set; }

    public float? AirDb { get; set; }
}

public sealed class CompressorConfig
{
    public double? Ratio { get; set; }

    public double? ThresholdDb { get; set; }

    public double? MakeupGainDb { get; set; }

    public float? AttackMs { get; set; }

    public float? ReleaseMs { get; set; }

    public double? MixPercent { get; set; }
}

public sealed class GateConfig
{
    public double? ThresholdDb { get; set; }

    public double? HysteresisDb { get; set; }

    public float? HoldMs { get; set; }

    public float? AttackMs { get; set; }

    public float? ReleaseMs { get; set; }
}

public sealed class LimiterConfig
{
    public float? CeilingDb { get; set; }

    public float? AttackMs { get; set; }

    public float? ReleaseMs { get; set; }
}

public sealed class DeEsserConfig
{
    public float? Hz { get; set; }

    public double? ThresholdDb { get; set; }

    public double? Ratio { get; set; }

    public float? MaxReductionDb { get; set; }
}
