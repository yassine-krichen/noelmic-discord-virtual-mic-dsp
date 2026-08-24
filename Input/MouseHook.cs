using System.Runtime.InteropServices;

namespace NoelMic.Input;

/// <summary>
/// Extra mouse buttons that can be bound to the mute toggle, matching the
/// Win32 HIWORD(mouseData) convention (XBUTTON1 = 1, XBUTTON2 = 2).
/// </summary>
public enum ToggleMouseButton
{
    XButton1 = 1,
    XButton2 = 2,
}

/// <summary>
/// Global low-level mouse hook that raises <see cref="ToggleRequested"/> when
/// the configured toggle button is pressed, regardless of which window has
/// focus. The owning thread's message pump (e.g. <c>Application.Run()</c>)
/// drives hook delivery — this class only installs/uninstalls the hook.
/// </summary>
public sealed class MouseHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const long DebounceMilliseconds = 150;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // Kept alive as a field: SetWindowsHookEx only holds a native function
    // pointer, so a delegate that's only a local/lambda is GC-eligible and its
    // collection causes an intermittent crash inside the hook callback.
    private readonly LowLevelMouseProc _proc;

    private IntPtr _hookHandle = IntPtr.Zero;
    private long _lastToggleTicks;

    // Plain field, not volatile: SetToggleButton and the hook callback both
    // only ever run on the STA thread that owns the message pump.
    private ToggleMouseButton _toggleButton;

    public event EventHandler? ToggleRequested;

    public MouseHook(ToggleMouseButton toggleButton)
    {
        _toggleButton = toggleButton;
        _proc = HookCallback;
    }

    public void SetToggleButton(ToggleMouseButton button)
    {
        _toggleButton = button;
    }

    public void Install()
    {
        // hMod must be IntPtr.Zero for an in-process WH_MOUSE_LL hook —
        // passing a module handle here is only correct for out-of-process hooks.
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    internal bool IsToggleButton(uint mouseData) => (ushort)(mouseData >> 16) == (int)_toggleButton;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // An exception escaping across the P/Invoke boundary crashes the
        // process (or corrupts hook chain state for other apps), so every
        // path must fall through to CallNextHookEx regardless.
        try
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_XBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                if (IsToggleButton(hookStruct.mouseData))
                {
                    var now = Environment.TickCount64;
                    if (now - _lastToggleTicks >= DebounceMilliseconds)
                    {
                        _lastToggleTicks = now;
                        ToggleRequested?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }
        catch
        {
            // Swallow — see method comment.
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
