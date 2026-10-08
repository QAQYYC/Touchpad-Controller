using System;
using System.Runtime.InteropServices;

namespace TouchpadToggle;

internal static class FnHotkey
{
    public const int FnBit = 0x10;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int LlkhfUp = 0x80;

    private static readonly HookProc Proc = Hook;
    private static IntPtr _hook;
    private static MainWindow? _window;
    private static int _modifiers;
    private static int _virtualKey;
    private static bool _armed;
    private static bool _capture;
    private static bool _fired;

    public static bool IsDown { get; private set; }

    public static bool Attach(MainWindow window, int modifiers, int virtualKey)
    {
        _window = window;
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        _armed = true;
        return EnsureHook();
    }

    public static void Disarm()
    {
        _armed = false;
        _fired = false;
        if (!_capture)
        {
            Unhook();
        }
    }

    public static void BeginCapture()
    {
        _capture = true;
        IsDown = false;
        EnsureHook();
    }

    public static void EndCapture()
    {
        _capture = false;
        if (!_armed)
        {
            Unhook();
        }
    }

    public static void Stop()
    {
        _armed = false;
        _capture = false;
        _window = null;
        IsDown = false;
        Unhook();
    }

    private static bool EnsureHook()
    {
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        _hook = SetWindowsHookEx(WhKeyboardLl, Proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    private static void Unhook()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private static IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KbdLlHook>(lParam);
            bool up = (data.Flags & LlkhfUp) != 0;
            if (IsFnScan(data))
            {
                IsDown = !up;
            }
            else if (_armed && !up && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown))
            {
                int vk = unchecked((int)data.VkCode);
                if (vk == _virtualKey && CurrentModifiers() == _modifiers)
                {
                    if (!_fired)
                    {
                        _fired = true;
                        MainWindow? window = _window;
                        window?.Dispatcher.BeginInvoke(new Action(() => window.TriggerHotkey()));
                    }

                    return (IntPtr)1;
                }
            }

            if (up && data.VkCode == (uint)_virtualKey)
            {
                _fired = false;
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static int CurrentModifiers()
    {
        int mods = 0;
        if (IsVkDown(0x11)) mods |= 2;
        if (IsVkDown(0x12)) mods |= 1;
        if (IsVkDown(0x10)) mods |= 4;
        if (IsVkDown(0x5B) || IsVkDown(0x5C)) mods |= 8;
        if (IsDown) mods |= FnBit;
        return mods;
    }

    private static bool IsVkDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static bool IsFnScan(KbdLlHook data)
    {
        if (data.VkCode == 0xFF)
        {
            return true;
        }

        uint scan = data.ScanCode & 0xFF;
        return scan is 0x63 or 0x64 or 0x68 or 0x6F;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHook
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
