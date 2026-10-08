using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace TouchpadToggle;

internal static class Admin
{
    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void RelaunchElevated(string arguments, bool wait)
    {
        using Process? process = Process.Start(new ProcessStartInfo(AppPaths.Exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = arguments
        });
        if (wait)
        {
            process?.WaitForExit();
        }
    }
}

internal static class SingleInstance
{
    public static bool TrySignal()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(AppPaths.ShowEventName);
            handle.Set();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryWakeAndWait()
    {
        try
        {
            using EventWaitHandle ack = EventWaitHandle.OpenExisting(AppPaths.AckEventName);
            ack.Reset();
            NativeMethods.AllowSetForegroundWindow(unchecked((uint)-1));
            NativeMethods.PostShowMain();
            TrySignal();
            return ack.WaitOne(1200);
        }
        catch
        {
            return false;
        }
    }
}

internal static class StartupTask
{
    public static bool Exists()
    {
        return Run($"/Query /TN \"{AppPaths.TaskName}\"").Code == 0;
    }

    public static void RegisterCurrentExe()
    {
        string exe = AppPaths.Exe;
        string tr = $"\\\"{exe}\\\" --background";
        string arguments = $"/Create /F /TN \"{AppPaths.TaskName}\" /SC ONLOGON /RL HIGHEST /IT /TR \"{tr}\"";
        (int code, string output) = Run(arguments);
        if (code != 0)
        {
            throw new InvalidOperationException("无法注册开机启动。" + output);
        }
    }

    public static void Remove()
    {
        if (Exists())
        {
            Run($"/Delete /F /TN \"{AppPaths.TaskName}\"");
        }
    }

    private static (int Code, string Output) Run(string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 schtasks。");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }
}

internal static class ToastService
{
    public static void Show(string message)
    {
        try
        {
            string escaped = System.Security.SecurityElement.Escape(message) ?? message;
            string xml = "<toast><visual><binding template=\"ToastGeneric\"><text>触摸板</text><text>"
                + escaped
                + "</text></binding></visual><audio src=\"ms-winsoundevent:Notification.Default\"/></toast>";
            var document = new Windows.Data.Xml.Dom.XmlDocument();
            document.LoadXml(xml);
            var toast = new Windows.UI.Notifications.ToastNotification(document);
            Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(AppPaths.AppId).Show(toast);
        }
        catch
        {
        }
    }
}

internal static class ShortcutService
{
    public static void EnsureStartMenu()
    {
        try
        {
            string path = AppPaths.StartMenuShortcut;
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = AppPaths.Exe;
            link.Arguments = "";
            link.WorkingDirectory = Path.GetDirectoryName(AppPaths.Exe) ?? "";
            link.WindowStyle = 1;
            link.Description = "触摸板开关";
            link.IconLocation = AppPaths.Exe + ",0";
            link.Save();
            ShortcutAumid.Set(path, AppPaths.AppId);
        }
        catch
        {
        }
    }

    public static void Remove()
    {
        if (File.Exists(AppPaths.StartMenuShortcut))
        {
            File.Delete(AppPaths.StartMenuShortcut);
        }
    }
}

internal static class ShortcutAumid
{
    public static void Set(string shortcutPath, string appId)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        Native.SHGetPropertyStoreFromParsingName(shortcutPath, IntPtr.Zero, 2, ref iid, out IPropertyStore store);
        var key = new PropertyKey
        {
            FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            PropertyId = 5
        };
        var value = new PropVariant
        {
            Vt = 31,
            Pointer = Marshal.StringToCoTaskMemUni(appId)
        };
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Vt;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    private static class Native
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHGetPropertyStoreFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindContext,
            uint flags,
            ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    }
}

internal static class ProcessControl
{
    public static void StopOtherCopies()
    {
        int current = Environment.ProcessId;
        string name = Process.GetCurrentProcess().ProcessName;
        foreach (Process process in Process.GetProcessesByName(name))
        {
            try
            {
                if (process.Id != current)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch
            {
            }
        }

        Thread.Sleep(200);
    }
}
