#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

public static class G25AutoInit
{
    const byte SpringStrength = 4;              // 1..15
    const string NativeId = "vid_046d&pid_c299";
    const string CompatId = "vid_046d&pid_c294";
    const int SwitchTimeoutMs = 25000;          // ms
    const int SettleMs = 800;                   // ms

    public static bool WheelPresent { get; private set; }
    public static bool NativeReady { get; private set; }
    public static string LastReport { get; private set; } = "";

    [DllImport("hid.dll")]
    static extern void HidD_GetHidGuid(out Guid gHid);
    [DllImport("hid.dll")]
    static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buffer, int length);
    [DllImport("hid.dll")]
    static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr preparsed);
    [DllImport("hid.dll")]
    static extern bool HidD_FreePreparsedData(IntPtr preparsed);
    [DllImport("hid.dll")]
    static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA did);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SP_DEVICE_INTERFACE_DATA did, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid guid; public int flags; public IntPtr reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    const int DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GenericReadWrite = 0xC0000000, GenericWrite = 0x40000000;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Kick()
    {
        if (Application.isEditor) return;
        Task.Run(() =>
        {
            string report = EnsureNative();
            if (!WheelPresent) return;
            if (NativeReady) Debug.Log("[G25AutoInit] " + report);
            else Debug.LogError("[G25AutoInit] " + report);
        });
    }

    public static bool NativePresent() => FindHidPaths(NativeId).Count > 0;
    public static bool CompatPresent() => FindHidPaths(CompatId).Count > 0;

    public static string EnsureNative()
    {
        try
        {
            var native = FindHidPaths(NativeId);
            var compat = native.Count > 0 ? new List<string>() : FindHidPaths(CompatId);
            WheelPresent = native.Count > 0 || compat.Count > 0;
            if (!WheelPresent)
            {
                NativeReady = false;
                LastReport = "no G25 detected";
                return LastReport;
            }

            if (native.Count == 0)
            {
                bool sent = false;
                for (int attempt = 0; attempt < 3 && !sent; attempt++)
                {
                    foreach (string p in compat)
                        if (WriteCommand(p, new byte[] { 0xF8, 0x10, 0, 0, 0, 0, 0 })) { sent = true; break; }
                    if (!sent) Thread.Sleep(300);
                }
                for (int waited = 0; native.Count == 0 && waited < SwitchTimeoutMs; waited += 500)
                {
                    Thread.Sleep(500);
                    native = FindHidPaths(NativeId);
                }
                if (native.Count == 0)
                {
                    NativeReady = false;
                    LastReport = sent
                        ? "the mode-switch command was accepted but the native device (PID C299) never appeared. The wheel is still in 200 deg compatibility mode, so the pedal axes CarController reads are empty. Check the wheel's external power brick (it self-calibrates with a full left-right sweep when powered)."
                        : "could not write the mode-switch command to the wheel: no HID interface accepted it (another application may hold the device open).";
                    return LastReport;
                }
                Thread.Sleep(SettleMs);
            }

            bool range = false, spring = false, activate = false;
            foreach (string p in native)
            {
                if (!range) range = WriteCommand(p, new byte[] { 0xF8, 0x81, 0x84, 0x03, 0, 0, 0 });
                if (!spring) spring = WriteCommand(p, new byte[] { 0xFE, 0x0D, SpringStrength, SpringStrength, (byte)Math.Min(255, SpringStrength * 16), 0, 0 });
                if (!activate) activate = WriteCommand(p, new byte[] { 0x14, 0, 0, 0, 0, 0, 0 });
                if (range && spring && activate) break;
            }
            NativeReady = true;
            LastReport = $"native mode ready: rotation range {(range ? "900 deg" : "NOT SET")}, autocenter {(spring && activate ? SpringStrength + "/15" : "NOT SET")}";
            return LastReport;
        }
        catch (Exception e)
        {
            NativeReady = false;
            LastReport = "initialization failed: " + e.Message;
            return LastReport;
        }
    }

    static bool WriteCommand(string path, byte[] payload)
    {
        if (WriteViaInterrupt(path, payload)) return true;
        return WriteViaSetReport(path, payload);
    }

    static bool WriteViaInterrupt(string path, byte[] payload)
    {
        var h = Open(path);
        if (h == null) return false;
        try
        {
            byte[] buf = BuildReport(h, payload);
            using var fs = new FileStream(h, FileAccess.Write, buf.Length);
            fs.Write(buf, 0, buf.Length);
            fs.Flush();
            return true;
        }
        catch { try { h.Close(); } catch { } return false; }
    }

    static bool WriteViaSetReport(string path, byte[] payload)
    {
        var h = Open(path);
        if (h == null) return false;
        try
        {
            byte[] buf = BuildReport(h, payload);
            return HidD_SetOutputReport(h, buf, buf.Length);
        }
        catch { return false; }
        finally { try { h.Close(); } catch { } }
    }

    static SafeFileHandle Open(string path)
    {
        var h = CreateFile(path, GenericReadWrite, 0x3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            h.Close();
            h = CreateFile(path, GenericWrite, 0x3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        }
        if (h.IsInvalid) { h.Close(); return null; }
        return h;
    }

    static byte[] BuildReport(SafeFileHandle h, byte[] payload)
    {
        int len = payload.Length + 1;
        if (HidD_GetPreparsedData(h, out IntPtr pp))
        {
            try
            {
                if (HidP_GetCaps(pp, out HIDP_CAPS caps) == 0x110000 && caps.OutputReportByteLength > 0)
                    len = caps.OutputReportByteLength;
            }
            finally { HidD_FreePreparsedData(pp); }
        }
        if (len < payload.Length + 1) len = payload.Length + 1;
        var buf = new byte[len];
        Array.Copy(payload, 0, buf, 1, payload.Length);
        return buf;
    }

    static List<string> FindHidPaths(string vidPidFragment)
    {
        var found = new List<string>();
        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return found;
        try
        {
            var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref did); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                if (required <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref did, buf, required, out _, IntPtr.Zero))
                    {
                        string path = Marshal.PtrToStringAuto(buf + 4);
                        if (path != null && path.IndexOf(vidPidFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                            found.Add(path);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return found;
    }
}
#endif
