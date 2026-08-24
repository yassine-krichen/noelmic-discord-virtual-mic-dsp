using Microsoft.Win32;

namespace NoelMic.Config;

/// <summary>
/// Registers or unregisters NoelMic to launch on Windows sign-in via the
/// per-user HKCU Run key — no elevation and no Shell COM interop required,
/// unlike a startup-folder shortcut.
/// </summary>
public static class StartupRegistration
{
    private const string DefaultRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NoelMic";

    // Optional path override exists so tests can round-trip against a
    // throwaway registry key instead of the real Run key — the default
    // matches production behavior exactly, same idiom as AppConfig.Load/Save.
    public static void SetEnabled(bool enabled, string? runKeyPathOverride = null)
    {
        var runKeyPath = runKeyPathOverride ?? DefaultRunKeyPath;

        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(runKeyPath);

        if (enabled)
        {
            // Environment.ProcessPath (not Assembly.Location) resolves correctly
            // under self-contained single-file publish, this project's packaging target.
            key.SetValue(ValueName, Environment.ProcessPath ?? string.Empty);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
