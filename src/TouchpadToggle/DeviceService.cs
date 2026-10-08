using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TouchpadToggle;

internal sealed class TouchpadDevice
{
    public string Name { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public Guid ClassGuid { get; init; }
    public uint DevInst { get; init; }
    public bool IsEnabled { get; init; }
    public int Score { get; init; }
}

internal static class DeviceService
{
    private const uint DigcfPresent = 0x02;
    private const uint DigcfAllClasses = 0x04;
    private const uint SpdrpDeviceDesc = 0;
    private const uint SpdrpHardwareId = 1;
    private const uint SpdrpClass = 7;
    private const uint SpdrpFriendlyName = 12;
    private const uint DifPropertyChange = 0x12;
    private const uint DicsEnable = 1;
    private const uint DicsDisable = 2;
    private const uint DicsFlagGlobal = 1;
    private const uint CmDisablePersist = 0x08;
    private const uint CmDisableUiNotOk = 0x04;
    private const int CmProbDisabled = 22;
    private static readonly IntPtr InvalidHandle = new(-1);

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
        List<TouchpadDevice> winners = ranked.FindAll(device => device.Score == best);
        TouchpadDevice chosen = winners[0];
        if (winners.Count != 1)
        {
            TouchpadDevice? mouse = winners.Find(device => device.ClassName.Equals("Mouse", StringComparison.OrdinalIgnoreCase));
            int mouseCount = 0;
            foreach (TouchpadDevice device in winners)
            {
                if (device.ClassName.Equals("Mouse", StringComparison.OrdinalIgnoreCase))
                {
                    mouseCount++;
                }
            }

            if (mouse == null || mouseCount != 1)
            {
                return null;
            }

            chosen = mouse;
        }

        SaveId(chosen.InstanceId);
        return chosen;
    }

    public static List<TouchpadDevice> ListCandidates()
    {
        List<TouchpadDevice> rows = new();
        foreach (TouchpadDevice device in QueryClassDevices())
        {
            bool interesting = device.Score >= 60
                || device.ClassName.Equals("Mouse", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(device.Name, "touch|pad|i2c|hid|goodix", RegexOptions.IgnoreCase);
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
            TouchpadDevice? device = FindModel(instanceId);
            if (device == null)
            {
                return false;
            }

            if (!ApplyState(device, enabled))
            {
                TouchpadDevice? failed = FindModel(instanceId);
                actual = failed?.IsEnabled ?? false;
                return false;
            }

            for (int i = 0; i < 6; i++)
            {
                Thread.Sleep(200);
                TouchpadDevice? fresh = FindModel(instanceId);
                actual = fresh?.IsEnabled ?? false;
                if (actual == enabled)
                {
                    return true;
                }
            }

            return false;
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
        foreach (TouchpadDevice device in EnumeratePresent())
        {
            if (string.Equals(device.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        return null;
    }

    private static bool ApplyState(TouchpadDevice device, bool enabled)
    {
        uint flags = enabled ? 0 : CmDisablePersist | CmDisableUiNotOk;
        int result = enabled ? CM_Enable_DevNode(device.DevInst, 0) : CM_Disable_DevNode(device.DevInst, flags);
        if (result == 0)
        {
            return true;
        }

        return TryClassInstaller(device, enabled);
    }

    private static bool TryClassInstaller(TouchpadDevice device, bool enabled)
    {
        Guid guid = device.ClassGuid;
        IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DigcfPresent);
        if (set == IntPtr.Zero || set == InvalidHandle)
        {
            return false;
        }

        try
        {
            var info = new SpDevinfoData { CbSize = Marshal.SizeOf<SpDevinfoData>() };
            for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                string id = ReadInstanceId(set, ref info);
                if (!string.Equals(id, device.InstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    info.CbSize = Marshal.SizeOf<SpDevinfoData>();
                    continue;
                }

                var change = new SpPropchangeParams
                {
                    Header = new SpClassInstallHeader
                    {
                        CbSize = Marshal.SizeOf<SpClassInstallHeader>(),
                        InstallFunction = DifPropertyChange
                    },
                    StateChange = enabled ? DicsEnable : DicsDisable,
                    Scope = DicsFlagGlobal,
                    HwProfile = 0
                };
                if (!SetupDiSetClassInstallParams(set, ref info, ref change, Marshal.SizeOf<SpPropchangeParams>()))
                {
                    return false;
                }

                return SetupDiCallClassInstaller(DifPropertyChange, set, ref info);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return false;
    }

    private static List<TouchpadDevice> QueryClassDevices()
    {
        List<TouchpadDevice> list = new();
        foreach (TouchpadDevice device in EnumeratePresent())
        {
            if (!IsExcluded(device.Name, device.InstanceId) && device.Score >= 0)
            {
                list.Add(device);
            }
        }

        return list;
    }

    private static List<TouchpadDevice> EnumeratePresent()
    {
        List<TouchpadDevice> list = new();
        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == IntPtr.Zero || set == InvalidHandle)
        {
            return list;
        }

        try
        {
            var info = new SpDevinfoData { CbSize = Marshal.SizeOf<SpDevinfoData>() };
            for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                string instanceId = ReadInstanceId(set, ref info);
                if (instanceId.Length == 0)
                {
                    info.CbSize = Marshal.SizeOf<SpDevinfoData>();
                    continue;
                }

                string friendly = ReadProperty(set, ref info, SpdrpFriendlyName);
                string description = ReadProperty(set, ref info, SpdrpDeviceDesc);
                string className = ReadProperty(set, ref info, SpdrpClass);
                string hardware = ReadProperty(set, ref info, SpdrpHardwareId);
                string name = friendly.Length > 0 ? friendly : description;
                int score = Score(name, instanceId, hardware, className);
                int statusCode = CM_Get_DevNode_Status(out _, out uint problem, info.DevInst, 0);
                list.Add(new TouchpadDevice
                {
                    Name = name.Length > 0 ? name : instanceId,
                    InstanceId = instanceId,
                    ClassName = className,
                    ClassGuid = info.ClassGuid,
                    DevInst = info.DevInst,
                    IsEnabled = statusCode == 0 && problem == 0,
                    Score = score
                });
                info.CbSize = Marshal.SizeOf<SpDevinfoData>();
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return list;
    }

    private static string ReadInstanceId(IntPtr set, ref SpDevinfoData info)
    {
        var buffer = new StringBuilder(512);
        if (!SetupDiGetDeviceInstanceId(set, ref info, buffer, buffer.Capacity, out _))
        {
            return "";
        }

        return buffer.ToString();
    }

    private static string ReadProperty(IntPtr set, ref SpDevinfoData info, uint property)
    {
        var buffer = new byte[2048];
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out uint regType, buffer, buffer.Length, out _))
        {
            return "";
        }

        string text = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        if (regType == 7)
        {
            return text.Replace("\0", " ");
        }

        int end = text.IndexOf('\0');
        return end >= 0 ? text.Substring(0, end) : text;
    }

    public static int Score(string name, string instanceId, string hardware, string className)
    {
        if (IsExcluded(name, instanceId))
        {
            return -1;
        }

        string id = instanceId + " " + hardware;
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

        if (className.Equals("Mouse", StringComparison.OrdinalIgnoreCase) && score > 0)
        {
            score += 40;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpClassInstallHeader
    {
        public int CbSize;
        public uint InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpPropchangeParams
    {
        public SpClassInstallHeader Header;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, int propertyBufferSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiSetClassInstallParams(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, ref SpPropchangeParams classInstallParams, int classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enable_DevNode(uint devInst, uint flags);
}
