using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace TouchpadToggle;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SolidColorBrush _bg = BrushOf("1C1C1E");
    private readonly SolidColorBrush _card = BrushOf("2C2C2E");
    private readonly SolidColorBrush _text = BrushOf("F5F5F7");
    private readonly SolidColorBrush _muted = BrushOf("A1A1A6");
    private readonly SolidColorBrush _chip = BrushOf("3A3A3C");
    private readonly SolidColorBrush _track = BrushOf("3A3A3C");
    private readonly SolidColorBrush _startupTrack = BrushOf("3A3A3C");
    private readonly SolidColorBrush _pad = BrushOf("3A3A3C");
    private readonly SolidColorBrush _knob = BrushOf("FFFFFF");
    private readonly SolidColorBrush _startupKnob = BrushOf("FFFFFF");
    private readonly SolidColorBrush _dot = BrushOf("F5F5F7");
    private readonly SolidColorBrush _slash = BrushOf("F5F5F7");
    private readonly SolidColorBrush _themeLight = BrushOf("3A3A3C");
    private readonly SolidColorBrush _themeDark = BrushOf("3A3A3C");
    private readonly SolidColorBrush _themeSystem = BrushOf("3A3A3C");

    private IntPtr _hwnd;
    private HwndSource? _source;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _toggleItem;
    private Forms.ToolStripMenuItem? _startupItem;
    private Storyboard? _dotStoryboard;
    private bool? _shownOn;
    private bool? _startupShown;
    private bool _toggleBusy;
    private bool _capturing;
    private bool _fullscreen;
    private bool _allowExit;
    private bool _opening;
    private bool _fillingList;
    private bool _prepared;
    private Rect _restore;
    private MediaColor _onColor;
    private MediaColor _offColor;
    private MediaColor _padOnColor;
    private MediaColor _padOffColor;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        AssignBrushes();
        ApplyTheme();
        HotkeyValue.Text = FormatHotkey(_settings.Modifiers, _settings.VirtualKey);
        Pad.SizeChanged += (_, _) => PadSlash.Height = Math.Max(48, Pad.ActualHeight * 0.62);
        EnsureDotStoryboard();
    }

    public void Prepare()
    {
        if (_prepared)
        {
            return;
        }

        _prepared = true;
        BindHandle();
        try
        {
            using System.Drawing.Icon? associated = System.Drawing.Icon.ExtractAssociatedIcon(AppPaths.Exe);
            if (associated != null)
            {
                Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(associated.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
        }
        catch
        {
        }

        CreateTray();
        bool startup = false;
        try { startup = StartupTask.Exists(); } catch { }
        SetStartupVisual(startup, true);
    }

    public void ReleaseNative()
    {
        _allowExit = true;
        try { NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HotkeyId); } catch { }
        _dotStoryboard?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }

    public void ShowFromUser()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ShowFromUser));
            return;
        }

        try
        {
            Reveal();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        if (_opening || _toggleBusy)
        {
            return;
        }

        _opening = true;
        _ = SyncAfterRevealAsync();
    }

    private void Reveal()
    {
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            Show();
        }

        BindHandle();
        NativeMethods.ForceForeground(_hwnd);
        Activate();
        NativeMethods.SignalAck();
        UpdateDot();
    }

    private void BindHandle()
    {
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero)
        {
            hwnd = helper.EnsureHandle();
        }

        if (hwnd == _hwnd && _source != null)
        {
            NativeMethods.AllowShowMessage(hwnd);
            return;
        }

        _source?.RemoveHook(Hook);
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(Hook);
        NativeMethods.AllowShowMessage(_hwnd);
        NativeMethods.SetCorner(_hwnd, !_fullscreen);
        RegisterHotkey();
    }

    private async Task SyncAfterRevealAsync()
    {
        try
        {
            await SyncAsync();
            UpdateDot();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText.Text = "未能读取触摸板状态";
        }
        finally
        {
            _opening = false;
        }
    }

    private async Task SyncAsync()
    {
        if (_toggleBusy)
        {
            return;
        }

        ApplyTheme();
        bool startup = false;
        try { startup = StartupTask.Exists(); } catch { }
        SetStartupVisual(startup, _startupShown == null || _startupShown == startup);

        if (_shownOn == null)
        {
            StatusText.Text = "正在读取触摸板状态";
        }

        TouchpadDevice? device = null;
        bool timedOut = false;
        try
        {
            Task<TouchpadDevice?> query = Task.Run(DeviceService.Resolve);
            if (await Task.WhenAny(query, Task.Delay(TimeSpan.FromSeconds(8))) != query)
            {
                timedOut = true;
            }
            else
            {
                device = await query;
            }
        }
        catch
        {
            device = null;
        }

        if (timedOut)
        {
            StatusText.Text = "读取触摸板超时";
            UpdateTray();
            return;
        }

        if (device == null)
        {
            _shownOn = null;
            await ShowPickerAsync();
            UpdateTray();
            return;
        }

        HidePicker();
        bool instant = _shownOn == null || _shownOn == device.IsEnabled;
        SetTouchpadVisual(device.IsEnabled, instant);
    }

    private async Task ShowPickerAsync()
    {
        _fillingList = true;
        try
        {
            Task<System.Collections.Generic.List<TouchpadDevice>> query = Task.Run(DeviceService.ListCandidates);
            if (await Task.WhenAny(query, Task.Delay(TimeSpan.FromSeconds(8))) != query)
            {
                StatusText.Text = "读取设备超时";
                PickerCard.Visibility = Visibility.Visible;
                Pad.Visibility = Visibility.Collapsed;
                UpdateDot();
                return;
            }

            DeviceList.ItemsSource = await query;
        }
        finally
        {
            _fillingList = false;
        }

        PickerCard.Visibility = Visibility.Visible;
        Pad.Visibility = Visibility.Collapsed;
        StatusText.Text = "请点选触摸板设备";
        UpdateDot();
    }

    private void HidePicker()
    {
        PickerCard.Visibility = Visibility.Collapsed;
        Pad.Visibility = Visibility.Visible;
    }

    private async void OnToggleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ToggleAsync();
    }

    private async Task ToggleAsync()
    {
        if (_toggleBusy)
        {
            return;
        }

        _toggleBusy = true;
        try
        {
            TouchpadDevice? device = await Task.Run(DeviceService.Resolve);
            if (device == null)
            {
                await ShowPickerAsync();
                ToastService.Show("未能切换触摸板");
                return;
            }

            bool expected = !device.IsEnabled;
            SetTouchpadVisual(expected, false);
            bool ok = await Task.Run(() => DeviceService.TrySetEnabled(device.InstanceId, expected, out bool actual) && actual == expected);
            bool actualState = ok ? expected : await ReadActualAsync(device.InstanceId, expected);
            SetTouchpadVisual(actualState, actualState == expected);
            if (ok && actualState == expected)
            {
                ToastService.Show(actualState ? "触摸板已开启" : "触摸板已关闭");
            }
            else
            {
                StatusText.Text = "未能切换触摸板";
                ToastService.Show("未能切换触摸板");
            }
        }
        catch
        {
            _toggleBusy = false;
            try { await SyncAsync(); } catch { StatusText.Text = "未能切换触摸板"; }
            ToastService.Show("未能切换触摸板");
        }
        finally
        {
            _toggleBusy = false;
        }
    }

    private static async Task<bool> ReadActualAsync(string instanceId, bool fallback)
    {
        TouchpadDevice? fresh = await Task.Run(() =>
        {
            foreach (TouchpadDevice candidate in DeviceService.ListCandidates())
            {
                if (candidate.InstanceId == instanceId)
                {
                    return candidate;
                }
            }

            return DeviceService.Resolve();
        });
        return fresh == null ? fallback : fresh.IsEnabled;
    }

    private void OnStartupClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleStartup();
    }

    private void ToggleStartup()
    {
        bool enable = _startupShown != true;
        try
        {
            if (enable)
            {
                StartupTask.RegisterCurrentExe();
                _settings.StartWithWindows = true;
            }
            else
            {
                StartupTask.Remove();
                _settings.StartWithWindows = false;
            }

            SettingsStore.Save(_settings);
            SetStartupVisual(StartupTask.Exists(), false);
        }
        catch
        {
            StatusText.Text = "无法更改开机启动";
            SetStartupVisual(StartupTask.Exists(), true);
        }
    }

    private async void OnRedetectClick(object sender, RoutedEventArgs e)
    {
        DeviceService.ClearId();
        _shownOn = null;
        await SyncAsync();
    }

    private async void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingList || DeviceList.SelectedItem is not TouchpadDevice device)
        {
            return;
        }

        DeviceService.SaveId(device.InstanceId);
        _shownOn = null;
        await SyncAsync();
    }

    private void OnThemeLight(object sender, RoutedEventArgs e) => ChooseTheme("light");
    private void OnThemeDark(object sender, RoutedEventArgs e) => ChooseTheme("dark");
    private void OnThemeSystem(object sender, RoutedEventArgs e) => ChooseTheme("system");

    private void ChooseTheme(string theme)
    {
        _settings.Theme = theme;
        SettingsStore.Save(_settings);
        ApplyTheme();
        if (_shownOn != null)
        {
            SetTouchpadVisual(_shownOn.Value, true);
        }

        if (_startupShown != null)
        {
            SetStartupVisual(_startupShown.Value, true);
        }
    }

    private void OnHotkeyButton(object sender, RoutedEventArgs e)
    {
        if (_capturing)
        {
            StopCapture(null);
            return;
        }

        _capturing = true;
        NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HotkeyId);
        HotkeyButton.Content = "请按下组合键";
        HotkeyHint.Text = "至少包含 Ctrl、Alt、Shift 或 Win。按 Esc 取消";
        HotkeyHint.Visibility = Visibility.Visible;
        Activate();
        Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing)
        {
            e.Handled = true;
            CompleteCapture(e);
            return;
        }

        if (e.Key == Key.Escape && _fullscreen)
        {
            ExitFullscreen();
            e.Handled = true;
        }
    }

    private void CompleteCapture(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopCapture(null);
            return;
        }

        if (IsModifier(key))
        {
            return;
        }

        int mods = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= 2;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= 1;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= 4;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods |= 8;
        if (mods == 0)
        {
            HotkeyHint.Text = "请包含 Ctrl、Alt、Shift 或 Win";
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk < 1)
        {
            HotkeyHint.Text = "这个按键不能用作快捷键";
            return;
        }

        int previousMods = _settings.Modifiers;
        int previousKey = _settings.VirtualKey;
        _settings.Modifiers = mods;
        _settings.VirtualKey = vk;
        if (RegisterHotkey())
        {
            SettingsStore.Save(_settings);
            StopCapture(null);
            return;
        }

        _settings.Modifiers = previousMods;
        _settings.VirtualKey = previousKey;
        StopCapture("这个快捷键已被占用，仍使用 " + FormatHotkey(previousMods, previousKey));
    }

    private void StopCapture(string? hint)
    {
        _capturing = false;
        HotkeyButton.Content = "更改快捷键";
        if (string.IsNullOrEmpty(hint))
        {
            HotkeyHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            HotkeyHint.Text = hint;
            HotkeyHint.Visibility = Visibility.Visible;
        }

        RegisterHotkey();
        HotkeyValue.Text = FormatHotkey(_settings.Modifiers, _settings.VirtualKey);
    }

    private bool RegisterHotkey()
    {
        NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HotkeyId);
        return NativeMethods.RegisterHotKey(
            _hwnd,
            NativeMethods.HotkeyId,
            (uint)(_settings.Modifiers | (int)NativeMethods.ModNoRepeat),
            (uint)_settings.VirtualKey);
    }

    private void OnFullScreenClick(object sender, RoutedEventArgs e)
    {
        if (_fullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        _restore = new Rect(Left, Top, Width, Height);
        Forms.Screen screen = Forms.Screen.FromHandle(_hwnd);
        System.Drawing.Rectangle bounds = screen.Bounds;
        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
        {
            return;
        }

        Matrix toDip = source.CompositionTarget.TransformFromDevice;
        Point origin = toDip.Transform(new Point(bounds.X, bounds.Y));
        Point corner = toDip.Transform(new Point(bounds.Right, bounds.Bottom));
        WindowState = WindowState.Normal;
        Left = origin.X;
        Top = origin.Y;
        Width = Math.Max(1, corner.X - origin.X);
        Height = Math.Max(1, corner.Y - origin.Y);
        _fullscreen = true;
        NativeMethods.SetCorner(_hwnd, false);
        FullScreenButton.Content = "\uE73F";
    }

    private void ExitFullscreen()
    {
        if (!_fullscreen)
        {
            return;
        }

        _fullscreen = false;
        Left = _restore.Left;
        Top = _restore.Top;
        Width = _restore.Width;
        Height = _restore.Height;
        NativeMethods.SetCorner(_hwnd, true);
        FullScreenButton.Content = "\uE740";
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => HideToTray();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        _dotStoryboard?.Stop();
        ShowInTaskbar = false;
        Hide();
        BindHandle();
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateDot();

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (_fullscreen || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node != null && node is not Button)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        if (node is Button)
        {
            return;
        }

        try { DragMove(); } catch { }
    }

    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(AppPaths.Exe) ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "触摸板开关"
        };
        var menu = new Forms.ContextMenuStrip();
        Forms.ToolStripMenuItem open = new("打开主窗口");
        _toggleItem = new Forms.ToolStripMenuItem("切换触摸板");
        _startupItem = new Forms.ToolStripMenuItem("开机启动");
        Forms.ToolStripMenuItem exit = new("退出");
        open.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromUser));
        _toggleItem.Click += async (_, _) => await ToggleAsync();
        _startupItem.Click += (_, _) => ToggleStartup();
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(open);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        _tray.ContextMenuStrip = menu;
        _tray.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.BeginInvoke(new Action(ShowFromUser));
            }
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.BeginInvoke(new Action(ShowFromUser));
            }
        };
        UpdateTray();
    }

    private void ExitApp()
    {
        ReleaseNative();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmShowMain)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(ShowFromUser));
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WmHotkey)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(() => _ = ToggleAsync()));
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WmSettingChange && lParam != IntPtr.Zero)
        {
            string? name = MarshalString(lParam);
            if (name == "ImmersiveColorSet" && _settings.Theme == "system")
            {
                Dispatcher.BeginInvoke(new Action(ApplyTheme));
            }
        }

        if (msg == NativeMethods.WmNcHitTest && !_fullscreen)
        {
            int hit = NativeMethods.HitTest(hwnd, lParam, 8);
            if (hit != NativeMethods.HtClient)
            {
                handled = true;
                return new IntPtr(hit);
            }
        }

        return IntPtr.Zero;
    }

    private static string? MarshalString(IntPtr pointer)
    {
        try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(pointer); }
        catch { return null; }
    }

    private void ApplyTheme()
    {
        bool light = _settings.Theme switch
        {
            "light" => true,
            "dark" => false,
            _ => SystemUsesLight()
        };
        _onColor = Hex("2BB673");
        _offColor = light ? Hex("E5E5EA") : Hex("3A3A3C");
        _padOnColor = light ? Hex("D8F3E6") : Hex("1E3A2C");
        _padOffColor = _offColor;
        SetColor(_bg, light ? Hex("F2F2F7") : Hex("1C1C1E"));
        SetColor(_card, light ? Hex("FFFFFF") : Hex("2C2C2E"));
        SetColor(_text, light ? Hex("1C1C1E") : Hex("F5F5F7"));
        SetColor(_muted, light ? Hex("6E6E73") : Hex("A1A1A6"));
        SetColor(_chip, _offColor);
        SetColor(_knob, Hex("FFFFFF"));
        SetColor(_startupKnob, Hex("FFFFFF"));
        SetColor(_dot, light ? _onColor : Hex("F5F5F7"));
        SetColor(_slash, light ? Hex("1C1C1E") : Hex("F5F5F7"));
        SetColor(_pad, _shownOn == true ? _padOnColor : _padOffColor);
        SetColor(_track, _shownOn == true ? _onColor : _offColor);
        SetColor(_startupTrack, _startupShown == true ? _onColor : _offColor);
        PaintSegment(_themeLight, _settings.Theme == "light");
        PaintSegment(_themeDark, _settings.Theme == "dark");
        PaintSegment(_themeSystem, _settings.Theme == "system");
    }

    private void PaintSegment(SolidColorBrush brush, bool selected)
    {
        SetColor(brush, selected ? _onColor : _offColor);
        if (ReferenceEquals(brush, _themeLight)) ThemeLight.Foreground = selected ? Brushes.White : _muted;
        if (ReferenceEquals(brush, _themeDark)) ThemeDark.Foreground = selected ? Brushes.White : _muted;
        if (ReferenceEquals(brush, _themeSystem)) ThemeSystem.Foreground = selected ? Brushes.White : _muted;
    }

    private void SetTouchpadVisual(bool on, bool instant)
    {
        _shownOn = on;
        MoveKnob(Knob, on ? 44 : 4, instant);
        AnimateColor(_track, on ? _onColor : _offColor, instant);
        AnimateColor(_pad, on ? _padOnColor : _padOffColor, instant);
        AnimateOpacity(PadSlash, on ? 0 : 1, instant);
        AnimateOpacity(PadDot, on ? 1 : 0, instant);
        if (PickerCard.Visibility != Visibility.Visible)
        {
            StatusText.Text = on ? "触摸板已开启" : "触摸板已关闭";
        }

        UpdateTray();
        UpdateDot();
    }

    private void SetStartupVisual(bool on, bool instant)
    {
        _startupShown = on;
        MoveKnob(StartupKnob, on ? 23 : 3, instant);
        AnimateColor(_startupTrack, on ? _onColor : _offColor, instant);
        if (_startupItem != null)
        {
            _startupItem.Checked = on;
        }
    }

    private void UpdateTray()
    {
        if (_tray == null || _toggleItem == null)
        {
            return;
        }

        if (_shownOn == true)
        {
            _toggleItem.Text = "关闭触摸板";
            _tray.Text = "触摸板已开启";
        }
        else if (_shownOn == false)
        {
            _toggleItem.Text = "开启触摸板";
            _tray.Text = "触摸板已关闭";
        }
        else
        {
            _toggleItem.Text = "切换触摸板";
            _tray.Text = "触摸板开关";
        }
    }

    private void UpdateDot()
    {
        if (_dotStoryboard == null)
        {
            return;
        }

        bool run = _shownOn == true && IsVisible && Pad.Visibility == Visibility.Visible;
        if (run)
        {
            if (_dotStoryboard.GetCurrentState() != ClockState.Active)
            {
                _dotStoryboard.Begin();
            }
        }
        else
        {
            _dotStoryboard.Stop();
        }
    }

    private void EnsureDotStoryboard()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(2800);
        var x = new DoubleAnimation(0, 96, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        var y = new DoubleAnimation(0, 64, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        Storyboard.SetTarget(x, DotTransform);
        Storyboard.SetTargetProperty(x, new PropertyPath(TranslateTransform.XProperty));
        Storyboard.SetTarget(y, DotTransform);
        Storyboard.SetTargetProperty(y, new PropertyPath(TranslateTransform.YProperty));
        _dotStoryboard = new Storyboard();
        _dotStoryboard.Children.Add(x);
        _dotStoryboard.Children.Add(y);
    }

    private static void MoveKnob(UIElement knob, double left, bool instant)
    {
        if (instant)
        {
            knob.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(knob, left);
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var animation = new DoubleAnimation(left, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        knob.BeginAnimation(Canvas.LeftProperty, animation);
    }

    private static void AnimateColor(SolidColorBrush brush, MediaColor color, bool instant)
    {
        if (instant)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = color;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var animation = new ColorAnimation(color, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private static void AnimateOpacity(UIElement element, double opacity, bool instant)
    {
        if (instant)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = opacity;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var animation = new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void AssignBrushes()
    {
        Background = _bg;
        foreach (Border card in new[] { HotkeyCard, StartupCard, ThemeCard, PickerCard })
        {
            card.Background = _card;
        }

        TitleText.Foreground = _text;
        StatusText.Foreground = _text;
        HotkeyValue.Foreground = _text;
        StartupTitle.Foreground = _text;
        ThemeTitle.Foreground = _text;
        PickerTitle.Foreground = _text;
        HotkeyLabel.Foreground = _muted;
        HotkeyHint.Foreground = _muted;
        StartupHint.Foreground = _muted;
        PickerHint.Foreground = _muted;
        Pad.Background = _pad;
        PadDot.Fill = _dot;
        PadSlash.Fill = _slash;
        Track.Background = _track;
        Knob.Fill = _knob;
        StartupTrack.Background = _startupTrack;
        StartupKnob.Fill = _startupKnob;
        foreach (Button button in new[] { FullScreenButton, MinButton, CloseButton, HotkeyButton, RedetectButton })
        {
            button.Background = _chip;
            button.Foreground = _text;
        }

        ThemeLight.Background = _themeLight;
        ThemeDark.Background = _themeDark;
        ThemeSystem.Background = _themeSystem;
        DeviceList.Foreground = _text;
        DeviceList.Background = _card;
    }

    private static void SetColor(SolidColorBrush brush, MediaColor color)
    {
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = color;
    }

    private static SolidColorBrush BrushOf(string hex)
    {
        var brush = new SolidColorBrush(Hex(hex));
        return brush;
    }

    private static MediaColor Hex(string hex)
    {
        return MediaColor.FromRgb(
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    private static bool SystemUsesLight()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatHotkey(int modifiers, int virtualKey)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 8) != 0) parts.Add("Win");
        Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
        parts.Add(key switch
        {
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Tab => "Tab",
            _ => key.ToString()
        });
        return string.Join(" + ", parts);
    }

    private static bool IsModifier(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
    }
}
