using System;
using System.Runtime.InteropServices;

namespace TouchpadToggle;

internal static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const int WmNcHitTest = 0x0084;
    public const int WmSettingChange = 0x001A;
    public const int HtClient = 1;
    public const int HtLeft = 10;
    public const int HtRight = 11;
    public const int HtTop = 12;
    public const int HtTopLeft = 13;
    public const int HtTopRight = 14;
    public const int HtBottom = 15;
    public const int HtBottomLeft = 16;
    public const int HtBottomRight = 17;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;
    public const int HotkeyId = 1;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RectNative lpRect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    public const uint Infinite = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static void SetCorner(IntPtr hwnd, bool round)
    {
        int pref = round ? 2 : 1;
        DwmSetWindowAttribute(hwnd, 33, ref pref, 4);
    }

    public static int HitTest(IntPtr hwnd, IntPtr lParam, int border)
    {
        int packed = lParam.ToInt32();
        int x = unchecked((short)(packed & 0xFFFF));
        int y = unchecked((short)((packed >> 16) & 0xFFFF));
        if (!GetWindowRect(hwnd, out RectNative rect))
        {
            return HtClient;
        }

        bool left = x < rect.Left + border;
        bool right = x >= rect.Right - border;
        bool top = y < rect.Top + border;
        bool bottom = y >= rect.Bottom - border;
        if (top && left) return HtTopLeft;
        if (top && right) return HtTopRight;
        if (bottom && left) return HtBottomLeft;
        if (bottom && right) return HtBottomRight;
        if (left) return HtLeft;
        if (right) return HtRight;
        if (top) return HtTop;
        if (bottom) return HtBottom;
        return HtClient;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out IntPtr securityDescriptor,
        IntPtr securityDescriptorSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(
        ref SecurityAttributes eventAttributes,
        bool manualReset,
        bool initialState,
        string name);

    public static IntPtr showEventHandle = IntPtr.Zero;

    public static void CreateShowEvent(string name)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                "D:(A;;0x1F0003;;;WD)S:(ML;;NW;;;LW)",
                1,
                out IntPtr descriptor,
                IntPtr.Zero))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            SecurityDescriptor = descriptor,
            InheritHandle = 0
        };
        IntPtr handle = CreateEvent(ref attributes, false, false, name);
        if (handle == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        showEventHandle = handle;
    }
}
