using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchpadToggle;

public sealed class AppSettings
{
    public string Theme { get; set; } = "system";
    public int Modifiers { get; set; } = 3;
    public int VirtualKey { get; set; } = 0x54;
    public bool StartWithWindows { get; set; } = true;
}

internal static class AppPaths
{
    public const string AppId = "Xiaomi.BookPro14.TouchpadToggle";
    public const string TaskName = "XiaomiTouchpadToggle";
    public const string ShowEventName = "Local\\XiaomiTouchpadToggle.Show";
    public const string AckEventName = "Local\\XiaomiTouchpadToggle.Ack";
    public const string MutexName = "Local\\XiaomiTouchpadToggle";

    public static string Exe => Environment.ProcessPath ?? "";

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XiaomiTouchpadToggle");

    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string DevicePath => Path.Combine(DataDir, "device.txt");

    public static string DesktopExe => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "触摸板开关.exe");

    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", "触摸板开关.lnk");
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
            {
                return new AppSettings();
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath), JsonOptions) ?? new AppSettings();
            if (loaded.Theme is not ("light" or "dark" or "system"))
            {
                loaded.Theme = "system";
            }

            if (loaded.Modifiers is < 1 or > 31)
            {
                loaded.Modifiers = 3;
            }

            if (loaded.VirtualKey is < 1 or > 255)
            {
                loaded.VirtualKey = 0x54;
            }

            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static void DeleteData()
    {
        if (Directory.Exists(AppPaths.DataDir))
        {
            Directory.Delete(AppPaths.DataDir, true);
        }
    }
}
