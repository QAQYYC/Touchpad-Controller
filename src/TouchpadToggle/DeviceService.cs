using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;

namespace TouchpadToggle;

internal sealed class TouchpadDevice
{
    public string Name { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public bool IsEnabled { get; init; }
    public int Score { get; init; }
}

internal static class DeviceService
{
    public static TouchpadDevice? Resolve()
    {
        TouchpadDevice? saved = ReadSaved();
        if (saved != null)
        {
            return saved;
        }

        List<TouchpadDevice> ranked = new();
        foreach (TouchpadDevice device in QueryClassDevices())
        {
            if (device.Score >= 60)
            {
                ranked.Add(device);
            }
        }

        if (ranked.Count == 0)
        {
            return null;
        }

        ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
        int best = ranked[0].Score;
        int winners = 0;
        foreach (TouchpadDevice device in ranked)
        {
            if (device.Score == best)
            {
                winners++;
            }
        }

        if (winners != 1)
        {
            return null;
        }

        SaveId(ranked[0].InstanceId);
        return ranked[0];
    }

    public static List<TouchpadDevice> ListCandidates()
    {
        List<TouchpadDevice> rows = new();
        foreach (TouchpadDevice device in QueryClassDevices())
        {
            bool interesting = device.Score >= 60
                || device.ClassName.Equals("Mouse", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(device.Name, "touch|pad|i2c|hid", RegexOptions.IgnoreCase);
            if (!interesting || IsExcluded(device.Name, device.InstanceId))
            {
                continue;
            }

            rows.Add(device);
        }

        rows.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (rows.Count > 40)
        {
            rows.RemoveRange(40, rows.Count - 40);
        }

        return rows;
    }

    public static bool TrySetEnabled(string instanceId, bool enabled, out bool actual)
    {
        actual = false;
        try
        {
            using ManagementObject? device = FindById(instanceId);
            if (device == null)
            {
                return false;
            }

            device.InvokeMethod(enabled ? "Enable" : "Disable", null);
            Thread.Sleep(300);
            TouchpadDevice? fresh = FindModel(instanceId);
            actual = fresh?.IsEnabled ?? false;
            return actual == enabled;
        }
        catch
        {
            TouchpadDevice? fresh = FindModel(instanceId);
            actual = fresh?.IsEnabled ?? false;
            return false;
        }
    }

    public static void SaveId(string instanceId)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        File.WriteAllText(AppPaths.DevicePath, instanceId);
    }

    public static void ClearId()
    {
        if (File.Exists(AppPaths.DevicePath))
        {
            File.Delete(AppPaths.DevicePath);
        }
    }

    private static TouchpadDevice? ReadSaved()
    {
        if (!File.Exists(AppPaths.DevicePath))
        {
            return null;
        }

        string id = File.ReadAllText(AppPaths.DevicePath).Trim().TrimStart('\uFEFF');
        if (id.Length == 0)
        {
            return null;
        }

        TouchpadDevice? device = FindModel(id);
        if (device == null || IsExcluded(device.Name, device.InstanceId))
        {
            return null;
        }

        return device;
    }

    private static TouchpadDevice? FindModel(string instanceId)
    {
        using ManagementObject? device = FindById(instanceId);
        return device == null ? null : ToModel(device);
    }

    private static ManagementObject? FindById(string instanceId)
    {
        string escaped = instanceId.Replace("\\", "\\\\").Replace("'", "\\'");
        using ManagementObjectSearcher searcher = CreateSearcher(
            $"SELECT Name, PNPDeviceID, Status, ConfigManagerErrorCode, PNPClass FROM Win32_PnPEntity WHERE PNPDeviceID = '{escaped}'");
        foreach (ManagementObject device in searcher.Get())
        {
            return device;
        }

        return null;
    }

    private static List<TouchpadDevice> QueryClassDevices()
    {
        List<TouchpadDevice> list = new();
        using ManagementObjectSearcher searcher = CreateSearcher(
            "SELECT Name, PNPDeviceID, Status, ConfigManagerErrorCode, PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'Mouse' OR PNPClass = 'HIDClass'");
        foreach (ManagementObject device in searcher.Get())
        {
            using (device)
            {
                TouchpadDevice model = ToModel(device);
                if (!IsExcluded(model.Name, model.InstanceId))
                {
                    list.Add(model);
                }
            }
        }

        return list;
    }

    private static ManagementObjectSearcher CreateSearcher(string query)
    {
        var scope = new ManagementScope(@"\\.\root\cimv2");
        scope.Options.EnablePrivileges = true;
        return new ManagementObjectSearcher(scope, new ObjectQuery(query));
    }

    private static TouchpadDevice ToModel(ManagementObject device)
    {
        string name = device["Name"] as string ?? "";
        string id = device["PNPDeviceID"] as string ?? "";
        string className = device["PNPClass"] as string ?? "";
        string status = device["Status"] as string ?? "";
        int code = device["ConfigManagerErrorCode"] == null ? 0 : Convert.ToInt32(device["ConfigManagerErrorCode"]);
        bool enabled = status.Equals("OK", StringComparison.OrdinalIgnoreCase) && code == 0;
        return new TouchpadDevice
        {
            Name = name,
            InstanceId = id,
            ClassName = className,
            IsEnabled = enabled,
            Score = Score(name, id)
        };
    }

    public static int Score(string name, string id)
    {
        if (IsExcluded(name, id))
        {
            return -1;
        }

        int score = 0;
        if (id.Contains("347D", StringComparison.OrdinalIgnoreCase) && id.Contains("7853", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (id.Contains("BLTP7853", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (id.Contains("GXTP7863", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (id.Contains("27C6", StringComparison.OrdinalIgnoreCase) && id.Contains("01E0", StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        if (Regex.IsMatch(name, "touch\\s*pad|touchpad|触摸板", RegexOptions.IgnoreCase))
        {
            score += 60;
        }

        return score;
    }

    public static bool IsExcluded(string name, string id)
    {
        string text = name + " " + id;
        if (Regex.IsMatch(text, "指纹|fingerprint|触摸屏|touch\\s*screen", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (text.Contains("6890", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase);
    }
}
