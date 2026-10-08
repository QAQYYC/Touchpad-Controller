# 小米笔记本 Pro 14 触摸板开关
# Requires Windows PowerShell 5.1 on Windows 11. UTF-8 with BOM.

param(
    [switch]$Show,
    [switch]$Install,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$script:AppId = 'Xiaomi.BookPro14.TouchpadToggle'
$script:TaskName = 'XiaomiTouchpadToggle'
$script:MutexName = 'Local\XiaomiTouchpadToggle'
$script:ShowEventName = 'Local\XiaomiTouchpadToggle.Show'
$script:HotkeyId = 1
$script:DataDir = Join-Path $env:LOCALAPPDATA 'XiaomiTouchpadToggle'
$script:ModNoRepeat = 0x4000

function Test-IsWindows {
    return $env:OS -eq 'Windows_NT'
}

function Test-IsAdmin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-DataDir {
    if (-not (Test-Path -LiteralPath $script:DataDir)) {
        New-Item -ItemType Directory -Path $script:DataDir | Out-Null
    }
}

function Get-PowerShellExe {
    return Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
}

function Get-InstalledScriptPath {
    return Join-Path $script:DataDir 'TouchpadApp.ps1'
}

function Get-IconPath {
    return Join-Path $script:DataDir 'touchpad.ico'
}

function Get-DesktopShortcutPath {
    $desktop = [Environment]::GetFolderPath('Desktop')
    return Join-Path $desktop '触摸板开关.lnk'
}

function Get-StartMenuShortcutPath {
    $programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    return Join-Path $programs '触摸板开关.lnk'
}

function Add-RoundRectanglePath {
    param($Path, [single]$X, [single]$Y, [single]$Width, [single]$Height, [single]$Radius)
    $diameter = $Radius * 2
    $Path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $Path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $Path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $Path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $Path.CloseFigure()
}

function Save-AppIcon {
    Ensure-DataDir
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 64, 64
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
        Add-RoundRectanglePath $tile 2 2 60 60 16
        $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 43, 182, 115))
        $graphics.FillPath($green, $tile)
        $pad = New-Object System.Drawing.Drawing2D.GraphicsPath
        Add-RoundRectanglePath $pad 16 20 32 24 6
        $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
        $graphics.FillPath($white, $pad)
        $green.Dispose()
        $white.Dispose()
        $tile.Dispose()
        $pad.Dispose()

        $pngStream = New-Object System.IO.MemoryStream
        $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $png = $pngStream.ToArray()
        $pngStream.Dispose()

        $path = Get-IconPath
        $file = [System.IO.File]::Open($path, [System.IO.FileMode]::Create)
        try {
            $file.WriteByte(0); $file.WriteByte(0)
            $file.WriteByte(1); $file.WriteByte(0)
            $file.WriteByte(1); $file.WriteByte(0)
            $file.WriteByte(64)
            $file.WriteByte(64)
            $file.WriteByte(0)
            $file.WriteByte(0)
            $file.WriteByte(1); $file.WriteByte(0)
            $file.WriteByte(32); $file.WriteByte(0)
            $lengthBytes = [BitConverter]::GetBytes([uint32]$png.Length)
            $offsetBytes = [BitConverter]::GetBytes([uint32]22)
            $file.Write($lengthBytes, 0, 4)
            $file.Write($offsetBytes, 0, 4)
            $file.Write($png, 0, $png.Length)
        } finally {
            $file.Dispose()
        }
    } finally {
        $graphics.Dispose()
        $bmp.Dispose()
    }
}

function Ensure-ShortcutType {
    if ('ShortcutAumid' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ShortcutAumid {
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PROPERTYKEY {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pszVal;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHGetPropertyStoreFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        uint flags,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    public static void Set(string shortcutPath, string appId) {
        Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        IPropertyStore store;
        SHGetPropertyStoreFromParsingName(shortcutPath, IntPtr.Zero, 2, ref iid, out store);
        PROPERTYKEY key = new PROPERTYKEY();
        key.fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        key.pid = 5;
        PROPVARIANT pv = new PROPVARIANT();
        pv.vt = 31;
        pv.pszVal = Marshal.StringToCoTaskMemUni(appId);
        store.SetValue(ref key, ref pv);
        store.Commit();
        Marshal.FreeCoTaskMem(pv.pszVal);
    }
}
'@
}

function New-AppShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$WithAppId
    )
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = Get-PowerShellExe
    $scriptPath = Get-InstalledScriptPath
    $link.Arguments = "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$scriptPath`" -Show"
    $link.WorkingDirectory = $script:DataDir
    $link.WindowStyle = 7
    $link.Description = '触摸板开关'
    $icon = Get-IconPath
    if (Test-Path -LiteralPath $icon) {
        $link.IconLocation = "$icon,0"
    }
    $link.Save()
    if ($WithAppId) {
        Ensure-ShortcutType
        [ShortcutAumid]::Set($Path, $script:AppId)
    }
}

function Invoke-SchtasksRaw {
    param([Parameter(Mandatory = $true)][string]$Arguments)
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $env:SystemRoot 'System32\schtasks.exe'
    $info.Arguments = $Arguments
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    return [pscustomobject]@{
        Code   = $process.ExitCode
        Output = ($stdout + $stderr).Trim()
    }
}

function Test-StartupTask {
    $result = Invoke-SchtasksRaw ('/Query /TN "{0}"' -f $script:TaskName)
    return $result.Code -eq 0
}

function Register-LogonTask {
    $powershell = Get-PowerShellExe
    $scriptPath = Get-InstalledScriptPath
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        $scriptPath = $PSCommandPath
    }
    $inner = '\"{0}\" -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{1}\"' -f $powershell, $scriptPath
    $arguments = '/Create /F /TN "{0}" /SC ONLOGON /RL HIGHEST /IT /TR "{1}"' -f $script:TaskName, $inner
    $result = Invoke-SchtasksRaw $arguments
    if ($result.Code -ne 0) {
        throw "无法注册开机启动（$($result.Code)）$($result.Output)"
    }
}

function Unregister-LogonTask {
    if (Test-StartupTask) {
        Invoke-SchtasksRaw ('/Delete /F /TN "{0}"' -f $script:TaskName) | Out-Null
    }
}

