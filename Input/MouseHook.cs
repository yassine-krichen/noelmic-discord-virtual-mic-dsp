using System.Runtime.InteropServices;

namespace NoelMic.Input;

/// <summary>
/// Global low-level mouse hook that raises <see cref="ToggleRequested"/> when
/// the configured toggle button (XButton1) is pressed, regardless of which
/// window has focus.
/// </summary>
public sealed class MouseHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_QUIT = 0x0012;
    private const int ToggleButton = 1; // XBUTTON1, per Win32 HIWORD(mouseData) convention
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // Kept alive as a field: SetWindowsHookEx only holds a native function
    // pointer, so a delegate that's only a local/lambda is GC-eligible and its
    // collection causes an intermittent crash inside the hook callback.
    private readonly LowLevelMouseProc _proc;

    private IntPtr _hookHandle = IntPtr.Zero;
    private uint _hookThreadId;
    private long _lastToggleTicks;

    public event EventHandler? ToggleRequested;

    public MouseHook()
    {
        _proc = HookCallback;
    }

    public void Run()
    {
        _hookThreadId = GetCurrentThreadId();

        // hMod must be IntPtr.Zero for an in-process WH_MOUSE_LL hook —
        // passing a module handle here is only correct for out-of-process hooks.
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);

        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) != 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }

    public void Stop()
    {
        PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

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
                var xButton = (ushort)(hookStruct.mouseData >> 16);

                if (xButton == ToggleButton)
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
