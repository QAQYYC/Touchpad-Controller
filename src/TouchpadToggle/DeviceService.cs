using System;
using System.Collections.Generic;
using Microsoft.Win32;
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
    public string Signature { get; init; } = "";
}

internal static class DeviceService
{
    private const int ConfigFlagDisabled = 0x1;
    private const int CmProbDisabled = 22;
    private const uint LocateNormal = 0;
    private const uint LocatePhantom = 1;
    private const uint LocateCancelRemove = 2;
    private const uint DifPropertyChange = 0x12;
    private const uint DicsEnable = 1;
    private const uint DicsDisable = 2;
    private const uint DicsFlagGlobal = 1;
    private const uint DicsFlagConfigSpecific = 2;
    private const uint DigcfPresent = 0x02;
    private const uint DigcfAllClasses = 0x04;
    private const uint CmDisablePersist = 0x08;
    private const uint CmDisableUiNotOk = 0x04;
    private static readonly IntPtr InvalidHandle = new(-1);
    private static readonly Guid MouseClass = new("4d36e96f-e325-11ce-bfc1-08002be10318");
    private static readonly Guid HidClass = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");

    public static TouchpadDevice? Resolve()
    {
        List<TouchpadDevice> devices = Enumerate();
        TouchpadDevice? saved = ReadSaved(devices);
        if (saved != null)
        {
            return saved;
        }

        List<TouchpadDevice> ranked = devices.FindAll(device => device.Score >= 60);
        if (ranked.Count == 0)
        {
            List<TouchpadDevice> internalMice = devices.FindAll(device =>
                device.ClassName.Equals("Mouse", StringComparison.OrdinalIgnoreCase)
                && !device.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase));
            if (internalMice.Count == 1)
            {
                SaveId(internalMice[0].InstanceId);
                return internalMice[0];
            }

            return null;
        }

        ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
        int best = ranked[0].Score;
        List<TouchpadDevice> winners = ranked.FindAll(device => device.Score == best);
        TouchpadDevice chosen = winners[0];
        if (winners.Count != 1)
        {
            List<TouchpadDevice> named = winners.FindAll(device =>
                Regex.IsMatch(device.Name, "touch\\s*pad|touchpad|触摸板", RegexOptions.IgnoreCase));
            if (named.Count != 1)
            {
                return null;
            }

            chosen = named[0];
        }