function Invoke-Install {
    if (-not (Test-IsWindows)) { throw '请在 Windows 笔记本上运行安装程序。' }
    if (-not (Test-IsAdmin)) { throw '请允许管理员权限后再安装。' }
    Ensure-DataDir
    Save-AppIcon
    $destination = Get-InstalledScriptPath
    $source = [IO.Path]::GetFullPath($PSCommandPath)
    $target = [IO.Path]::GetFullPath($destination)
    if ($source -ne $target) {
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
    Register-LogonTask
    New-AppShortcut -Path (Get-DesktopShortcutPath)
    New-AppShortcut -Path (Get-StartMenuShortcutPath) -WithAppId
    Write-Host '已放到桌面，并会在登录后于后台待命。'
    Start-Process -FilePath (Get-PowerShellExe) -ArgumentList @(
        '-NoProfile', '-STA', '-WindowStyle', 'Hidden', '-ExecutionPolicy', 'Bypass',
        '-File', $destination, '-Show'
    )
}

function Stop-AppProcesses {
    $processes = Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" -ErrorAction SilentlyContinue
    foreach ($process in $processes) {
        if ($process.ProcessId -eq $PID) { continue }
        if ($process.CommandLine -and $process.CommandLine -like '*TouchpadApp.ps1*') {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-Uninstall {
    if (-not (Test-IsWindows)) { throw '请在 Windows 上运行卸载程序。' }
    if (-not (Test-IsAdmin)) { throw '请允许管理员权限后再卸载。' }
    Stop-AppProcesses
    Unregister-LogonTask
    foreach ($path in @((Get-DesktopShortcutPath), (Get-StartMenuShortcutPath))) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    if (Test-Path -LiteralPath $script:DataDir) {
        Remove-Item -LiteralPath $script:DataDir -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $script:DataDir) {
            $quoted = $script:DataDir.Replace('"', '')
            Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -WindowStyle Hidden -ArgumentList @(
                '/c', "ping 127.0.0.1 -n 2 >nul & rmdir /s /q `"$quoted`""
            )
        }
    }
    Write-Host '已卸载触摸板开关。'
}

function Convert-HexColor {
    param([Parameter(Mandatory = $true)][string]$Hex)
    return [System.Windows.Media.Color]::FromRgb(
        [Convert]::ToByte($Hex.Substring(0, 2), 16),
        [Convert]::ToByte($Hex.Substring(2, 2), 16),
        [Convert]::ToByte($Hex.Substring(4, 2), 16))
}

function Get-DefaultSettings {
    return [pscustomobject]@{
        theme      = 'system'
        modifiers  = 3
        virtualKey = 84
    }
}

function Read-Settings {
    $path = Join-Path $script:DataDir 'settings.json'
    if (-not (Test-Path -LiteralPath $path)) { return Get-DefaultSettings }
    try {
        $raw = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        $settings = Get-DefaultSettings
        if ($raw.theme -in @('light', 'dark', 'system')) { $settings.theme = [string]$raw.theme }
        if ($raw.modifiers -ge 1 -and $raw.modifiers -le 15) { $settings.modifiers = [int]$raw.modifiers }
        if ($raw.virtualKey -ge 1 -and $raw.virtualKey -le 255) { $settings.virtualKey = [int]$raw.virtualKey }
        return $settings
    } catch {
        return Get-DefaultSettings
    }
}

function Write-Settings {
    Ensure-DataDir
    $path = Join-Path $script:DataDir 'settings.json'
    $script:Settings | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
}

function Get-DeviceText {
    param($Device)
    return "$($Device.FriendlyName) $($Device.Name) $($Device.InstanceId)"
}

function Test-DeviceExcluded {
    param($Device)
    if (-not $Device) { return $true }
    $text = Get-DeviceText $Device
    if ($text -match '指纹|fingerprint|触摸屏|touch\s*screen|TouchScreen') { return $true }
    if ($text -match '6890') { return $true }
    if ($Device.InstanceId -match '^(?i)USB\\') { return $true }
    return $false
}

function Get-DeviceScore {
    param($Device)
    if (Test-DeviceExcluded $Device) { return -1 }
    $id = [string]$Device.InstanceId
    $name = "$($Device.FriendlyName) $($Device.Name)"
    $score = 0
    if ($id -match '347D' -and $id -match '7853') { $score += 100 }
    if ($id -match 'BLTP7853') { $score += 100 }
    if ($id -match 'GXTP7863') { $score += 100 }
    if ($id -match '27C6' -and $id -match '01E0') { $score += 80 }
    if ($name -match 'touch\s*pad|touchpad|触摸板') { $score += 60 }
    return $score
}

function Save-DeviceId {
    param([Parameter(Mandatory = $true)][string]$InstanceId)
    Ensure-DataDir
    Set-Content -LiteralPath (Join-Path $script:DataDir 'device.txt') -Value $InstanceId -Encoding UTF8
}

function Clear-DeviceId {
    $path = Join-Path $script:DataDir 'device.txt'
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}

function Get-SavedDevice {
    $path = Join-Path $script:DataDir 'device.txt'
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $id = (Get-Content -LiteralPath $path -Raw -Encoding UTF8 -ErrorAction SilentlyContinue)
    if ([string]::IsNullOrWhiteSpace($id)) { return $null }
    $id = $id.Trim().TrimStart([char]0xFEFF)
    $device = Get-PnpDevice -InstanceId $id -ErrorAction SilentlyContinue
    if ($device -and -not (Test-DeviceExcluded $device)) { return $device }
    return $null
}

function Resolve-TouchpadDevice {
    $saved = Get-SavedDevice
    if ($saved) { return $saved }
    $devices = @(Get-PnpDevice -Class Mouse, HIDClass -ErrorAction SilentlyContinue)
    $ranked = @(
        foreach ($device in $devices) {
            $score = Get-DeviceScore $device
            if ($score -ge 60) {
                [pscustomobject]@{ Device = $device; Score = $score }
            }
        }
    )
    if ($ranked.Count -eq 0) { return $null }
    $best = ($ranked | Sort-Object Score -Descending | Select-Object -First 1).Score
    $winners = @($ranked | Where-Object { $_.Score -eq $best })
    if ($winners.Count -ne 1) { return $null }
    Save-DeviceId $winners[0].Device.InstanceId
    return $winners[0].Device
}

function Get-FreshDevice {
    param($Device)
    if (-not $Device) { return $null }
    $fresh = Get-PnpDevice -InstanceId $Device.InstanceId -ErrorAction SilentlyContinue
    if ($fresh) { return $fresh }
    return $Device
}

function Get-PickerDevices {
    $devices = @(Get-PnpDevice -Class Mouse, HIDClass -ErrorAction SilentlyContinue)
    $rows = @(
        foreach ($device in $devices) {
            if (Test-DeviceExcluded $device) { continue }
            $score = Get-DeviceScore $device
            $name = if ($device.FriendlyName) { [string]$device.FriendlyName } else { [string]$device.Name }
            $interesting = $score -ge 60 -or $device.Class -eq 'Mouse' -or $name -match 'touch|pad|i2c|I2C|HID'
            if (-not $interesting) { continue }
            [pscustomobject]@{
                Score      = $score
                Label      = $name
                InstanceId = [string]$device.InstanceId
            }
        }
    )
    return @($rows | Sort-Object Score -Descending | Select-Object -First 40)
}

function Test-DeviceOn {
    param($Device)
    return $Device -and $Device.Status -eq 'OK'
}

function Send-Toast {
    param([Parameter(Mandatory = $true)][string]$Message)
    try {
        $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
        $escaped = [System.Security.SecurityElement]::Escape($Message)
        $xml = @"
<toast>
  <visual>
    <binding template="ToastGeneric">
      <text>触摸板</text>
      <text>$escaped</text>
    </binding>
  </visual>
  <audio src="ms-winsoundevent:Notification.Default"/>
</toast>
"@
        $document = New-Object Windows.Data.Xml.Dom.XmlDocument
        $document.LoadXml($xml)
        $toast = New-Object Windows.UI.Notifications.ToastNotification $document
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($script:AppId).Show($toast)
    } catch {
    }
}

function Get-SystemThemeName {
    try {
        $value = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name AppsUseLightTheme -ErrorAction Stop
        if ([int]$value.AppsUseLightTheme -eq 1) { return 'light' }
    } catch {
    }
    return 'dark'
}

function Get-Palette {
    param([Parameter(Mandatory = $true)][string]$Name)
    if ($Name -eq 'light') {
        return @{
            Bg      = (Convert-HexColor 'F2F2F7')
            Card    = (Convert-HexColor 'FFFFFF')
            Text    = (Convert-HexColor '1C1C1E')
            Muted   = (Convert-HexColor '6E6E73')
            Off     = (Convert-HexColor 'E5E5EA')
            On      = (Convert-HexColor '2BB673')
            Knob    = (Convert-HexColor 'FFFFFF')
            PadOff  = (Convert-HexColor 'E5E5EA')
            PadOn   = (Convert-HexColor 'D8F3E6')
            Dot     = (Convert-HexColor '2BB673')
            Slash   = (Convert-HexColor '1C1C1E')
            Chip    = (Convert-HexColor 'E5E5EA')
        }
    }
    return @{
        Bg     = (Convert-HexColor '1C1C1E')
        Card   = (Convert-HexColor '2C2C2E')
        Text   = (Convert-HexColor 'F5F5F7')
        Muted  = (Convert-HexColor 'A1A1A6')
        Off    = (Convert-HexColor '3A3A3C')
        On     = (Convert-HexColor '2BB673')
        Knob   = (Convert-HexColor 'FFFFFF')
        PadOff = (Convert-HexColor '3A3A3C')
        PadOn  = (Convert-HexColor '1E3A2C')
        Dot    = (Convert-HexColor 'F5F5F7')
        Slash  = (Convert-HexColor 'F5F5F7')
        Chip   = (Convert-HexColor '3A3A3C')
    }
}

function Set-BrushColor {
    param($Brush, [System.Windows.Media.Color]$Color)
    if ($Brush -is [System.Windows.Media.SolidColorBrush]) {
        $Brush.BeginAnimation([System.Windows.Media.SolidColorBrush]::ColorProperty, $null)
        $Brush.Color = $Color
    }
}

function Update-ThemeSegments {
    $palette = $script:Palette
    foreach ($pair in @(
            @{ Button = $script:ThemeLight; Value = 'light' },
            @{ Button = $script:ThemeDark; Value = 'dark' },
            @{ Button = $script:ThemeSystem; Value = 'system' }
        )) {
        if ($script:Settings.theme -eq $pair.Value) {
            Set-BrushColor $pair.Button.Background $palette.On
            $pair.Button.Foreground = [System.Windows.Media.Brushes]::White
        } else {
            Set-BrushColor $pair.Button.Background $palette.Chip
            $pair.Button.Foreground = New-SolidBrush $palette.Muted
        }
    }
}

function New-SolidBrush {
    param([System.Windows.Media.Color]$Color)
    $brush = New-Object System.Windows.Media.SolidColorBrush $Color
    $brush.Freeze()
    return $brush
}

function Apply-Palette {
    param([Parameter(Mandatory = $true)][string]$ResolvedName)
    $script:ResolvedTheme = $ResolvedName
    $palette = Get-Palette $ResolvedName
    $script:Palette = $palette
    $script:Window.Background = New-SolidBrush $palette.Bg
    foreach ($card in @($script:HotkeyCard, $script:StartupCard, $script:ThemeCard, $script:PickerCard)) {
        Set-BrushColor $card.Background $palette.Card
    }
    foreach ($label in @($script:TitleText, $script:StatusText, $script:HotkeyValue, $script:StartupTitle, $script:ThemeTitle)) {
        $label.Foreground = New-SolidBrush $palette.Text
    }
    foreach ($muted in @($script:HotkeyLabel, $script:HotkeyHint, $script:StartupHint, $script:PickerHint)) {
        $muted.Foreground = New-SolidBrush $palette.Muted
    }
    Set-BrushColor $script:Pad.Background $(if ($script:ShownOn) { $palette.PadOn } else { $palette.PadOff })
    Set-BrushColor $script:PadDot.Fill $palette.Dot
    Set-BrushColor $script:PadSlash.Fill $palette.Slash
    Set-BrushColor $script:Knob.Fill $palette.Knob
    Set-BrushColor $script:StartupKnob.Fill $palette.Knob
    if (-not $script:ShownOn) { Set-BrushColor $script:Track.Background $palette.Off }
    else { Set-BrushColor $script:Track.Background $palette.On }
    if ($script:StartupShown) { Set-BrushColor $script:StartupTrack.Background $palette.On }
    else { Set-BrushColor $script:StartupTrack.Background $palette.Off }
    foreach ($button in @($script:FullScreenButton, $script:MinButton, $script:CloseButton, $script:HotkeyButton, $script:RedetectButton)) {
        Set-BrushColor $button.Background $palette.Chip
        $button.Foreground = New-SolidBrush $palette.Text
    }
    $script:DeviceList.Foreground = New-SolidBrush $palette.Text
    $script:DeviceList.Background = New-SolidBrush $palette.Card
    Update-ThemeSegments
}

function Apply-ThemeChoice {
    $resolved = if ($script:Settings.theme -eq 'system') { Get-SystemThemeName } else { $script:Settings.theme }
    Apply-Palette $resolved
}

function Format-Hotkey {
    param([int]$Modifiers, [int]$VirtualKey)
    $parts = New-Object System.Collections.Generic.List[string]
    if ($Modifiers -band 2) { $parts.Add('Ctrl') }
    if ($Modifiers -band 1) { $parts.Add('Alt') }
    if ($Modifiers -band 4) { $parts.Add('Shift') }
    if ($Modifiers -band 8) { $parts.Add('Win') }
    $key = [System.Windows.Input.KeyInterop]::KeyFromVirtualKey($VirtualKey)
    $name = switch ($key) {
        'Space' { 'Space' }
        'Return' { 'Enter' }
        'Escape' { 'Esc' }
        'Tab' { 'Tab' }
        default { $key.ToString() }
    }
    $parts.Add($name)
    return ($parts -join ' + ')
}

function Register-CurrentHotkey {
    if (-not $script:Hwnd -or $script:Hwnd -eq [IntPtr]::Zero) { return $false }
    [TouchpadNative]::UnregisterHotKey($script:Hwnd, $script:HotkeyId) | Out-Null
    $mods = [uint32]($script:Settings.modifiers -bor $script:ModNoRepeat)
    $vk = [uint32]$script:Settings.virtualKey
    $ok = [TouchpadNative]::RegisterHotKey($script:Hwnd, $script:HotkeyId, $mods, $vk)
    $script:HotkeyRegistered = [bool]$ok
    return [bool]$ok
}

function Update-HotkeyText {
    $script:HotkeyValue.Text = Format-Hotkey $script:Settings.modifiers $script:Settings.virtualKey
}

function Start-HotkeyCapture {
    $script:Capturing = $true
    [TouchpadNative]::UnregisterHotKey($script:Hwnd, $script:HotkeyId) | Out-Null
    $script:HotkeyButton.Content = '请按下组合键'
    $script:HotkeyHint.Text = '至少包含 Ctrl、Alt、Shift 或 Win。按 Esc 取消'
    $script:HotkeyHint.Visibility = 'Visible'
    $script:Window.Activate() | Out-Null
    $script:Window.Focus() | Out-Null
}

function Stop-HotkeyCapture {
    param([string]$Hint)
    $script:Capturing = $false
    $script:HotkeyButton.Content = '更改快捷键'
    if ($Hint) {
        $script:HotkeyHint.Text = $Hint
        $script:HotkeyHint.Visibility = 'Visible'
    } else {
        $script:HotkeyHint.Visibility = 'Collapsed'
    }
    Register-CurrentHotkey | Out-Null
    Update-HotkeyText
}

function Test-ModifierKey {
    param($Key)
    return $Key -in @(
        'LeftCtrl', 'RightCtrl', 'LeftAlt', 'RightAlt', 'LeftShift', 'RightShift', 'LWin', 'RWin', 'System'
    )
}

function Complete-HotkeyCapture {
    param($EventArgs)
    $key = $EventArgs.Key
    if ($key -eq [System.Windows.Input.Key]::System) { $key = $EventArgs.SystemKey }
    if ($key -eq [System.Windows.Input.Key]::Escape) {
        Stop-HotkeyCapture
        return
    }
    if (Test-ModifierKey $key) { return }
    $mods = 0
    $keyboardMods = $EventArgs.KeyboardDevice.Modifiers
    if ($keyboardMods -band [System.Windows.Input.ModifierKeys]::Control) { $mods = $mods -bor 2 }
    if ($keyboardMods -band [System.Windows.Input.ModifierKeys]::Alt) { $mods = $mods -bor 1 }
    if ($keyboardMods -band [System.Windows.Input.ModifierKeys]::Shift) { $mods = $mods -bor 4 }
    if ($keyboardMods -band [System.Windows.Input.ModifierKeys]::Windows) { $mods = $mods -bor 8 }
    if ($mods -eq 0) {
        $script:HotkeyHint.Text = '请包含 Ctrl、Alt、Shift 或 Win'
        return
    }
    $virtualKey = [System.Windows.Input.KeyInterop]::VirtualKeyFromKey($key)
    if ($virtualKey -lt 1) {
        $script:HotkeyHint.Text = '这个按键不能用作快捷键'
        return
    }
    $previousMods = $script:Settings.modifiers
    $previousKey = $script:Settings.virtualKey
    $script:Settings.modifiers = $mods
    $script:Settings.virtualKey = $virtualKey
    if (Register-CurrentHotkey) {
        Write-Settings
        Stop-HotkeyCapture
    } else {
        $script:Settings.modifiers = $previousMods
        $script:Settings.virtualKey = $previousKey
        $kept = Format-Hotkey $previousMods $previousKey
        Stop-HotkeyCapture "这个快捷键已被占用，仍使用 $kept"
    }
}

function Update-DotAnimation {
    $shouldRun = $script:ShownOn -and $script:Window.IsVisible -and $script:Pad.Visibility -eq 'Visible'
    if (-not $script:DotStoryboard) { return }
    if ($shouldRun) {
        if ($script:DotStoryboard.GetCurrentState() -ne 'Active') {
            $script:DotStoryboard.Begin()
        }
    } else {
        $script:DotStoryboard.Stop()
    }
}

function Set-KnobPosition {
    param($Element, [double]$Left, [bool]$Instant, [int]$Milliseconds)
    $property = [System.Windows.Controls.Canvas]::LeftProperty
    if ($Instant) {
        $Element.BeginAnimation($property, $null)
        [System.Windows.Controls.Canvas]::SetLeft($Element, $Left)
        return
    }
    $ease = New-Object System.Windows.Media.Animation.CubicEase
    $ease.EasingMode = 'EaseInOut'
    $animation = New-Object System.Windows.Media.Animation.DoubleAnimation($Left, [TimeSpan]::FromMilliseconds($Milliseconds))
    $animation.EasingFunction = $ease
    $Element.BeginAnimation($property, $animation)
}

function Set-TrackColor {
    param($Brush, [System.Windows.Media.Color]$Color, [bool]$Instant)
    if ($Instant -or -not $Brush) {
        Set-BrushColor $Brush $Color
        return
    }
    $ease = New-Object System.Windows.Media.Animation.CubicEase
    $ease.EasingMode = 'EaseInOut'
    $animation = New-Object System.Windows.Media.Animation.ColorAnimation($Color, [TimeSpan]::FromMilliseconds(220))
    $animation.EasingFunction = $ease
    $Brush.BeginAnimation([System.Windows.Media.SolidColorBrush]::ColorProperty, $animation)
}

function Set-OpacityAnimated {
    param($Element, [double]$Opacity, [bool]$Instant)
    $property = [System.Windows.UIElement]::OpacityProperty
    if ($Instant) {
        $Element.BeginAnimation($property, $null)
        $Element.Opacity = $Opacity
        return
    }
    $ease = New-Object System.Windows.Media.Animation.CubicEase
    $ease.EasingMode = 'EaseInOut'
    $animation = New-Object System.Windows.Media.Animation.DoubleAnimation($Opacity, [TimeSpan]::FromMilliseconds(220))
    $animation.EasingFunction = $ease
    $Element.BeginAnimation($property, $animation)
}

function Update-StatusText {
    if ($script:PickerCard.Visibility -eq 'Visible') { return }
    if ($script:ShownOn -eq $true) { $script:StatusText.Text = '触摸板已开启' }
    elseif ($script:ShownOn -eq $false) { $script:StatusText.Text = '触摸板已关闭' }
    else { $script:StatusText.Text = '尚未识别触摸板' }
}

function Update-TrayMenu {
    if (-not $script:ToggleMenuItem) { return }
    if ($script:ShownOn -eq $true) {
        $script:ToggleMenuItem.Text = '关闭触摸板'
        $script:Notify.Text = '触摸板已开启'
    } elseif ($script:ShownOn -eq $false) {
        $script:ToggleMenuItem.Text = '开启触摸板'
        $script:Notify.Text = '触摸板已关闭'
    } else {
        $script:ToggleMenuItem.Text = '切换触摸板'
        $script:Notify.Text = '触摸板开关'
    }
    if ($script:StartupMenuItem) {
        $script:StartupMenuItem.Checked = [bool](Test-StartupTask)
    }
}

function Set-TouchpadVisual {
    param([bool]$On, [bool]$Instant)
    $script:ShownOn = $On
    $palette = $script:Palette
    $knobLeft = if ($On) { 44 } else { 4 }
    Set-KnobPosition $script:Knob $knobLeft $Instant 220
    $trackColor = if ($On) { $palette.On } else { $palette.Off }
    Set-TrackColor $script:Track.Background $trackColor $Instant
    $padColor = if ($On) { $palette.PadOn } else { $palette.PadOff }
    Set-TrackColor $script:Pad.Background $padColor $Instant
    Set-OpacityAnimated $script:PadSlash $(if ($On) { 0 } else { 1 }) $Instant
    Set-OpacityAnimated $script:PadDot $(if ($On) { 1 } else { 0 }) $Instant
    Update-StatusText
    Update-TrayMenu
    Update-DotAnimation
}

function Set-StartupVisual {
    param([bool]$On, [bool]$Instant)
    $script:StartupShown = $On
    $palette = $script:Palette
    $left = if ($On) { 23 } else { 3 }
    Set-KnobPosition $script:StartupKnob $left $Instant 220
    $color = if ($On) { $palette.On } else { $palette.Off }
    Set-TrackColor $script:StartupTrack.Background $color $Instant
    if ($script:StartupMenuItem) { $script:StartupMenuItem.Checked = $On }
}

function Show-Picker {
    $script:SelectingDevice = $true
    try {
        $script:DeviceList.ItemsSource = Get-PickerDevices
    } finally {
        $script:SelectingDevice = $false
    }
    $script:PickerCard.Visibility = 'Visible'
    $script:Pad.Visibility = 'Collapsed'
    $script:StatusText.Text = '请点选触摸板设备'
    Update-DotAnimation
}

function Hide-Picker {
    $script:PickerCard.Visibility = 'Collapsed'
    $script:Pad.Visibility = 'Visible'
}

function Sync-FromSystem {
    if ($script:ToggleBusy) { return }
    Apply-ThemeChoice
    $startup = Test-StartupTask
    $startupChanged = $script:StartupShown -ne $startup
    Set-StartupVisual $startup (-not $startupChanged -or $null -eq $script:StartupShown)

    $device = $null
    try { $device = Get-FreshDevice (Resolve-TouchpadDevice) } catch { $device = $null }
    if (-not $device) {
        $script:ShownOn = $null
        Show-Picker
        Update-TrayMenu
        return
    }
    Hide-Picker
    $on = Test-DeviceOn $device
    $instant = $null -eq $script:ShownOn -or $script:ShownOn -eq $on
    Set-TouchpadVisual $on $instant
}

function Invoke-TouchpadToggle {
    if ($script:ToggleBusy) { return }
    $script:ToggleBusy = $true
    try {
        if (-not (Test-IsAdmin)) {
            $script:StatusText.Text = '需要管理员权限。请重新运行安装程序。'
            Send-Toast '未能切换触摸板'
            return
        }
        $device = $null
        try { $device = Get-FreshDevice (Resolve-TouchpadDevice) } catch { $device = $null }
        if (-not $device) {
            Show-Picker
            Send-Toast '未能切换触摸板'
            return
        }
        $wasOn = Test-DeviceOn $device
        $expected = -not $wasOn
        Set-TouchpadVisual $expected $false
        if ($expected) {
            Enable-PnpDevice -InstanceId $device.InstanceId -Confirm:$false -ErrorAction Stop
        } else {
            Disable-PnpDevice -InstanceId $device.InstanceId -Confirm:$false -ErrorAction Stop
        }
        $fresh = Get-PnpDevice -InstanceId $device.InstanceId -ErrorAction SilentlyContinue
        if ((Test-DeviceOn $fresh) -ne $expected) {
            Start-Sleep -Milliseconds 300
            $fresh = Get-PnpDevice -InstanceId $device.InstanceId -ErrorAction SilentlyContinue
        }
        $actual = Test-DeviceOn $fresh
        Set-TouchpadVisual $actual $(-not ($actual -eq $expected))
        if ($actual -eq $expected) {
            Send-Toast $(if ($actual) { '触摸板已开启' } else { '触摸板已关闭' })
        } else {
            $script:StatusText.Text = '未能切换触摸板'
            Send-Toast '未能切换触摸板'
        }
    } catch {
        $script:ToggleBusy = $false
        try {
            Sync-FromSystem
        } catch {
            $script:StatusText.Text = '未能切换触摸板'
        }
        Send-Toast '未能切换触摸板'
    } finally {
        $script:ToggleBusy = $false
    }
}

function Set-StartupEnabled {
    param([bool]$Enabled)
    try {
        if ($Enabled) { Register-LogonTask } else { Unregister-LogonTask }
        Set-StartupVisual (Test-StartupTask) $false
    } catch {
        $script:StatusText.Text = '无法更改开机启动'
        Set-StartupVisual (Test-StartupTask) $true
    }
}

function Enter-Fullscreen {
    if ($script:IsFullscreen) { return }
    $script:SavedBounds = @{
        Left   = $script:Window.Left
        Top    = $script:Window.Top
        Width  = $script:Window.Width
        Height = $script:Window.Height
    }
    $screen = [System.Windows.Forms.Screen]::FromHandle($script:Hwnd)
    $bounds = $screen.Bounds
    $source = [System.Windows.PresentationSource]::FromVisual($script:Window)
    $toDip = $source.CompositionTarget.TransformFromDevice
    $origin = $toDip.Transform((New-Object System.Windows.Point $bounds.X, $bounds.Y))
    $corner = $toDip.Transform((New-Object System.Windows.Point $bounds.Right, $bounds.Bottom))
    $script:Window.WindowState = 'Normal'
    $script:Window.Left = $origin.X
    $script:Window.Top = $origin.Y
    $script:Window.Width = [Math]::Max(1, $corner.X - $origin.X)
    $script:Window.Height = [Math]::Max(1, $corner.Y - $origin.Y)
    $script:IsFullscreen = $true
    [TouchpadNative]::Fullscreen = $true
    [TouchpadNative]::SetCorner($script:Hwnd, $false)
    $script:FullScreenButton.Content = [char]0xE73F
}

function Exit-Fullscreen {
    if (-not $script:IsFullscreen) { return }
    $script:IsFullscreen = $false
    [TouchpadNative]::Fullscreen = $false
    if ($script:SavedBounds) {
        $script:Window.Left = $script:SavedBounds.Left
        $script:Window.Top = $script:SavedBounds.Top
        $script:Window.Width = $script:SavedBounds.Width
        $script:Window.Height = $script:SavedBounds.Height
    }
    [TouchpadNative]::SetCorner($script:Hwnd, $true)
    $script:FullScreenButton.Content = [char]0xE740
}

function Hide-ToTray {
    if ($script:DotStoryboard) { $script:DotStoryboard.Stop() }
    $script:Window.ShowInTaskbar = $false
    $script:Window.Hide()
}

function Show-MainWindow {
    if ($script:Opening) { return }
    $script:Opening = $true
    try {
        Sync-FromSystem
        $script:Window.ShowInTaskbar = $true
        if (-not $script:Window.IsVisible) { $script:Window.Show() }
        if ($script:Window.WindowState -eq 'Minimized') { $script:Window.WindowState = 'Normal' }
        $script:Window.Activate() | Out-Null
        [TouchpadNative]::ShowWindow($script:Hwnd, 9) | Out-Null
        [TouchpadNative]::SetForegroundWindow($script:Hwnd) | Out-Null
        Update-DotAnimation
    } finally {
        $script:Opening = $false
    }
}

function Exit-App {
    $script:AllowExit = $true
    try { [TouchpadNative]::UnregisterHotKey($script:Hwnd, $script:HotkeyId) | Out-Null } catch {}
    if ($script:DotStoryboard) { $script:DotStoryboard.Stop() }
    if ($script:Notify) {
        $script:Notify.Visible = $false
        $script:Notify.Dispose()
        $script:Notify = $null
    }
    $script:Window.Close()
    if ([System.Windows.Application]::Current) {
        [System.Windows.Application]::Current.Shutdown()
    }
}

function Ensure-DotStoryboard {
    $ease = New-Object System.Windows.Media.Animation.CubicEase
    $ease.EasingMode = 'EaseInOut'
    $duration = [TimeSpan]::FromMilliseconds(2800)
    $xAnim = New-Object System.Windows.Media.Animation.DoubleAnimation(0, 96, $duration)
    $xAnim.AutoReverse = $true
    $xAnim.RepeatBehavior = [System.Windows.Media.Animation.RepeatBehavior]::Forever
    $xAnim.EasingFunction = $ease
    $yAnim = New-Object System.Windows.Media.Animation.DoubleAnimation(0, 64, $duration)
    $yAnim.AutoReverse = $true
    $yAnim.RepeatBehavior = [System.Windows.Media.Animation.RepeatBehavior]::Forever
    $yAnim.EasingFunction = $ease
    [System.Windows.Media.Animation.Storyboard]::SetTarget($xAnim, $script:DotTransform)
    [System.Windows.Media.Animation.Storyboard]::SetTargetProperty($xAnim, (New-Object System.Windows.PropertyPath 'X'))
    [System.Windows.Media.Animation.Storyboard]::SetTarget($yAnim, $script:DotTransform)
    [System.Windows.Media.Animation.Storyboard]::SetTargetProperty($yAnim, (New-Object System.Windows.PropertyPath 'Y'))
    $storyboard = New-Object System.Windows.Media.Animation.Storyboard
    [void]$storyboard.Children.Add($xAnim)
    [void]$storyboard.Children.Add($yAnim)
    $script:DotStoryboard = $storyboard
}

function Add-NativeTypes {
    if ('TouchpadNative' -as [type]) { return }
    Add-Type -ReferencedAssemblies @('PresentationFramework', 'PresentationCore', 'WindowsBase') -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

public static class TouchpadNative {
    public static bool Fullscreen;
    public static int Border = 8;
    public static Action HotkeyPressed;
    public static Action SettingChanged;

    const int WM_HOTKEY = 0x0312;
    const int WM_NCHITTEST = 0x0084;
    const int WM_SETTINGCHANGE = 0x001A;
    const int HTCLIENT = 1;
    const int HTLEFT = 10;
    const int HTRIGHT = 11;
    const int HTTOP = 12;
    const int HTTOPLEFT = 13;
    const int HTTOPRIGHT = 14;
    const int HTBOTTOM = 15;
    const int HTBOTTOMLEFT = 16;
    const int HTBOTTOMRIGHT = 17;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWCP_DONOTROUND = 1;
    const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void SetCorner(IntPtr hwnd, bool round) {
        int pref = round ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SECURITY_ATTRIBUTES {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSDRevision,
        out IntPtr securityDescriptor,
        IntPtr securityDescriptorSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateEvent(
        ref SECURITY_ATTRIBUTES eventAttributes,
        bool manualReset,
        bool initialState,
        string name);

    static IntPtr showEventHandle = IntPtr.Zero;

    public static void CreateShowEvent(string name) {
        IntPtr descriptor;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
            "D:(A;;0x1F0003;;;WD)S:(ML;;NW;;;LW)",
            1,
            out descriptor,
            IntPtr.Zero)) {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        SECURITY_ATTRIBUTES attributes = new SECURITY_ATTRIBUTES();
        attributes.nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES));
        attributes.lpSecurityDescriptor = descriptor;
        attributes.bInheritHandle = 0;
        IntPtr handle = CreateEvent(ref attributes, false, false, name);
        if (handle == IntPtr.Zero) {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        showEventHandle = handle;
    }

    public static void Attach(IntPtr hwnd) {
        HwndSource src = HwndSource.FromHwnd(hwnd);
        src.AddHook(Hook);
    }

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if (msg == WM_HOTKEY) {
            handled = true;
            Action hotkey = HotkeyPressed;
            if (hotkey != null) hotkey();
            return IntPtr.Zero;
        }
        if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero) {
            string name = Marshal.PtrToStringUni(lParam);
            if (name == "ImmersiveColorSet") {
                Action changed = SettingChanged;
                if (changed != null) changed();
            }
            return IntPtr.Zero;
        }
        if (msg == WM_NCHITTEST && !Fullscreen) {
            int packed = lParam.ToInt32();
            int x = unchecked((short)(packed & 0xFFFF));
            int y = unchecked((short)((packed >> 16) & 0xFFFF));
            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) return IntPtr.Zero;
            bool left = x < rect.Left + Border;
            bool right = x >= rect.Right - Border;
            bool top = y < rect.Top + Border;
            bool bottom = y >= rect.Bottom - Border;
            int hit = HTCLIENT;
            if (top && left) hit = HTTOPLEFT;
            else if (top && right) hit = HTTOPRIGHT;
            else if (bottom && left) hit = HTBOTTOMLEFT;
            else if (bottom && right) hit = HTBOTTOMRIGHT;
            else if (left) hit = HTLEFT;
            else if (right) hit = HTRIGHT;
            else if (top) hit = HTTOP;
            else if (bottom) hit = HTBOTTOM;
            if (hit != HTCLIENT) {
                handled = true;
                return new IntPtr(hit);
            }
        }
        return IntPtr.Zero;
    }
}

public static class ShowListener {
    public static void Start(string name, System.Windows.Threading.Dispatcher dispatcher, Action action) {
        var signal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, name);
        var thread = new System.Threading.Thread(() => {
            while (true) {
                signal.WaitOne();
                try { dispatcher.BeginInvoke(action); }
                catch (Exception) { }
            }
        });
        thread.IsBackground = true;
        thread.Start();
    }
}
'@
}

function Initialize-Tray {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $iconPath = Get-IconPath
    if (-not (Test-Path -LiteralPath $iconPath)) { Save-AppIcon }
    $notify = New-Object System.Windows.Forms.NotifyIcon
    $notify.Icon = New-Object System.Drawing.Icon $iconPath
    $notify.Visible = $true
    $notify.Text = '触摸板开关'
    $menu = New-Object System.Windows.Forms.ContextMenuStrip
    $openItem = $menu.Items.Add('打开主窗口')
    $toggleItem = $menu.Items.Add('切换触摸板')
    $startupItem = New-Object System.Windows.Forms.ToolStripMenuItem '开机启动'
    [void]$menu.Items.Add($startupItem)
    [void]$menu.Items.Add('-')
    $exitItem = $menu.Items.Add('退出')
    $notify.ContextMenuStrip = $menu
    $openItem.Add_Click({ Show-MainWindow })
    $toggleItem.Add_Click({ Invoke-TouchpadToggle })
    $startupItem.Add_Click({ Set-StartupEnabled (-not $script:StartupMenuItem.Checked) })
    $exitItem.Add_Click({ Exit-App })
    $notify.Add_MouseUp({
            param($sender, $eventArgs)
            if ($eventArgs.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
                Show-MainWindow
            }
        })
    $script:Notify = $notify
    $script:ToggleMenuItem = $toggleItem
    $script:StartupMenuItem = $startupItem
}

function Start-ElevatedGui {
    $launch = @('-NoProfile', '-STA', '-WindowStyle', 'Hidden', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($Show) { $launch += '-Show' }
    try {
        Start-Process -FilePath (Get-PowerShellExe) -ArgumentList $launch -Verb RunAs
    } catch {
    }
}

function Start-Gui {
    if (-not (Test-IsWindows)) { throw '请在 Windows 笔记本上运行。' }
    if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
        $launch = @('-NoProfile', '-STA', '-WindowStyle', 'Hidden', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
        if ($Show) { $launch += '-Show' }
        Start-Process -FilePath (Get-PowerShellExe) -ArgumentList $launch
        return
    }
    if (-not (Test-IsAdmin)) {
        if ($Show) {
            try {
                $existing = [System.Threading.EventWaitHandle]::OpenExisting($script:ShowEventName)
                [void]$existing.Set()
                return
            } catch {
            }
        }
        Start-ElevatedGui
        return
    }

    $script:SingleInstanceMutex = New-Object System.Threading.Mutex($false, $script:MutexName)
    $ownsMutex = $false
    try {
        $ownsMutex = $script:SingleInstanceMutex.WaitOne(0)
    } catch [System.Threading.AbandonedMutexException] {
        $ownsMutex = $true
    }
    if (-not $ownsMutex) {
        if ($Show) {
            for ($attempt = 0; $attempt -lt 40; $attempt++) {
                try {
                    $existing = [System.Threading.EventWaitHandle]::OpenExisting($script:ShowEventName)
                    [void]$existing.Set()
                    return
                } catch {
                    Start-Sleep -Milliseconds 100
                }
            }
        }
        return
    }

    Add-Type -AssemblyName PresentationFramework
    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase
    $script:Application = New-Object System.Windows.Application
    $script:Application.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown
    Add-NativeTypes
    [TouchpadNative]::CreateShowEvent($script:ShowEventName)
    Ensure-DataDir
    if (-not (Test-Path -LiteralPath (Get-IconPath))) { Save-AppIcon }
    try { New-AppShortcut -Path (Get-StartMenuShortcutPath) -WithAppId } catch {}
    $script:Settings = Read-Settings
    $script:ShownOn = $null
    $script:StartupShown = $null
    $script:ToggleBusy = $false
    $script:Capturing = $false
    $script:AllowExit = $false
    $script:IsFullscreen = $false
    $script:Opening = $false

    $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="触摸板开关"
        Width="420" Height="600"
        MinWidth="360" MinHeight="520"
        WindowStyle="None"
        ResizeMode="CanResize"
        WindowStartupLocation="CenterScreen"
        Background="#1C1C1E"
        FontFamily="Segoe UI"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True"
        TextOptions.TextFormattingMode="Display">
  <Window.Resources>
    <Style x:Key="FlatButton" TargetType="Button">
      <Setter Property="Foreground" Value="#F5F5F7"/>
      <Setter Property="Background" Value="#3A3A3C"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border Background="{TemplateBinding Background}" CornerRadius="16" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="ChromeButton" TargetType="Button" BasedOn="{StaticResource FlatButton}">
      <Setter Property="Width" Value="32"/>
      <Setter Property="Height" Value="32"/>
      <Setter Property="FontFamily" Value="Segoe MDL2 Assets"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Padding" Value="0"/>
    </Style>
  </Window.Resources>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>
    <Grid x:Name="TitleBar" Grid.Row="0" Margin="20,16,20,8" Background="Transparent">
      <TextBlock x:Name="TitleText" Text="小米笔记本 Pro 14" FontSize="16" FontWeight="Light" VerticalAlignment="Center"/>
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
        <Button x:Name="FullScreenButton" Style="{StaticResource ChromeButton}" Content="&#xE740;"/>
        <Button x:Name="MinButton" Style="{StaticResource ChromeButton}" Content="&#xE921;" Margin="8,0,0,0"/>
        <Button x:Name="CloseButton" Style="{StaticResource ChromeButton}" Content="&#xE8BB;" Margin="8,0,0,0"/>
      </StackPanel>
    </Grid>
    <ScrollViewer x:Name="Scroller" Grid.Row="1" BorderThickness="0" Background="Transparent" VerticalScrollBarVisibility="Auto">
      <Grid Margin="20,8,20,20" MinHeight="{Binding ViewportHeight, ElementName=Scroller}">
        <Grid.RowDefinitions>
          <RowDefinition Height="*" MinHeight="140"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <Grid Grid.Row="0" Margin="0,0,0,16">
          <Border x:Name="Pad" CornerRadius="16" Background="#3A3A3C">
            <Grid>
              <Ellipse x:Name="PadDot" Width="12" Height="12" Fill="#F5F5F7" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="32,40,0,0" Opacity="0">
                <Ellipse.RenderTransform>
                  <TranslateTransform x:Name="DotTransform"/>
                </Ellipse.RenderTransform>
              </Ellipse>
              <Rectangle x:Name="PadSlash" Width="3" Height="88" RadiusX="1.5" RadiusY="1.5" Fill="#F5F5F7" Opacity="0" HorizontalAlignment="Center" VerticalAlignment="Center" RenderTransformOrigin="0.5,0.5">
                <Rectangle.RenderTransform>
                  <RotateTransform Angle="-42"/>
                </Rectangle.RenderTransform>
              </Rectangle>
            </Grid>
          </Border>
          <Border x:Name="PickerCard" CornerRadius="12" Background="#2C2C2E" Padding="16" Visibility="Collapsed">
            <DockPanel x:Name="PickerPanel" LastChildFill="True">
              <TextBlock DockPanel.Dock="Top" Text="选择触摸板" FontSize="15" Margin="0,0,0,6"/>
              <TextBlock x:Name="PickerHint" DockPanel.Dock="Top" TextWrapping="Wrap" FontSize="12" Margin="0,0,0,10" Text="点一项即可记住。指纹和触摸屏不会出现在这里。"/>
              <Button x:Name="RedetectButton" DockPanel.Dock="Bottom" Style="{StaticResource FlatButton}" Content="重新检测" Padding="14,8" Margin="0,12,0,0" HorizontalAlignment="Left"/>
              <ListBox x:Name="DeviceList" BorderThickness="0" Background="Transparent" DisplayMemberPath="Label">
                <ListBox.ItemContainerStyle>
                  <Style TargetType="ListBoxItem">
                    <Setter Property="Padding" Value="8,6"/>
                    <Setter Property="Margin" Value="0,2"/>
                    <Setter Property="HorizontalContentAlignment" Value="Left"/>
                    <Setter Property="Template">
                      <Setter.Value>
                        <ControlTemplate TargetType="ListBoxItem">
                          <Border x:Name="Row" Background="Transparent" CornerRadius="8" Padding="{TemplateBinding Padding}">
                            <ContentPresenter/>
                          </Border>
                          <ControlTemplate.Triggers>
                            <Trigger Property="IsSelected" Value="True">
                              <Setter TargetName="Row" Property="Background" Value="#2BB673"/>
                              <Setter Property="Foreground" Value="White"/>
                            </Trigger>
                          </ControlTemplate.Triggers>
                        </ControlTemplate>
                      </Setter.Value>
                    </Setter>
                  </Style>
                </ListBox.ItemContainerStyle>
              </ListBox>
            </DockPanel>
          </Border>
        </Grid>
        <Border x:Name="Track" Grid.Row="1" Width="84" Height="44" CornerRadius="22" Background="#3A3A3C" HorizontalAlignment="Center" Margin="0,0,0,12" Cursor="Hand">
          <Canvas>
            <Ellipse x:Name="Knob" Width="36" Height="36" Fill="White" Canvas.Left="4" Canvas.Top="4"/>
          </Canvas>
        </Border>
        <TextBlock x:Name="StatusText" Grid.Row="2" Text="正在读取触摸板状态" HorizontalAlignment="Center" FontSize="15" Margin="0,0,0,16"/>
        <Border x:Name="HotkeyCard" Grid.Row="3" CornerRadius="12" Background="#2C2C2E" Padding="16,12" Margin="0,0,0,10">
          <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>
            <Grid>
              <StackPanel VerticalAlignment="Center">
                <TextBlock x:Name="HotkeyLabel" Text="快捷键" FontSize="12"/>
                <TextBlock x:Name="HotkeyValue" Text="Ctrl + Alt + T" FontSize="15" Margin="0,4,0,0"/>
              </StackPanel>
              <Button x:Name="HotkeyButton" Style="{StaticResource FlatButton}" Content="更改快捷键" Padding="14,8" HorizontalAlignment="Right" VerticalAlignment="Center"/>
            </Grid>
            <TextBlock x:Name="HotkeyHint" Grid.Row="1" Margin="0,8,0,0" FontSize="12" TextWrapping="Wrap" Visibility="Collapsed"/>
          </Grid>
        </Border>
        <Border x:Name="StartupCard" Grid.Row="4" CornerRadius="12" Background="#2C2C2E" Padding="16,12" Margin="0,0,0,10">
          <Grid>
            <StackPanel VerticalAlignment="Center">
              <TextBlock x:Name="StartupTitle" Text="开机启动" FontSize="15"/>
              <TextBlock x:Name="StartupHint" Text="登录后留在托盘里待命" FontSize="12" Margin="0,4,0,0"/>
            </StackPanel>
            <Border x:Name="StartupTrack" Width="46" Height="26" CornerRadius="13" Background="#3A3A3C" HorizontalAlignment="Right" VerticalAlignment="Center" Cursor="Hand">
              <Canvas>
                <Ellipse x:Name="StartupKnob" Width="20" Height="20" Fill="White" Canvas.Left="3" Canvas.Top="3"/>
              </Canvas>
            </Border>
          </Grid>
        </Border>
        <Border x:Name="ThemeCard" Grid.Row="5" CornerRadius="12" Background="#2C2C2E" Padding="16,12">
          <StackPanel>
            <TextBlock x:Name="ThemeTitle" Text="外观" FontSize="15" Margin="0,0,0,10"/>
            <UniformGrid Columns="3">
              <Button x:Name="ThemeLight" Style="{StaticResource FlatButton}" Content="浅色" Padding="8,8" Margin="0,0,6,0"/>
              <Button x:Name="ThemeDark" Style="{StaticResource FlatButton}" Content="深色" Padding="8,8" Margin="3,0,3,0"/>
              <Button x:Name="ThemeSystem" Style="{StaticResource FlatButton}" Content="适应系统" Padding="8,8" Margin="6,0,0,0"/>
            </UniformGrid>
          </StackPanel>
        </Border>
      </Grid>
    </ScrollViewer>
  </Grid>
</Window>
'@

    $script:Window = [System.Windows.Markup.XamlReader]::Parse($xaml)
    foreach ($name in @(
            'TitleBar', 'TitleText', 'FullScreenButton', 'MinButton', 'CloseButton', 'Scroller',
            'Pad', 'PadDot', 'DotTransform', 'PadSlash', 'PickerCard', 'PickerPanel', 'PickerHint',
            'RedetectButton', 'DeviceList', 'Track', 'Knob', 'StatusText', 'HotkeyCard', 'HotkeyLabel',
            'HotkeyValue', 'HotkeyButton', 'HotkeyHint', 'StartupCard', 'StartupTitle', 'StartupHint',
            'StartupTrack', 'StartupKnob', 'ThemeCard', 'ThemeTitle', 'ThemeLight', 'ThemeDark', 'ThemeSystem'
        )) {
        $element = $script:Window.FindName($name)
        if (-not $element) { throw "界面缺少 $name" }
        Set-Variable -Name $name -Scope Script -Value $element
    }

    $script:Track.Background = New-Object System.Windows.Media.SolidColorBrush (Convert-HexColor '3A3A3C')
    $script:Pad.Background = New-Object System.Windows.Media.SolidColorBrush (Convert-HexColor '3A3A3C')
    $script:Knob.Fill = New-Object System.Windows.Media.SolidColorBrush (Convert-HexColor 'FFFFFF')
    $script:StartupTrack.Background = New-Object System.Windows.Media.SolidColorBrush (Convert-HexColor '3A3A3C')
    $script:StartupKnob.Fill = New-Object System.Windows.Media.SolidColorBrush (Convert-HexColor 'FFFFFF')
    $script:PadDot.Fill = New-Object System.Windows.Media.SolidColorBrush (Convert-HexColor 'F5F5F7')
    $script:PadSlash.Fill = New-Object System.Windows.Media.SolidColorBrush (Convert-HexColor 'F5F5F7')
    foreach ($card in @($script:HotkeyCard, $script:StartupCard, $script:ThemeCard, $script:PickerCard)) {
        $card.Background = New-Object System.Windows.Media.SolidColorBrush (Convert-HexColor '2C2C2E')
    }
    foreach ($button in @($script:FullScreenButton, $script:MinButton, $script:CloseButton, $script:HotkeyButton, $script:RedetectButton, $script:ThemeLight, $script:ThemeDark, $script:ThemeSystem)) {
        $button.Background = New-Object System.Windows.Media.SolidColorBrush (Convert-HexColor '3A3A3C')
    }

    $iconUri = New-Object Uri (Get-IconPath)
    $script:Window.Icon = [System.Windows.Media.Imaging.BitmapFrame]::Create($iconUri)
    Ensure-DotStoryboard

    $helper = New-Object System.Windows.Interop.WindowInteropHelper $script:Window
    $script:Hwnd = $helper.EnsureHandle()
    [TouchpadNative]::SetCorner($script:Hwnd, $true)
    [TouchpadNative]::Attach($script:Hwnd)
    $script:HotkeyAction = { Invoke-TouchpadToggle }
    $script:ThemeAction = {
        if ($script:Settings.theme -eq 'system' -and -not $script:ToggleBusy) { Apply-ThemeChoice }
    }
    [TouchpadNative]::HotkeyPressed = [System.Action]$script:HotkeyAction
    [TouchpadNative]::SettingChanged = [System.Action]$script:ThemeAction
    Register-CurrentHotkey | Out-Null
    Update-HotkeyText

    $script:TitleBar.Add_MouseLeftButtonDown({
            param($sender, $eventArgs)
            if ($script:IsFullscreen) { return }
            $node = $eventArgs.OriginalSource -as [System.Windows.DependencyObject]
            while ($node -and -not ($node -is [System.Windows.Controls.Button])) {
                $node = [System.Windows.Media.VisualTreeHelper]::GetParent($node)
            }
            if ($node -is [System.Windows.Controls.Button]) { return }
            try { $script:Window.DragMove() } catch {}
        })
    $script:FullScreenButton.Add_Click({
            if ($script:IsFullscreen) { Exit-Fullscreen } else { Enter-Fullscreen }
        })
    $script:MinButton.Add_Click({ Hide-ToTray })
    $script:CloseButton.Add_Click({ Hide-ToTray })
    $script:Window.Add_Closing({
            param($sender, $eventArgs)
            if (-not $script:AllowExit) {
                $eventArgs.Cancel = $true
                Hide-ToTray
            }
        })
    $script:Window.Add_PreviewKeyDown({
            param($sender, $eventArgs)
            if ($script:Capturing) {
                $eventArgs.Handled = $true
                Complete-HotkeyCapture $eventArgs
                return
            }
            if ($eventArgs.Key -eq [System.Windows.Input.Key]::Escape -and $script:IsFullscreen) {
                Exit-Fullscreen
                $eventArgs.Handled = $true
            }
        })
    $script:Window.Add_IsVisibleChanged({ Update-DotAnimation })
    $script:Pad.Add_SizeChanged({
            $height = [Math]::Max(48, $script:Pad.ActualHeight * 0.62)
            $script:PadSlash.Height = $height
        })
    $script:Track.Add_PreviewMouseLeftButtonUp({ Invoke-TouchpadToggle })
    $script:StartupTrack.Add_PreviewMouseLeftButtonUp({
            Set-StartupEnabled (-not (Test-StartupTask))
        })
    $script:HotkeyButton.Add_Click({
            if ($script:Capturing) { Stop-HotkeyCapture } else { Start-HotkeyCapture }
        })
    $script:ThemeLight.Add_Click({
            $script:Settings.theme = 'light'
            Write-Settings
            Apply-ThemeChoice
        })
    $script:ThemeDark.Add_Click({
            $script:Settings.theme = 'dark'
            Write-Settings
            Apply-ThemeChoice
        })
    $script:ThemeSystem.Add_Click({
            $script:Settings.theme = 'system'
            Write-Settings
            Apply-ThemeChoice
        })
    $script:RedetectButton.Add_Click({
            Clear-DeviceId
            $script:ShownOn = $null
            Sync-FromSystem
        })
    $script:DeviceList.Add_SelectionChanged({
            if ($script:SelectingDevice) { return }
            $item = $script:DeviceList.SelectedItem
            if (-not $item) { return }
            Save-DeviceId $item.InstanceId
            $script:ShownOn = $null
            Sync-FromSystem
        })

    Initialize-Tray
    [ShowListener]::Start($script:ShowEventName, $script:Application.Dispatcher, [System.Action]{ Show-MainWindow })
    $script:Application.Add_Exit({
            if ($script:SingleInstanceMutex) {
                try { $script:SingleInstanceMutex.ReleaseMutex() } catch {}
                $script:SingleInstanceMutex.Dispose()
            }
        })
    $script:Application.Add_Startup({
            if ($Show) { Show-MainWindow } else { Update-TrayMenu }
        })
    [void]$script:Application.Run()
}

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch {}

if (-not (Test-IsWindows)) {
    Write-Host '这个程序需要在 Windows 11 的小米笔记本 Pro 14 上运行。'
    exit 1
}

if ($Install) {
    try {
        Invoke-Install
        exit 0
    } catch {
        Write-Host $_.Exception.Message
        exit 1
    }
}

if ($Uninstall) {
    try {
        Invoke-Uninstall
        exit 0
    } catch {
        Write-Host $_.Exception.Message
        exit 1
    }
}

try {
    Start-Gui
    exit 0
} catch {
    try {
        Ensure-DataDir
        $line = '{0} {1}' -f (Get-Date).ToString('s'), $_.Exception.Message
        Add-Content -LiteralPath (Join-Path $script:DataDir 'error.log') -Value $line -Encoding UTF8
    } catch {}
    Write-Host $_.Exception.Message
    exit 1
}
