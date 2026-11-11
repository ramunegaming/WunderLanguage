using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WunderLanguage.Host.Capture
{
    // HDR / advanced-color display query (2026-11-11; revert:
    // revert hdr-detect). Detection only — no pipeline change, no gating.
    // WHY: GDI BitBlt screen capture reads an SDR copy of what the GPU
    // composites; on an HDR-enabled display that copy comes back blank
    // (black/flat), so OCR gets a solid frame and the feed goes silent
    // with nothing on screen saying why — the silent-death class again.
    // We cannot predict the user's HDR setting, so this auto-detects:
    // one [hdr] log line per session dumps every active path (source
    // device, monitor name, HDR flags), and the E9 blank-frame chip's
    // live line re-queries at fire time for hdr=on/off/unknown.
    //
    // SAFETY CONTRACT (fail-open, always):
    //  - every struct layout is copied from the official wingdi.h
    //    (microsoft/win32metadata RecompiledIdlHeaders) and self-checked
    //    with Marshal.SizeOf BEFORE any native call; a mismatch disables
    //    the probe (log + return null) instead of corrupting memory;
    //  - device-info packets ride in over-sized unallocated buffers
    //    (1 KiB) with the official per-type size in header.size, so a
    //    wrong size can only produce ERROR_INVALID_PARAMETER, never an
    //    overwrite;
    //  - the mode buffer is over-allocated at 128 B/element (native is
    //    64 B), so even a stride surprise stays inside the allocation;
    //  - any API failure logs once and returns null — capture never
    //    waits on, retries, or dies because of this probe.
    public static class DisplayHdr
    {
        // QueryDisplayConfig flags / return codes (winuser.h, official
        // values: QDC_ALL_PATHS=1, QDC_ONLY_ACTIVE_PATHS=2,
        // QDC_DATABASE_CURRENT=4). We want ACTIVE paths only.
        private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        // DISPLAYCONFIG_DEVICE_INFO_TYPE (wingdi.h — verified values:
        // GET_SOURCE_NAME=1, GET_TARGET_NAME=2, GET_ADVANCED_COLOR_INFO=9).
        private const uint GET_SOURCE_NAME = 1;
        private const uint GET_TARGET_NAME = 2;
        private const uint GET_ADVANCED_COLOR_INFO = 9;

        // Native struct sizes in bytes, computed from the official header:
        //  DEVICE_INFO_HEADER = type4 + size4 + LUID8 + id4            = 20
        //  SOURCE_DEVICE_NAME = header20 + WCHAR[CCHDEVICENAME=32]     = 84
        //  TARGET_DEVICE_NAME = header20 + flags4 + outTech4 + edid2+2
        //                       + conn4 + friendly[64]=128 + path[128]=256
        //                                                                 = 420
        //  GET_ADVANCED_COLOR_INFO = header20 + flags4 + encoding4
        //                            + bpc4                             = 32
        //  PATH_INFO = SOURCE_INFO20 + TARGET_INFO48 + flags4           = 72
        private const int NativeHeaderSize = 20;
        private const int NativeSourceNameSize = 84;
        private const int NativeTargetNameSize = 420;
        private const int NativeAdvColorSize = 32;
        private const int NativePathInfoSize = 72;
        // Native MODE_INFO is 64 B; we never read it, we only own the
        // buffer — over-allocate 128 B per claimed element.
        private const int ModeBytesPerElement = 128;
        // Over-sized scratch for device-info packets (largest native
        // packet here is 420 B).
        private const int PacketAllocBytes = 1024;
        private const int InitialPathCapacity = 32;
        private const int MaxPathCapacity = 512;
        private const int MaxModeCapacity = 4096;

        // One result per active display path.
        public sealed class DisplayPathInfo
        {
            public string SourceName = "";   // e.g. \\.\DISPLAY1
            public string MonitorName = "";  // EDID friendly name
            public string DevicePath = "";
            public bool HdrKnown;            // advanced-color packet answered
            public bool HdrSupported;
            public bool HdrEnabled;          // advancedColorEnabled = HDR-ish mode active
            public bool WideColorEnforced;
            public bool ForceDisabled;
            public int ColorEncoding;        // DISPLAYCONFIG_COLOR_ENCODING
            public int BitsPerChannel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        // 20 bytes — official layout: LUID + id + mode union + statusFlags.
        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;   // union { modeInfoIdx | clone/source bits }
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        // 48 bytes — official layout (see header comment for the math).
        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public int outputTechnology;
            public int rotation;
            public int scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public int scanLineOrdering;
            public int targetAvailable;   // BOOL
            public uint statusFlags;
        }

        // 72 bytes — self-checked against NativePathInfoSize before use.
        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEXW
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] // CCHDEVICENAME
            public string szDevice;
        }

        // currentTopologyId: MUST be NULL unless flags is
        // QDC_DATABASE_CURRENT — a non-NULL pointer with active-path
        // flags is exactly ERROR_INVALID_PARAMETER (learned live from
        // --hdrtest: hr=87 until this became IntPtr.Zero).
        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            IntPtr modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        // Runtime guard: the native stride of PATH_INFO MUST equal our
        // marshalled size or QueryDisplayConfig would walk off the array.
        // Mismatch = probe disabled (fail-open), never a guess.
        private static bool SelfCheckOk()
        {
            int path = Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>();
            int header = Marshal.SizeOf<DISPLAYCONFIG_DEVICE_INFO_HEADER>();
            if (path != NativePathInfoSize || header != NativeHeaderSize)
            {
                FileLogger.Log($"[hdr] struct self-check FAILED path={path} (expect {NativePathInfoSize}) " +
                    $"header={header} (expect {NativeHeaderSize}) — probe disabled (fail-open)");
                return false;
            }
            return true;
        }

        // Enumerate every active path. null = could not answer (fail-open).
        public static List<DisplayPathInfo>? QueryAll()
        {
            if (!SelfCheckOk()) return null;
            uint paths = InitialPathCapacity;
            uint modes = 128;
            var pathBuf = new DISPLAYCONFIG_PATH_INFO[InitialPathCapacity];
            IntPtr modeBuf = IntPtr.Zero;
            try
            {
                modeBuf = Marshal.AllocHGlobal(128 * ModeBytesPerElement);
                int hr = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref paths, pathBuf,
                    ref modes, modeBuf, IntPtr.Zero);
                if (hr == ERROR_INSUFFICIENT_BUFFER)
                {
                    // Windows wrote back the counts it needs.
                    if (paths == 0 || paths > MaxPathCapacity || modes == 0 || modes > MaxModeCapacity)
                    {
                        FileLogger.Log($"[hdr] QueryDisplayConfig demanded paths={paths} modes={modes} (fail-open: unknown)");
                        return null;
                    }
                    pathBuf = new DISPLAYCONFIG_PATH_INFO[paths];
                    Marshal.FreeHGlobal(modeBuf);
                    modeBuf = Marshal.AllocHGlobal((int)modes * ModeBytesPerElement);
                    hr = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref paths, pathBuf,
                        ref modes, modeBuf, IntPtr.Zero);
                }
                if (hr != ERROR_SUCCESS)
                {
                    FileLogger.Log($"[hdr] QueryDisplayConfig hr={hr} (fail-open: unknown)");
                    return null;
                }
                var result = new List<DisplayPathInfo>();
                for (int i = 0; i < paths; i++)
                {
                    var p = pathBuf[i];
                    var info = new DisplayPathInfo
                    {
                        SourceName = ReadSourceName(p.sourceInfo),
                        MonitorName = ReadTargetName(p.targetInfo, out string devPath),
                        DevicePath = devPath,
                    };
                    ReadAdvancedColor(p.targetInfo, info);
                    result.Add(info);
                }
                return result;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[hdr] QueryAll error (fail-open: unknown): {ex.Message}");
                return null;
            }
            finally
            {
                if (modeBuf != IntPtr.Zero) Marshal.FreeHGlobal(modeBuf);
            }
        }

        // Packet helper: official per-type size in header.size, 1 KiB of
        // room behind it so a size surprise cannot overrun.
        private static IntPtr AllocPacket(uint type, uint size, in LUID adapterId, uint id)
        {
            IntPtr p = Marshal.AllocHGlobal(PacketAllocBytes);
            for (int o = 0; o < PacketAllocBytes; o += 8) Marshal.WriteInt64(p, o, 0);
            Marshal.WriteInt32(p, 0, (int)type);
            Marshal.WriteInt32(p, 4, (int)size);
            Marshal.WriteInt32(p, 8, unchecked((int)adapterId.LowPart));
            Marshal.WriteInt32(p, 12, adapterId.HighPart);
            Marshal.WriteInt32(p, 16, unchecked((int)id));
            return p;
        }

        private static string ReadSourceName(in DISPLAYCONFIG_PATH_SOURCE_INFO src)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                p = AllocPacket(GET_SOURCE_NAME, NativeSourceNameSize, src.adapterId, src.id);
                if (DisplayConfigGetDeviceInfo(p) != ERROR_SUCCESS) return "";
                return (Marshal.PtrToStringUni(IntPtr.Add(p, 20), 32) ?? "").TrimEnd('\0');
            }
            catch { return ""; }
            finally { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }
        }

        private static string ReadTargetName(in DISPLAYCONFIG_PATH_TARGET_INFO tgt, out string devicePath)
        {
            devicePath = "";
            IntPtr p = IntPtr.Zero;
            try
            {
                p = AllocPacket(GET_TARGET_NAME, NativeTargetNameSize, tgt.adapterId, tgt.id);
                if (DisplayConfigGetDeviceInfo(p) != ERROR_SUCCESS) return "";
                // header20 + flags4 + outTech4 + edid2 + edid2 + conn4 = 36
                devicePath = (Marshal.PtrToStringUni(IntPtr.Add(p, 164), 128) ?? "").TrimEnd('\0');
                return (Marshal.PtrToStringUni(IntPtr.Add(p, 36), 64) ?? "").TrimEnd('\0');
            }
            catch { return ""; }
            finally { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }
        }

        private static void ReadAdvancedColor(in DISPLAYCONFIG_PATH_TARGET_INFO tgt, DisplayPathInfo info)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                p = AllocPacket(GET_ADVANCED_COLOR_INFO, NativeAdvColorSize, tgt.adapterId, tgt.id);
                if (DisplayConfigGetDeviceInfo(p) != ERROR_SUCCESS) { info.HdrKnown = false; return; }
                int flags = Marshal.ReadInt32(p, 20);
                info.HdrKnown = true;
                info.HdrSupported = (flags & 1) != 0;        // advancedColorSupported
                info.HdrEnabled = (flags & 2) != 0;          // advancedColorEnabled
                info.WideColorEnforced = (flags & 4) != 0;   // wideColorEnforced
                info.ForceDisabled = (flags & 8) != 0;       // advancedColorForceDisabled
                info.ColorEncoding = Marshal.ReadInt32(p, 24);
                info.BitsPerChannel = Marshal.ReadInt32(p, 28);
            }
            catch { info.HdrKnown = false; }
            finally { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }
        }

        // GDI device name of the monitor hosting hWnd (\\.\DISPLAYn),
        // "" when it cannot be determined.
        private static string MonitorDeviceName(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero) return "";
                IntPtr mon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                if (mon == IntPtr.Zero) return "";
                var mi = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>(), szDevice = "" };
                if (!GetMonitorInfo(mon, ref mi)) return "";
                return mi.szDevice ?? "";
            }
            catch { return ""; }
        }

        // Path hosting the window, else the first path, else null.
        private static DisplayPathInfo? MatchPath(List<DisplayPathInfo> paths, IntPtr hWnd)
        {
            if (paths.Count == 0) return null;
            string dev = MonitorDeviceName(hWnd);
            if (dev.Length > 0)
                foreach (var p in paths)
                    if (string.Equals(p.SourceName, dev, StringComparison.OrdinalIgnoreCase))
                        return p;
            return paths[0];
        }

        // One [hdr] baseline dump per session, fired from the capture
        // loop's first captured frame (log-only; fail-open).
        private static bool _logged;
        public static void LogOnce(IntPtr hWnd)
        {
            if (_logged) return;
            _logged = true;
            try
            {
                var paths = QueryAll();
                if (paths is null) { FileLogger.Log("[hdr] query unavailable (fail-open: unknown)"); return; }
                if (paths.Count == 0) { FileLogger.Log("[hdr] no active display paths"); return; }
                string match = MonitorDeviceName(hWnd);
                foreach (var p in paths)
                {
                    bool isGame = match.Length > 0 &&
                        string.Equals(p.SourceName, match, StringComparison.OrdinalIgnoreCase);
                    FileLogger.Log($"[hdr] path src={p.SourceName} mon=\"{p.MonitorName}\" " +
                        $"game={(isGame ? "yes" : "no")} hdr={(HdrState(p))} " +
                        $"supported={(p.HdrKnown ? (p.HdrSupported ? 1 : 0) : "?")} " +
                        $"enabled={(p.HdrKnown ? (p.HdrEnabled ? 1 : 0) : "?")} " +
                        $"enc={(p.HdrKnown ? p.ColorEncoding.ToString() : "?")} " +
                        $"bpc={(p.HdrKnown ? p.BitsPerChannel.ToString() : "?")}");
                }
                if (match.Length == 0)
                    FileLogger.Log("[hdr] game-monitor match unavailable (window not on a queryable monitor)");
            }
            catch (Exception ex) { FileLogger.Log($"[hdr] LogOnce error (fail-open): {ex.Message}"); }
        }

        private static string HdrState(DisplayPathInfo p)
            => !p.HdrKnown ? "unknown" : (p.HdrEnabled ? "on" : "off");

        // Live-line payload for the E9 chip: fresh query at fire time
        // (HDR can be toggled mid-session), one string, fail-open.
        // e.g. hdr=on mon="DELL U2720Q"
        public static string DescribeForWindow(IntPtr hWnd)
        {
            try
            {
                var paths = QueryAll();
                if (paths is null || paths.Count == 0) return "hdr=unknown mon=\"?\"";
                var p = MatchPath(paths, hWnd);
                if (p is null) return "hdr=unknown mon=\"?\"";
                return $"hdr={HdrState(p)} mon=\"{(p.MonitorName.Length > 0 ? p.MonitorName : "?")}\"";
            }
            catch { return "hdr=unknown mon=\"?\""; }
        }

        // --hdrtest offline gate (revert: revert hdr-detect): struct
        // self-checks + the flat-frame classifier the loop runs + a dump
        // of every active path. No emulator, no sidecar, no GPU.
        public static bool RunOfflineTest()
        {
            bool ok = true;
            try
            {
                int path = Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>();
                int header = Marshal.SizeOf<DISPLAYCONFIG_DEVICE_INFO_HEADER>();
                FileLogger.Log($"[hdrtest] self-check path={path} (expect {NativePathInfoSize}) " +
                    $"header={header} (expect {NativeHeaderSize})");
                if (path != NativePathInfoSize || header != NativeHeaderSize) ok = false;

                // Same classifier the capture loop reads — prove the
                // threshold both ways (tolerance = max-min <= 8).
                var solid = new byte[160 * 90];
                var edge8 = new byte[160 * 90];
                var edge9 = new byte[160 * 90];
                var noise = new byte[160 * 90];
                for (int i = 0; i < edge8.Length; i++) edge8[i] = (byte)(i % 2 == 0 ? 0 : 8);
                for (int i = 0; i < edge9.Length; i++) edge9[i] = (byte)(i % 2 == 0 ? 0 : 9);
                for (int i = 0; i < noise.Length; i++) noise[i] = (byte)(i % 2 == 0 ? 0 : 255);
                bool a = CaptureController.IsFlatThumb(solid);
                bool b = CaptureController.IsFlatThumb(edge8);
                bool c = CaptureController.IsFlatThumb(edge9);
                bool d = CaptureController.IsFlatThumb(noise);
                FileLogger.Log($"[hdrtest] flat classifier solid={a} (want True) edge8={b} (want True) " +
                    $"edge9={c} (want False) noise={d} (want False)");
                if (!a || !b || c || d) ok = false;

                var paths = QueryAll();
                if (paths is null) { FileLogger.Log("[hdrtest] display query FAILED (fail-open)"); ok = false; }
                else if (paths.Count == 0) { FileLogger.Log("[hdrtest] no active display paths"); ok = false; }
                else
                {
                    foreach (var p in paths)
                        FileLogger.Log($"[hdrtest] path src={p.SourceName} mon=\"{p.MonitorName}\" " +
                            $"hdr={(HdrState(p))} supported={(p.HdrKnown ? (p.HdrSupported ? 1 : 0) : "?")} " +
                            $"enabled={(p.HdrKnown ? (p.HdrEnabled ? 1 : 0) : "?")} " +
                            $"enc={(p.HdrKnown ? p.ColorEncoding.ToString() : "?")} " +
                            $"bpc={(p.HdrKnown ? p.BitsPerChannel.ToString() : "?")}");
                }
            }
            catch (Exception ex) { FileLogger.Log($"[hdrtest] ERROR {ex.Message}"); ok = false; }
            return ok;
        }
    }
}