        SaveId(chosen.InstanceId);
        return chosen;
    }

    public static List<TouchpadDevice> ListCandidates()
    {
        List<TouchpadDevice> rows = new();
        foreach (TouchpadDevice device in Enumerate())
        {
            bool interesting = device.Score >= 60
                || device.ClassName.Equals("Mouse", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(device.Name, "touch|pad|i2c|goodix", RegexOptions.IgnoreCase);
            if (interesting)
            {
                rows.Add(device);
            }
        }

        rows.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (rows.Count > 40)
        {
            rows.RemoveRange(40, rows.Count - 40);
        }

        return rows;
    }

    public static TouchpadDevice? Find(string instanceId)
    {
        return ReadRegistryDevice(instanceId);
    }

    public static bool TrySetEnabled(string instanceId, bool enabled, out bool actual)
    {
        actual = false;
        try
        {
            List<string> targets = RelatedIds(instanceId);
            if (targets.Count == 0)
            {
                targets.Add(instanceId);
            }

            foreach (string id in targets)
            {
                ApplyState(id, enabled);
            }

            for (int i = 0; i < 8; i++)
            {
                Thread.Sleep(250);
                TouchpadDevice? fresh = ReadRegistryDevice(instanceId);
                if (fresh != null)
                {
                    actual = fresh.IsEnabled;
                    if (actual == enabled)
                    {
                        return true;
                    }
                }
            }

            TouchpadDevice? last = Find(instanceId);
            actual = last?.IsEnabled ?? false;
            return actual == enabled;
        }
        catch
        {
            TouchpadDevice? fresh = Find(instanceId);
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

    private static TouchpadDevice? ReadSaved(List<TouchpadDevice> devices)
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

        foreach (TouchpadDevice device in devices)
        {
            if (string.Equals(device.InstanceId, id, StringComparison.OrdinalIgnoreCase) && !IsExcluded(device.Name, device.InstanceId))
            {
                return device;
            }
        }

        return ReadRegistryDevice(id);
    }

    private static List<string> RelatedIds(string instanceId)
    {
        List<TouchpadDevice> devices = Enumerate();
        TouchpadDevice? self = null;
        foreach (TouchpadDevice device in devices)
        {
            if (string.Equals(device.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            {
                self = device;
                break;
            }
        }

        self ??= ReadRegistryDevice(instanceId);
        List<string> ids = new() { instanceId };
        if (self == null || self.Signature.Length == 0)
        {
            return ids;
        }

        foreach (TouchpadDevice device in devices)
        {
            if (device.Signature.Equals(self.Signature, StringComparison.OrdinalIgnoreCase)
                && !ids.Exists(id => id.Equals(device.InstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                ids.Add(device.InstanceId);
            }
        }

        return ids;
    }

    private static void ApplyState(string instanceId, bool enabled)
    {
        SetDisabledFlag(instanceId, !enabled);
        if (!TryLocate(instanceId, out uint devInst))
        {
            return;
        }

        if (enabled)
        {
            CM_Enable_DevNode(devInst, 0);
            CM_Setup_DevNode(devInst, 0);
        }
        else if (CM_Disable_DevNode(devInst, CmDisableUiNotOk | CmDisablePersist) != 0)
        {
            CM_Disable_DevNode(devInst, CmDisableUiNotOk);
        }

        TryClassInstaller(instanceId, enabled);
        if (enabled)
        {
            CM_Enable_DevNode(devInst, 0);
            CM_Setup_DevNode(devInst, 0);
            if (CM_Get_Parent(out uint parent, devInst, 0) == 0)
            {
                CM_Reenumerate_DevNode(parent, 0);
            }

            CM_Reenumerate_DevNode(devInst, 0);
        }
    }

    private static List<TouchpadDevice> Enumerate()
    {
        Dictionary<string, TouchpadDevice> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (TouchpadDevice device in EnumerateRegistry())
        {
            map[device.InstanceId] = device;
        }

        foreach (TouchpadDevice device in EnumerateSetup())
        {
            if (!map.TryGetValue(device.InstanceId, out TouchpadDevice? existing))
            {
                map[device.InstanceId] = device;
                continue;
            }

            map[device.InstanceId] = new TouchpadDevice
            {
                Name = existing.Name.Length > 0 ? existing.Name : device.Name,
                InstanceId = existing.InstanceId,
                ClassName = existing.ClassName.Length > 0 ? existing.ClassName : device.ClassName,
                ClassGuid = existing.ClassGuid == Guid.Empty ? device.ClassGuid : existing.ClassGuid,
                DevInst = device.DevInst != 0 ? device.DevInst : existing.DevInst,
                IsEnabled = existing.IsEnabled && device.IsEnabled,
                Score = Math.Max(existing.Score, device.Score),
                Signature = existing.Signature.Length > 0 ? existing.Signature : device.Signature
            };
        }

        return new List<TouchpadDevice>(map.Values);
    }

    private static List<TouchpadDevice> EnumerateRegistry()
    {
        List<TouchpadDevice> list = new();
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum");
            if (root == null)
            {
                return list;
            }

            foreach (string bus in new[] { "HID", "I2C", "ACPI", "ROOT" })
            {
                using RegistryKey? busKey = root.OpenSubKey(bus);
                if (busKey == null)
                {
                    continue;
                }

                foreach (string hardwareKeyName in busKey.GetSubKeyNames())
                {
                    using RegistryKey? hardwareKey = busKey.OpenSubKey(hardwareKeyName);
                    if (hardwareKey == null)
                    {
                        continue;
                    }

                    foreach (string instanceName in hardwareKey.GetSubKeyNames())
                    {
                        using RegistryKey? instanceKey = hardwareKey.OpenSubKey(instanceName);
                        if (instanceKey == null)
                        {
                            continue;
                        }

                        string instanceId = bus + "\\" + hardwareKeyName + "\\" + instanceName;
                        TouchpadDevice? device = ReadKey(instanceId, instanceKey);
                        if (device != null)
                        {
                            list.Add(device);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return list;
    }

    private static TouchpadDevice? ReadRegistryDevice(string instanceId)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + instanceId);
            return key == null ? null : ReadKey(instanceId, key);
        }
        catch
        {
            return null;
        }
    }

    private static TouchpadDevice? ReadKey(string instanceId, RegistryKey key)
    {
        string friendly = CleanDesc(ReadString(key, "FriendlyName"));
        string description = CleanDesc(ReadString(key, "DeviceDesc"));
        string className = ReadString(key, "Class");
        string hardware = string.Join(" ", ReadMulti(key, "HardwareID"));
        hardware += " " + string.Join(" ", ReadMulti(key, "CompatibleIDs"));
        string name = friendly.Length > 0 ? friendly : description;
        if (IsExcluded(name, instanceId + " " + hardware))
        {
            return null;
        }

        int score = Score(name, instanceId, hardware, className);
        Guid.TryParse(ReadString(key, "ClassGUID"), out Guid classGuid);
        bool disabled = HasDisabledFlag(key) || ProfileDisabled(instanceId) || ProblemDisabled(instanceId);
        return new TouchpadDevice
        {
            Name = name.Length > 0 ? name : instanceId,
            InstanceId = instanceId,
            ClassName = className,
            ClassGuid = classGuid,
            DevInst = 0,
            IsEnabled = !disabled,
            Score = score,
            Signature = SignatureOf(instanceId + " " + hardware)
        };
    }

    private static bool HasDisabledFlag(RegistryKey key)
    {
        return key.GetValue("ConfigFlags") is int flags && (flags & ConfigFlagDisabled) != 0;
    }

    private static bool ProfileDisabled(string instanceId)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Hardware Profiles\Current\System\CurrentControlSet\Enum\" + instanceId);
            return key?.GetValue("CSConfigFlags") is int flags && (flags & ConfigFlagDisabled) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ProblemDisabled(string instanceId)
    {
        if (!TryLocate(instanceId, out uint devInst))
        {
            return false;
        }

        int code = CM_Get_DevNode_Status(out _, out uint problem, devInst, 0);
        return code == 0 && problem == CmProbDisabled;
    }

    private static void SetDisabledFlag(string instanceId, bool disabled)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + instanceId, true);
            if (key != null)
            {
                int flags = key.GetValue("ConfigFlags") is int current ? current : 0;
                flags = disabled ? flags | ConfigFlagDisabled : flags & ~ConfigFlagDisabled;
                key.SetValue("ConfigFlags", flags, RegistryValueKind.DWord);
            }

            string profilePath = @"SYSTEM\CurrentControlSet\Hardware Profiles\Current\System\CurrentControlSet\Enum\" + instanceId;
            using RegistryKey? profile = Registry.LocalMachine.OpenSubKey(profilePath, true)
                ?? (disabled ? Registry.LocalMachine.CreateSubKey(profilePath) : null);
            if (profile != null)
            {
                int flags = profile.GetValue("CSConfigFlags") is int current ? current : 0;
                flags = disabled ? flags | ConfigFlagDisabled : flags & ~ConfigFlagDisabled;
                profile.SetValue("CSConfigFlags", flags, RegistryValueKind.DWord);
            }
        }
        catch
        {
        }
    }

    private static bool TryLocate(string instanceId, out uint devInst)
    {
        if (CM_Locate_DevNode(out devInst, instanceId, LocateNormal) == 0)
        {
            return true;
        }

        if (CM_Locate_DevNode(out devInst, instanceId, LocateCancelRemove) == 0)
        {
            return true;
        }

        return CM_Locate_DevNode(out devInst, instanceId, LocatePhantom) == 0;
    }

    private static void TryClassInstaller(string instanceId, bool enabled)
    {
        TouchpadDevice? device = ReadRegistryDevice(instanceId);
        Guid guid = device?.ClassGuid ?? Guid.Empty;
        if (guid == Guid.Empty)
        {
            guid = device?.ClassName.Equals("HIDClass", StringComparison.OrdinalIgnoreCase) == true ? HidClass : MouseClass;
        }

        ChangeByClass(guid, instanceId, enabled);
        ChangeByClass(Guid.Empty, instanceId, enabled);
    }

    private static void ChangeByClass(Guid classGuid, string instanceId, bool enabled)
    {
        IntPtr set = classGuid == Guid.Empty
            ? SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfAllClasses)
            : SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DigcfPresent);
        if (set == IntPtr.Zero || set == InvalidHandle)
        {
            return;
        }

        try
        {
            var info = new SpDevinfoData { CbSize = IntPtr.Size == 8 ? 32 : 28 };
            for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                if (!ReadInstanceId(set, ref info).Equals(instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    info.CbSize = IntPtr.Size == 8 ? 32 : 28;
                    continue;
                }

                foreach (uint scope in new[] { DicsFlagGlobal, DicsFlagConfigSpecific })
                {
                    var change = new SpPropchangeParams
                    {
                        Header = new SpClassInstallHeader
                        {
                            CbSize = Marshal.SizeOf<SpClassInstallHeader>(),
                            InstallFunction = DifPropertyChange
                        },
                        StateChange = enabled ? DicsEnable : DicsDisable,
                        Scope = scope,
                        HwProfile = 0
                    };
                    if (SetupDiSetClassInstallParams(set, ref info, ref change, Marshal.SizeOf<SpPropchangeParams>()))
                    {
                        SetupDiCallClassInstaller(DifPropertyChange, set, ref info);
                    }
                }

                return;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static List<TouchpadDevice> EnumerateSetup()
    {
        List<TouchpadDevice> list = new();
        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == IntPtr.Zero || set == InvalidHandle)
        {
            return list;
        }

        try
        {
            int size = IntPtr.Size == 8 ? 32 : 28;
            var info = new SpDevinfoData { CbSize = size };
            for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                string instanceId = ReadInstanceId(set, ref info);
                if (instanceId.Length == 0 || IsExcluded("", instanceId))
                {
                    info.CbSize = size;
                    continue;
                }

                string friendly = ReadProperty(set, ref info, 12);
                string description = ReadProperty(set, ref info, 0);
                string className = ReadProperty(set, ref info, 7);
                string hardware = ReadProperty(set, ref info, 1);
                string name = friendly.Length > 0 ? friendly : description;
                int statusCode = CM_Get_DevNode_Status(out _, out uint problem, info.DevInst, 0);
                list.Add(new TouchpadDevice
                {
                    Name = CleanDesc(name),
                    InstanceId = instanceId,
                    ClassName = className,
                    ClassGuid = info.ClassGuid,
                    DevInst = info.DevInst,
                    IsEnabled = !(statusCode == 0 && problem == CmProbDisabled),
                    Score = Score(name, instanceId, hardware, className),
                    Signature = SignatureOf(instanceId + " " + hardware)
                });
                info.CbSize = size;
            }
        }
        catch
        {
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
        return SetupDiGetDeviceInstanceId(set, ref info, buffer, buffer.Capacity, out _) ? buffer.ToString() : "";
    }

    private static string ReadProperty(IntPtr set, ref SpDevinfoData info, uint property)
    {
        var buffer = new byte[2048];
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out uint regType, buffer, buffer.Length, out _))
        {
            return "";
        }

        string text = Encoding.Unicode.GetString(buffer);
        if (regType == 7)
        {
            return text.Replace("\0", " ").Trim();
        }

        int end = text.IndexOf('\0');
        return CleanDesc(end >= 0 ? text.Substring(0, end) : text.Trim());
    }

    private static string ReadString(RegistryKey key, string name)
    {
        return key.GetValue(name) as string ?? "";
    }

    private static string[] ReadMulti(RegistryKey key, string name)
    {
        return key.GetValue(name) switch
        {
            string[] list => list,
            string one => new[] { one },
            _ => Array.Empty<string>()
        };
    }

    private static string CleanDesc(string raw)
    {
        int split = raw.LastIndexOf(';');
        if (raw.StartsWith("@", StringComparison.Ordinal) && split >= 0 && split < raw.Length - 1)
        {
            return raw.Substring(split + 1);
        }

        return raw;
    }

    private static string SignatureOf(string text)
    {
        Match vid = Regex.Match(text, @"VID_([0-9A-F]{4}).{0,16}PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        if (vid.Success)
        {
            return vid.Groups[1].Value.ToUpperInvariant() + ":" + vid.Groups[2].Value.ToUpperInvariant();
        }

        Match model = Regex.Match(text, @"BLTP[0-9A-F]+|GXTP[0-9A-F]+", RegexOptions.IgnoreCase);
        return model.Success ? model.Value.ToUpperInvariant() : "";
    }

    private static int Score(string name, string instanceId, string hardware, string className)
    {
        if (IsExcluded(name, instanceId + " " + hardware))
        {
            return -1;
        }

        string id = instanceId + " " + hardware;
        int score = 0;
        if (id.Contains("347D", StringComparison.OrdinalIgnoreCase) && id.Contains("7853", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (id.Contains("BLTP7853", StringComparison.OrdinalIgnoreCase) || id.Contains("GXTP7863", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (Regex.IsMatch(id, @"GXTP7\d{3}|BLTP7\d{3}", RegexOptions.IgnoreCase))
        {
            score += 90;
        }

        if (id.Contains("27C6", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(id, "01E0|01E9|0D42", RegexOptions.IgnoreCase))
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

        if (className.Equals("Mouse", StringComparison.OrdinalIgnoreCase)
            && !instanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)
            && !instanceId.StartsWith("ACPI\\PNP0F", StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (instanceId.StartsWith("I2C\\", StringComparison.OrdinalIgnoreCase) && score > 0)
        {
            score += 15;
        }

        return score;
    }

    private static bool IsExcluded(string name, string id)
    {
        string text = name + " " + id;
        if (Regex.IsMatch(text, "指纹|fingerprint|触摸屏|touch\\s*screen", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(text, @"PID_6890|PID_550A|PID_55A4|PID_63AC|PID_6384|&DEV_6890", RegexOptions.IgnoreCase))
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

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Setup_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Reenumerate_DevNode(uint devInst, uint flags);
}
