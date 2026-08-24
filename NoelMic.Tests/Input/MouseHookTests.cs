using NoelMic.Input;

namespace NoelMic.Tests.Input;

public class MouseHookTests
{
    // HIWORD(mouseData) carries the XBUTTON code; LOWORD is unused for
    // XBUTTONDOWN and left zero here to mirror real hook payloads.
    private static uint MouseDataFor(ToggleMouseButton button) => (uint)((int)button << 16);

    [Fact]
    public void IsToggleButton_WithMatchingXButton1Data_ReturnsTrue()
    {
        var hook = new MouseHook(ToggleMouseButton.XButton1);

        Assert.True(hook.IsToggleButton(MouseDataFor(ToggleMouseButton.XButton1)));
    }

    [Fact]
    public void IsToggleButton_WithMatchingXButton2Data_ReturnsTrue()
    {
        var hook = new MouseHook(ToggleMouseButton.XButton2);

        Assert.True(hook.IsToggleButton(MouseDataFor(ToggleMouseButton.XButton2)));
    }

    [Fact]
    public void IsToggleButton_WithOtherConfiguredButtonData_ReturnsFalse()
    {
        var hook = new MouseHook(ToggleMouseButton.XButton1);

        Assert.False(hook.IsToggleButton(MouseDataFor(ToggleMouseButton.XButton2)));
    }

    [Fact]
    public void IsToggleButton_WithUnrelatedMouseData_ReturnsFalse()
    {
        var hook = new MouseHook(ToggleMouseButton.XButton1);

        Assert.False(hook.IsToggleButton(0));
        Assert.False(hook.IsToggleButton(0x00030000));
    }

    [Fact]
    public void SetToggleButton_ChangesWhichButtonIsMatched()
    {
        var hook = new MouseHook(ToggleMouseButton.XButton1);

        hook.SetToggleButton(ToggleMouseButton.XButton2);

        Assert.True(hook.IsToggleButton(MouseDataFor(ToggleMouseButton.XButton2)));
        Assert.False(hook.IsToggleButton(MouseDataFor(ToggleMouseButton.XButton1)));
    }
}
