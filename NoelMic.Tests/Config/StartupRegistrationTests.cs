using Microsoft.Win32;
using NoelMic.Config;

namespace NoelMic.Tests.Config;

public class StartupRegistrationTests : IDisposable
{
    // A throwaway subkey under HKCU so these tests never touch the real
    // Run key that controls the user's actual Windows startup behavior.
    private readonly string _testKeyPath = @"SOFTWARE\NoelMic.Tests-" + Guid.NewGuid();

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_testKeyPath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void SetEnabled_True_CreatesValueMatchingCurrentProcessPath()
    {
        StartupRegistration.SetEnabled(true, _testKeyPath);

        using var key = Registry.CurrentUser.OpenSubKey(_testKeyPath);
        Assert.NotNull(key);
        Assert.Equal(Environment.ProcessPath, key!.GetValue("NoelMic"));
    }

    [Fact]
    public void SetEnabled_FalseAfterTrue_RemovesValue()
    {
        StartupRegistration.SetEnabled(true, _testKeyPath);

        StartupRegistration.SetEnabled(false, _testKeyPath);

        using var key = Registry.CurrentUser.OpenSubKey(_testKeyPath);
        Assert.NotNull(key);
        Assert.Null(key!.GetValue("NoelMic"));
    }

    [Fact]
    public void SetEnabled_FalseWithNoExistingValue_DoesNotThrow()
    {
        var exception = Record.Exception(() => StartupRegistration.SetEnabled(false, _testKeyPath));

        Assert.Null(exception);
    }
}
