using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace TouchpadToggle;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private MainWindow? _window;
    private Thread? _signalThread;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };

        bool install = Has(e.Args, "--install");
        bool uninstall = Has(e.Args, "--uninstall");
        bool background = Has(e.Args, "--background");
        bool show = !background || install;

        if (!Admin.IsElevated())
        {
            if (!uninstall && show && SingleInstance.TryWakeAndWait())
            {
                Shutdown();
                return;
            }

            try
            {
                NativeMethods.AllowSetForegroundWindow(unchecked((uint)-1));
                Admin.RelaunchElevated(string.Join(" ", e.Args), uninstall);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
            }
            catch (Exception ex)
            {
                Log(ex);
                MessageBox.Show("需要管理员权限才能打开主窗口。", "触摸板开关");
            }

            Shutdown();
            return;
        }

        if (uninstall)
        {
            ProcessControl.StopOtherCopies();
            try { StartupTask.Remove(); } catch { }
            ShortcutService.Remove();
            SettingsStore.DeleteData();
            Shutdown();
            return;
        }

        if (install && !SamePath(AppPaths.Exe, AppPaths.DesktopExe))
        {
            try
            {
                File.Copy(AppPaths.Exe, AppPaths.DesktopExe, true);
            }
            catch
            {
                if (show && SingleInstance.TryWakeAndWait())
                {
                    Shutdown();
                    return;
                }

                Shutdown();
                return;
            }

            ProcessControl.StopOtherCopies();
            Process.Start(new ProcessStartInfo(AppPaths.DesktopExe) { UseShellExecute = true });
            Shutdown();
            return;
        }

        if (show)
        {
            ProcessControl.StopOtherCopies();
        }

        if (!AcquireMutex())
        {
            if (show)
            {
                SingleInstance.TryWakeAndWait();
            }

            Shutdown();
            return;
        }

        try
        {
            NativeMethods.CreateIpcEvents();
        }
        catch (Exception ex)
        {
            Log(ex);
        }

        AppSettings settings = SettingsStore.Load();
        if (settings.StartWithWindows)
        {
            try { StartupTask.RegisterCurrentExe(); } catch (Exception ex) { Log(ex); }
        }

        ShortcutService.EnsureStartMenu();
        try
        {
            _window = new MainWindow(settings);
            _window.Prepare();
            StartSignalLoop();
            if (show)
            {
                _window.ShowFromUser();
            }
        }
        catch (Exception ex)
        {
            Log(ex);
            if (show)
            {
                MessageBox.Show("主窗口无法打开。\n" + ex.Message, "触摸板开关");
            }

            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _signalThread = null;
        if (_window != null)
        {
            _window.ReleaseNative();
        }

        ReleaseMutex();
        base.OnExit(e);
    }

    private void StartSignalLoop()
    {
        IntPtr handle = NativeMethods.showEventHandle;
        if (handle == IntPtr.Zero || _window == null)
        {
            return;
        }

        MainWindow window = _window;
        _signalThread = new Thread(() =>
        {
            while (true)
            {
                NativeMethods.WaitForSingleObject(handle, NativeMethods.Infinite);
                window.Dispatcher.BeginInvoke(new Action(window.ShowFromUser));
            }
        })
        {
            IsBackground = true,
            Name = "TouchpadShow"
        };
        _signalThread.Start();
    }

    private bool AcquireMutex()
    {
        _mutex = new Mutex(false, AppPaths.MutexName);
        for (int i = 0; i < 40; i++)
        {
            try
            {
                if (_mutex.WaitOne(0))
                {
                    _ownsMutex = true;
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private void ReleaseMutex()
    {
        if (_mutex == null)
        {
            return;
        }

        try
        {
            if (_ownsMutex)
            {
                _mutex.ReleaseMutex();
                _ownsMutex = false;
            }
        }
        catch
        {
        }

        _mutex.Dispose();
        _mutex = null;
    }

    private static bool Has(string[] args, string flag)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    internal static void Log(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "error.log"), DateTime.Now.ToString("s") + " " + exception.Message + Environment.NewLine);
        }
        catch
        {
        }
    }
}
