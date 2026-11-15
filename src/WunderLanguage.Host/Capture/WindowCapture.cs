using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
namespace WunderLanguage.Host.Capture
{
    public record WindowInfo(IntPtr Handle, string Title, bool IsMinimized);
    public class WindowUnavailableException : Exception
    {
        public bool HandleStillValid { get; }
        public WindowUnavailableException(string message, bool handleStillValid) : base(message)
        {
            HandleStillValid = handleStillValid;
        }
    }
    public static class WindowCapture
    {
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        // Screen-copy path (flicker-free). Reads DWM-composited pixels, never forces a redraw.
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
            IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private const uint PW_RENDERFULLCONTENT = 2;
        private const uint CAPTURE_FLAGS = PW_RENDERFULLCONTENT;
        private const int SW_RESTORE = 9;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int DWMWA_CLOAKED = 14;
        private const uint SRCCOPY = 0x00CC0020;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        private static readonly HashSet<string> ExcludedClassNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "CabinetWClass", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
        };
        private static readonly HashSet<string> ExcludedTitles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Program Manager", "Windows Input Experience"
        };
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        public static List<WindowInfo> ListVisibleWindows()
        {
            var windows = new List<WindowInfo>();
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;
                var titleSb = new StringBuilder(256);
                GetWindowText(hWnd, titleSb, titleSb.Capacity);
                string title = titleSb.ToString();
                if (string.IsNullOrWhiteSpace(title) || ExcludedTitles.Contains(title))
                    return true;
                var classSb = new StringBuilder(256);
                GetClassName(hWnd, classSb, classSb.Capacity);
                if (ExcludedClassNames.Contains(classSb.ToString()))
                    return true;
                windows.Add(new WindowInfo(hWnd, title, IsIconic(hWnd)));
                return true;
            }, IntPtr.Zero);
            return windows;
        }
        public static void RestoreIfMinimized(IntPtr hWnd)
        {
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);
        }
        public static bool IsMinimized(IntPtr hWnd) => IsIconic(hWnd);
        public static IntPtr GetForegroundWindowHandle() => GetForegroundWindow();
        // fullscreen-fg (revert: revert fullscreen-fg): focus-gate predicate.
        // PCSX2-style emulators hide their MAIN window and render F11
        // fullscreen in a NEW top-level window of the SAME process (proven
        // live 2026-11-13 Okami leg: attached 0xB0B38 hidden at its old
        // windowed rect, foreground 0xB1560 same-pid for 28 min -> gate
        // held 4145 loops, 0 publishes; live probe confirmed attached
        // vis=False + fg pid=37640 both pcsx2-qt). Pass when the
        // foreground is the attached window (unchanged), a companion
        // (unchanged), OR the fullscreen signature: attached window NOT
        // visible + foreground in the attached window's process. The
        // visible-attached check is the phantom guard: windowed + a
        // same-process dialog taking foreground (settings over game list)
        // must still hold - occlusion deliberately skips same-process
        // windows, so this predicate is the only thing that can. Capture
        // geometry follows fs-capture (revert: revert fs-capture): the
        // calibrated slice keeps anchoring on the attached window while
        // windowed (calibration is relative to that rect); in the
        // fullscreen signature capture switches to the live game window's
        // WHOLE frame instead - a scaled stale slice can no longer miss
        // the dialogue (Okami leg 2 cause A).
        public static bool FocusAllowed(IntPtr fgFocus, IntPtr attached, Func<IntPtr, bool>? isCompanionWindow)
        {
            if (fgFocus == attached) return true;
            if (isCompanionWindow?.Invoke(fgFocus) ?? false) return true;
            if (IsWindowVisible(attached)) return false;
            if (fgFocus == IntPtr.Zero) return false;
            GetWindowThreadProcessId(fgFocus, out uint fgPid);
            GetWindowThreadProcessId(attached, out uint attPid);
            return fgPid != 0 && fgPid == attPid;
        }
        // fs-capture (revert: revert fs-capture): which window captures THIS
        // loop, re-resolved from live USER32 state every time - never a
        // stored handle (design principle: derive from screen state, not
        // assumptions).
        //
        // Windowed (attached visible) -> IntPtr.Zero: the calibrated-slice
        // path on the attached window runs unchanged. The visibility check
        // FIRST is the phantom guard, exactly like FocusAllowed: a
        // same-process dialog over a visible main window must not divert
        // capture.
        //
        // Fullscreen signature (attached hidden - proven live 2026-11-13:
        // PCSX2 F11 hides the main window and renders in a NEW top-level
        // HWND of the same process) -> the LARGEST visible top-level window
        // of the attached window's process:
        //   * largest, not foreground: a same-process dialog opened OVER
        //     the fullscreen game (settings etc.) stays a guest on the game
        //     frame - the game window remains the capture surface, which is
        //     what the old slice effectively read (screen copy reads
        //     composited pixels either way);
        //   * process-derived: a dead/recreated game HWND (display-mode
        //     churn) is picked up on the next loop;
        //   * that window's own rect, WHOLE: dialogue anywhere in the frame
        //     is captured at any aspect/resolution/dialogue position, so
        //     the Okami leg-2 cause-A class (slice missed 13/13 ink rows at
        //     y=1152-1200) cannot recur.
        // Zero whenever the signature is absent -> callers fall back
        // untouched.
        public static IntPtr FullscreenCaptureTarget(IntPtr attached)
        {
            if (attached == IntPtr.Zero || IsWindowVisible(attached)) return IntPtr.Zero;
            GetWindowThreadProcessId(attached, out uint attPid);
            if (attPid == 0) return IntPtr.Zero;
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            try
            {
                EnumWindows((h, _) =>
                {
                    try
                    {
                        if (h == attached) return true;
                        if (!IsWindowVisible(h) || IsIconic(h)) return true;
                        GetWindowThreadProcessId(h, out uint pid);
                        if (pid != attPid) return true;
                        int hr = DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int));
                        if (hr == 0 && cloaked != 0) return true;
                        if (!TryGetWindowBounds(h, out var r) || r.Width <= 0 || r.Height <= 0) return true;
                        long area = (long)r.Width * r.Height;
                        if (area > bestArea) { bestArea = area; best = h; }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return bestArea > 0 ? best : IntPtr.Zero;
        }
        // fs-coverage (revert: revert fs-coverage): the window that SHOWS
        // the game right now, for coverage/geometry reads - the fullscreen
        // target while the signature is active, else the attached window
        // itself. RefreshDockAffordance and the pop anchor both resolve
        // through this, so the coverage edge and the pop land on the SAME
        // surface (Okami leg 2 cause C: coverage read the HIDDEN main's
        // stale windowed rect -> GameCoversMonitor stayed false forever ->
        // auto-popout never fired -> the docked feed stayed behind the
        // fullscreen game; proven: zero dockvis/auto-popout lines all
        // leg). Zero in, zero out (no attached window).
        public static IntPtr CoverageWindowFor(IntPtr attached)
        {
            IntPtr t = FullscreenCaptureTarget(attached);
            return t != IntPtr.Zero ? t : attached;
        }
        public static void FocusWindow(IntPtr hWnd)
        {
            RestoreIfMinimized(hWnd);
            SetForegroundWindow(hWnd);
        }
        public static bool TryGetWindowBounds(IntPtr hWnd, out Rectangle bounds)
        {
            int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>());
            if (hr == 0)
            {
                bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                return true;
            }
            if (GetWindowRect(hWnd, out rect))
            {
                bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                return true;
            }
            bounds = Rectangle.Empty;
            return false;
        }
        // Raw USER32 readback (no DWM): true on-screen frame including
        // shadows. Same coordinate space as SetWindowPos. Diagnostic use.
        public static bool TryGetUserRect(IntPtr hWnd, out Rectangle bounds)
        {
            if (GetWindowRect(hWnd, out RECT rect))
            {
                bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                return true;
            }
            bounds = Rectangle.Empty;
            return false;
        }
        // PMv2 raw virtual-screen rect in PHYSICAL pixels. The framework's
        // SystemParameters.VirtualScreen* instead reports DIP divided by
        // the STALE system DPI (144 ghost after display churn): on this
        // machine (0,0,2986.7,960) instead of the true (0,0,4480,1440) —
        // which is what left the calibration overlay's bottom/right
        // unreachable and let the feed clamp strand the pop-out feed.
        // Revert: revert calib-cover.
        public static Rectangle GetVirtualScreenRect() =>
            new Rectangle(
                GetSystemMetrics(SM_XVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN),
                GetSystemMetrics(SM_CYVIRTUALSCREEN));

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        // The monitor the window sits on (nearest on ties), full rect in
        // physical pixels - same coordinate space as TryGetWindowBounds, so
        // the two compare directly. Revert: revert dock-hide.
        public static bool TryGetMonitorBounds(IntPtr hWnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            try
            {
                IntPtr mon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                if (mon == IntPtr.Zero) return false;
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(mon, ref info)) return false;
                var rc = info.rcMonitor;
                bounds = new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
                return bounds.Width > 0 && bounds.Height > 0;
            }
            catch { return false; }
        }

        // Pure coverage test (revert: revert dock-hide): does the game rect
        // cover its WHOLE monitor? Exclusive/borderless fullscreen reports
        // the monitor rect exactly (or within a pixel of rounding, hence
        // the 2px tolerance); anything inset more than that is windowed and
        // keeps its Dock affordance. Feed the OUTER rect (TryGetUserRect):
        // outer ⊇ visible, so frame-carrying windows that visually cover
        // still count; the DWM frame excludes the 8px invisible border.
        public static bool GameCoversMonitor(Rectangle game, Rectangle monitor, double tolerance = 2.0)
        {
            return game.Left <= monitor.Left + tolerance
                && game.Top <= monitor.Top + tolerance
                && game.Right >= monitor.Right - tolerance
                && game.Bottom >= monitor.Bottom - tolerance;
        }
        // Occlusion probe (revert: revert occlusion-hold): the hot path
        // BitBlts the SCREEN at the game's rect, so whatever is composited
        // ABOVE the game there is what capture actually reads - proven live
        // 2026-10-24 10:34/10:39-10:42: a foreign browser page covered the
        // region for ~45s and its text reached the pipeline as candidate
        // dialogue (the phantom-post class). EnumWindows walks top-down
        // Z-order, so every window enumerated BEFORE the target sits above
        // it. An occluder must be: visible, not minimized, not DWM-cloaked
        // (UWP/virtual-desktop ghosts keep their Z-order while invisible -
        // catching those would hold forever on an invisible window), owned
        // by ANOTHER process (our own chrome is companion policy; the focus
        // gate already allows it), and intersecting the capture rect by at
        // least 2% of its area (an edge kiss below that cannot hold text
        // inside the frame). Uses the same bounds source as the capture
        // rect on both sides, so the coordinate spaces always match.
        // Returns false when the target handle is never seen: without the
        // stop point the walk cannot know which side of the game it is on.
        public static bool IsCapturedRegionOccluded(IntPtr hWnd, GameRegion gameRegion, out IntPtr occluder, out string occluderTitle)
        {
            occluder = IntPtr.Zero;
            occluderTitle = string.Empty;
            if (!IsWindow(hWnd)) return false;
            if (!TryGetWindowBounds(hWnd, out var winBounds)) return false;
            var rel = gameRegion.GetScaledCropRect(winBounds);
            var screenRect = new Rectangle(
                winBounds.Left + rel.Left,
                winBounds.Top + rel.Top,
                rel.Width,
                rel.Height);
            return IsScreenRegionOccluded(hWnd, screenRect, out occluder, out occluderTitle);
        }
        // fs-capture (revert: revert fs-capture): the same probe anchored on
        // the window's OWN whole rect - the surface fs-capture captures in
        // the fullscreen signature. Probe rect and capture rect come from
        // one bounds read of one window per loop, so the gate can never
        // gate different pixels than the capture reads.
        public static bool IsWindowRegionOccluded(IntPtr hWnd, out IntPtr occluder, out string occluderTitle)
        {
            occluder = IntPtr.Zero;
            occluderTitle = string.Empty;
            if (!IsWindow(hWnd)) return false;
            if (!TryGetWindowBounds(hWnd, out var winBounds)) return false;
            return IsScreenRegionOccluded(hWnd, winBounds, out occluder, out occluderTitle);
        }

        private static bool IsScreenRegionOccluded(IntPtr hWnd, Rectangle screenRect, out IntPtr occluder, out string occluderTitle)
        {
            occluder = IntPtr.Zero;
            occluderTitle = string.Empty;
            if (screenRect.Width <= 0 || screenRect.Height <= 0) return false;
            long minOverlap = Math.Max(4096L, (long)screenRect.Width * screenRect.Height / 50);
            GetWindowThreadProcessId(hWnd, out uint selfPid);
            bool found = false, sawTarget = false;
            IntPtr occ = IntPtr.Zero;
            string occTitle = string.Empty;
            try
            {
                EnumWindows((h, _) =>
                {
                    if (h == hWnd) { sawTarget = true; return false; }
                    if (found) return false;
                    try
                    {
                        if (!IsWindowVisible(h) || IsIconic(h)) return true;
                        int hr = DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int));
                        if (hr == 0 && cloaked != 0) return true;
                        if (!TryGetWindowBounds(h, out var r) || r.Width <= 0 || r.Height <= 0) return true;
                        if (!r.IntersectsWith(screenRect)) return true;
                        var inter = Rectangle.Intersect(r, screenRect);
                        if ((long)inter.Width * inter.Height < minOverlap) return true;
                        GetWindowThreadProcessId(h, out uint pid);
                        if (pid == selfPid) return true;
                        occ = h;
                        var sb = new StringBuilder(256);
                        GetWindowText(h, sb, sb.Capacity);
                        occTitle = sb.ToString();
                        if (string.IsNullOrWhiteSpace(occTitle))
                        {
                            var cb = new StringBuilder(256);
                            GetClassName(h, cb, cb.Capacity);
                            occTitle = cb.ToString();
                        }
                        found = true;
                    }
                    catch { }
                    return !found;
                }, IntPtr.Zero);
            }
            catch { }
            occluder = occ;
            occluderTitle = occTitle;
            return found && sawTarget;
        }
        // fscapture case-7 support (revert: revert case7-env-tolerant):
        // EnumWindows walks Z top->bottom, so whichever of the two handles
        // appears first is above the other. Lets the gate accept a
        // fullscreen foreign window that legitimately outranks the taskbar.
        private static bool IsAboveInZOrder(IntPtr hAbove, IntPtr hBelow)
        {
            if (hAbove == IntPtr.Zero || hBelow == IntPtr.Zero || hAbove == hBelow) return false;
            int iAbove = -1, iBelow = -1, i = 0;
            try
            {
                EnumWindows((h, _) =>
                {
                    if (h == hAbove && iAbove < 0) iAbove = i;
                    if (h == hBelow && iBelow < 0) iBelow = i;
                    i++;
                    return iAbove < 0 || iBelow < 0;   // stop once both seen
                }, IntPtr.Zero);
            }
            catch { }
            return iAbove >= 0 && iBelow >= 0 && iAbove < iBelow;
        }
        public static Bitmap CaptureWindow(IntPtr hWnd)
        {
            if (!GetWindowRect(hWnd, out RECT rect))
            {
                bool handleStillValid = IsWindow(hWnd);
                throw new WindowUnavailableException(
                    $"Could not get window rect (handle valid: {handleStillValid}).",
                    handleStillValid);
            }
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("Invalid window dimensions - is the window minimized?");
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(hWnd, hdc, CAPTURE_FLAGS))
                        throw new InvalidOperationException("PrintWindow call failed.");
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }
            return bitmap;
        }
        // HOT LOOP PATH. Screen-copy only — no PrintWindow, no forced redraw, no flicker.
        // Requires the game region to be visible on screen (true while playing).
        // Falls back to PrintWindow if BitBlt throws, and — on the GameRegion
        // overload — when BitBlt "succeeds" but hands back a degenerate
        // (blank/flat) frame (capture-swap, revert: revert capture-swap).
        public static Bitmap CaptureWindowRegion(IntPtr hWnd, Rectangle relativeCropRect)
        {
            if (TryGetWindowBounds(hWnd, out var winBounds))
            {
                var screenRect = new Rectangle(
                    winBounds.Left + relativeCropRect.Left,
                    winBounds.Top + relativeCropRect.Top,
                    relativeCropRect.Width,
                    relativeCropRect.Height);
                try { return CaptureScreenRegion(screenRect); }
                catch (Exception ex)
                {
                    FileLogger.Log($"CaptureWindowRegion: screen-copy failed ({ex.Message}) - falling back to PrintWindow.");
                }
            }
            using var full = CaptureWindow(hWnd);
            var rect = relativeCropRect;
            rect.Intersect(new Rectangle(0, 0, full.Width, full.Height));
            if (rect.Width <= 0 || rect.Height <= 0)
                throw new InvalidOperationException(
                    "The calibrated region is outside the current window bounds - try recalibrating.");
            return full.Clone(rect, full.PixelFormat);
        }

        // Move + resize aware hot path. GameRegion scales against live bounds.
        public static Bitmap CaptureWindowRegion(IntPtr hWnd, GameRegion gameRegion)
        {
            if (TryGetWindowBounds(hWnd, out var winBounds))
            {
                var rel = gameRegion.GetScaledCropRect(winBounds);
                var screenRect = new Rectangle(
                    winBounds.Left + rel.Left,
                    winBounds.Top + rel.Top,
                    rel.Width,
                    rel.Height);
                try
                {
                    // Capture-swap (revert: revert capture-swap): a BitBlt that
                    // SUCCEEDS with a blank frame (the HDR-composited display
                    // class E9 detects) used to sail straight into OCR — the
                    // PrintWindow fallback below only fired on a thrown error.
                    // SwapIfDegenerate qualifies the result and adopts the
                    // existing PrintWindow path when it carries content.
                    return SwapIfDegenerate(CaptureScreenRegion(screenRect), hWnd, gameRegion);
                }
                catch (Exception ex)
                {
                    FileLogger.Log($"CaptureWindowRegion: screen-copy failed ({ex.Message}) - falling back to PrintWindow.");
                }
            }
            using var full = CaptureWindow(hWnd);
            var rect = gameRegion.GetRelativeCropRect();
            rect.Intersect(new Rectangle(0, 0, full.Width, full.Height));
            if (rect.Width <= 0 || rect.Height <= 0)
                throw new InvalidOperationException(
                    "The calibrated region is outside the current window bounds - try recalibrating.");
            return full.Clone(rect, full.PixelFormat);
        }

        // fs-capture (revert: revert fs-capture). HOT LOOP PATH for the
        // fullscreen signature: capture the window's OWN rect WHOLE - no
        // calibrated slice. The window rect is the frame because a
        // fullscreen game owns its surface: dialogue can sit anywhere in it
        // (Okami leg 2: ink at y=1152-1200, outside the stale windowed
        // slice), and the rect is read live in physical pixels from the
        // window actually rendering the game - any aspect, any resolution,
        // any display (multi-monitor: the window's own rect, not the
        // primary's). Screen-copy first (same no-redraw hot path), the
        // capture-swap blank qualifier applies to the whole frame, and
        // PrintWindow falls back on a thrown screen-copy error.
        public static Bitmap CaptureWindowFull(IntPtr hWnd)
        {
            if (TryGetWindowBounds(hWnd, out var winBounds) && winBounds.Width > 0 && winBounds.Height > 0)
            {
                try
                {
                    // PrintWindow-space crop: an oversized rect that the
                    // swap's Intersect clamps to the WHOLE compat frame -
                    // full-frame capture has no slice to clone.
                    return SwapIfDegenerate(CaptureScreenRegion(winBounds), hWnd,
                        new Rectangle(0, 0, int.MaxValue, int.MaxValue));
                }
                catch (Exception ex)
                {
                    FileLogger.Log($"CaptureWindowFull: screen-copy failed ({ex.Message}) - falling back to PrintWindow.");
                }
            }
            return CaptureWindow(hWnd);
        }
        public static Bitmap CaptureScreenRegion(Rectangle screenRect)
        {
            if (screenRect.Width <= 0 || screenRect.Height <= 0)
                throw new InvalidOperationException("Invalid capture region.");
            IntPtr screenDC = GetDC(IntPtr.Zero);
            if (screenDC == IntPtr.Zero)
                throw new InvalidOperationException("GetDC failed.");
            try
            {
                IntPtr memDC = CreateCompatibleDC(screenDC);
                if (memDC == IntPtr.Zero)
                    throw new InvalidOperationException("CreateCompatibleDC failed.");
                try
                {
                    IntPtr hBmp = CreateCompatibleBitmap(screenDC, screenRect.Width, screenRect.Height);
                    if (hBmp == IntPtr.Zero)
                        throw new InvalidOperationException("CreateCompatibleBitmap failed.");
                    try
                    {
                        IntPtr old = SelectObject(memDC, hBmp);
                        try
                        {
                            if (!BitBlt(memDC, 0, 0, screenRect.Width, screenRect.Height,
                                screenDC, screenRect.Left, screenRect.Top, SRCCOPY))
                                throw new InvalidOperationException("BitBlt failed.");
                        }
                        finally
                        {
                            SelectObject(memDC, old);
                        }
                        var bmp = Image.FromHbitmap(hBmp);
                        return bmp;
                    }
                    finally
                    {
                        DeleteObject(hBmp);
                    }
                }
                finally
                {
                    DeleteDC(memDC);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        // ------------------------------------------------------------------
        // Capture-swap (2026-11-11; revert: revert capture-swap).
        // WHY: on an HDR-composited display the GDI screen BitBlt can
        // "succeed" while handing back a blank/flat frame (the class E9
        // detects, batch revert hdr-detect). The PrintWindow fallback that
        // already exists for thrown errors never saw that case, so blank
        // frames went straight into OCR. The swap qualifies every
        // screen-copy result: degenerate screen + a PrintWindow frame that
        // carries content = adopt PrintWindow for as long as the screen
        // stays degenerate (compat episode); both degenerate = keep the
        // screen frame untouched (an honest black loading screen must not
        // be "rescued" into something else, and E9 upstream still reports).
        // Fail-open throughout: any probe error keeps the screen frame.
        private static bool _swapEpisode;
        private static int _swapMisses;     // consecutive compat attempts that ALSO came back degenerate
        private static long _swapAdoptedN;  // compat frames adopted this episode
        private static int _swapMissN;      // misses this episode (log cadence)
        // Eager retries for the first misses (a transient black flash
        // self-heals in ~2s), then amortized — a real all-black loading
        // screen must not pay PrintWindow every 400ms loop forever.
        private const int SwapEagerMisses = 5;
        private const int SwapRetryEvery = 10;

        // Blank test: the SAME 64-wide grayscale thumb conversion
        // ComputeFrameDiff uses (revert hdr-detect), then the SAME shared
        // classifier the E9 probe reads (CaptureController.IsFlatThumb) —
        // the swap and the chip must never disagree on what "blank" means.
        // Fail-open: a sampling error reads "healthy", so a broken thumb
        // can never trigger a swap.
        private static bool IsDegenerate(Bitmap bmp)
        {
            try
            {
                int tw = 64;
                int th = Math.Max(1, bmp.Height * tw / Math.Max(1, bmp.Width));
                byte[] gray;
                using (var thumb = new Bitmap(bmp, new Size(tw, th)))
                {
                    var rect = new Rectangle(0, 0, tw, th);
                    var data = thumb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        gray = new byte[tw * th];
                        int stride = Math.Abs(data.Stride);
                        var row = new byte[stride];
                        for (int y = 0; y < th; y++)
                        {
                            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, stride);
                            for (int x = 0; x < tw; x++)
                                gray[y * tw + x] = (byte)((row[x * 3 + 2] * 77 + row[x * 3 + 1] * 150 + row[x * 3] * 29) >> 8);
                        }
                    }
                    finally { thumb.UnlockBits(data); }
                }
                return CaptureController.IsFlatThumb(gray);
            }
            catch { return false; }
        }

        private static void NoteSwapMiss(string why)
        {
            if (_swapMissN++ == 0 || _swapMissN % 20 == 0)
                FileLogger.Log($"[capture-swap] compat frame unavailable ({why}), keeping degenerate screen-copy (miss #{_swapMissN})");
        }

        private static Bitmap SwapIfDegenerate(Bitmap bmp, IntPtr hWnd, GameRegion gameRegion)
            => SwapIfDegenerate(bmp, hWnd, gameRegion.GetRelativeCropRect());

        // Crop in PrintWindow space: the compat frame is cloned at this rect
        // (calibrated slice for the GameRegion path; oversized rect clamped
        // to the whole frame for fs-capture's full-frame path).
        private static Bitmap SwapIfDegenerate(Bitmap bmp, IntPtr hWnd, Rectangle pwCrop)
        {
            try
            {
                bool bad = IsDegenerate(bmp);
                if (!_swapEpisode)
                {
                    if (!bad) return bmp;
                    _swapEpisode = true;
                    _swapMisses = 0;
                    _swapAdoptedN = 0;
                    _swapMissN = 0;
                    FileLogger.Log("[capture-swap] screen-copy frame degenerate - engaging PrintWindow compat capture");
                    // fall through: first miss tries immediately (eager window)
                }
                else if (!bad)
                {
                    FileLogger.Log($"[capture-swap] screen-copy healthy again - compat capture disengaged (adopted={_swapAdoptedN} misses={_swapMissN})");
                    _swapEpisode = false;
                    _swapMisses = 0;
                    return bmp;
                }
                if (!_swapEpisode) return bmp;
                bool attempt = _swapMisses < SwapEagerMisses || _swapMisses % SwapRetryEvery == 0;
                if (!attempt) return bmp;
                using var full = CaptureWindow(hWnd);
                var rect = pwCrop;
                rect.Intersect(new Rectangle(0, 0, full.Width, full.Height));
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    _swapMisses++;
                    NoteSwapMiss("compat crop outside window bounds");
                    return bmp;
                }
                var alt = full.Clone(rect, full.PixelFormat);
                if (!IsDegenerate(alt))
                {
                    _swapMisses = 0;
                    if (_swapAdoptedN++ % 100 == 0)
                        FileLogger.Log($"[capture-swap] compat frame adopted ({_swapAdoptedN} this episode) - screen-copy still degenerate");
                    bmp.Dispose();
                    return alt;
                }
                alt.Dispose();
                _swapMisses++;
                NoteSwapMiss("compat frame degenerate too");
                return bmp;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[capture-swap] probe error (keeping screen-copy): {ex.Message}");
                return bmp;
            }
        }

        // --captureswap offline gate (revert: revert capture-swap): proves
        // the moving parts on this machine with no emulator/sidecar/GPU:
        // the shared blank classifier in BOTH directions, BOTH capture
        // primitives against a real on-screen window of ours (BitBlt
        // screen-copy + PrintWindow compat), and the production
        // CaptureWindowRegion path end-to-end including size agreement
        // (DPI/coordinate-space bugs fail here, not live). The engage/miss
        // arm needs an actually-degenerate screen - live-owed with the
        // HDR-on proof.
        public static bool RunSwapOfflineTest()
        {
            bool ok = true;
            try
            {
                var flat = new byte[64 * 36];
                var rich = new byte[64 * 36];
                for (int i = 0; i < rich.Length; i++) rich[i] = (byte)(i % 251);
                bool dFlat = CaptureController.IsFlatThumb(flat);
                bool dRich = CaptureController.IsFlatThumb(rich);
                FileLogger.Log($"[captureswap] classifier flat={dFlat} (want True) rich={dRich} (want False)");
                // flat must be degenerate, rich must NOT be (want-False case
                // is inverted vs the want-True case above)
                if (!dFlat || dRich) ok = false;

                System.Windows.Window? w = null;
                try
                {
                    w = new System.Windows.Window
                    {
                        Width = 340,
                        Height = 220,
                        WindowStyle = System.Windows.WindowStyle.ToolWindow,
                        Left = 60,
                        Top = 60,
                        ShowInTaskbar = false,
                        Title = "capture-swap gate",
                        Background = System.Windows.Media.Brushes.White,
                    };
                    var sp = new System.Windows.Controls.StackPanel();
                    sp.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = "CAPTURE SWAP GATE",
                        FontSize = 18,
                        FontWeight = System.Windows.FontWeights.Bold,
                        Margin = new System.Windows.Thickness(10, 10, 0, 0),
                    });
                    sp.Children.Add(new System.Windows.Controls.Border
                    {
                        Background = System.Windows.Media.Brushes.Black,
                        Height = 70,
                        Margin = new System.Windows.Thickness(10),
                    });
                    sp.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = "content band above - both backends must see it",
                        Margin = new System.Windows.Thickness(10),
                    });
                    w.Content = sp;
                    // captureswap-topmost (revert: revert captureswap-topmost):
                    // the raw screen-copy primitive asserts "our test window is
                    // ON SCREEN". A fullscreen foreign window can sit ABOVE a
                    // freshly shown normal-band window on this desktop (proven
                    // 2026-11-15: Brave F11 video covering the whole primary
                    // with a black frame - WindowFromPoint over the gate rect
                    // returned Brave's renderer, pixels (0,0,0), and the
                    // resting occluder of a brand-new gate window was Brave).
                    // Screen-copy then read the occluder's black video ->
                    // degenerate -> raw primitive failed while production
                    // survived only via its PrintWindow swap (log:
                    // "screen-copy frame degenerate - engaging ..."). The
                    // window is OURS: Topmost enforces the precondition the
                    // gate already assumes, touches no foreign window, and
                    // weakens no assertion. Same pre-existing environment as
                    // fscapture case 7 (untouched code diff-wise).
                    w.Topmost = true;
                    w.Show();
                    w.Activate();
                    // Same-thread pump until the deadline: BeginInvoke(Background)
                    // + PushFrame flushes the dispatcher queue, the sleep gives
                    // DWM time to actually paint the window on screen.
                    var deadline = DateTime.UtcNow.AddMilliseconds(400);
                    while (DateTime.UtcNow < deadline)
                    {
                        var frame = new System.Windows.Threading.DispatcherFrame();
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Background,
                            new Action(() => frame.Continue = false));
                        System.Windows.Threading.Dispatcher.PushFrame(frame);
                        System.Threading.Thread.Sleep(10);
                    }
                    IntPtr h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                    if (h == IntPtr.Zero || !TryGetWindowBounds(h, out Rectangle winRect) || winRect.Width <= 0)
                    {
                        FileLogger.Log("[captureswap] gate window has no usable HWND/bounds");
                        ok = false;
                    }
                    else
                    {
                        // Production measures against the DWM extended frame
                        // (same source as the occlusion probe); log the raw
                        // USER32 rect too — the delta is the invisible border.
                        if (TryGetUserRect(h, out Rectangle rawRect))
                            FileLogger.Log($"[captureswap] bounds dwm={winRect.Width}x{winRect.Height} getrect={rawRect.Width}x{rawRect.Height} (production uses the DWM frame)");
                        var region = new GameRegion(winRect, winRect); // whole window = calibration
                        // Production path end-to-end (swap logic included).
                        // Our window is healthy, so it must stay on screen-copy
                        // and return the full-window size exactly.
                        using (var prod = CaptureWindowRegion(h, region))
                        {
                            bool prodBad = IsDegenerate(prod);
                            FileLogger.Log($"[captureswap] production path {prod.Width}x{prod.Height} degenerate={prodBad} (want False) window={winRect.Width}x{winRect.Height}");
                            if (prodBad) ok = false;
                            if (prod.Width != winRect.Width || prod.Height != winRect.Height)
                            {
                                FileLogger.Log($"[captureswap] production size mismatch vs DWM frame bounds ({winRect.Width}x{winRect.Height})");
                                ok = false;
                            }
                        }
                        // Primitive A: screen-copy at the client area.
                        try
                        {
                            var origin = w.PointToScreen(new System.Windows.Point(0, 0));
                            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
                            var client = new Rectangle(
                                (int)origin.X,
                                (int)origin.Y,
                                (int)Math.Ceiling(w.ActualWidth * dpi.DpiScaleX),
                                (int)Math.Ceiling(w.ActualHeight * dpi.DpiScaleY));
                            using var blt = CaptureScreenRegion(client);
                            bool bltBad = IsDegenerate(blt);
                            FileLogger.Log($"[captureswap] bitblt primitive {blt.Width}x{blt.Height} degenerate={bltBad} (want False)");
                            if (bltBad) ok = false;
                        }
                        catch (Exception ex)
                        {
                            FileLogger.Log($"[captureswap] bitblt primitive FAILED: {ex.Message}");
                            ok = false;
                        }
                        // Primitive B: PrintWindow compat (the rescue backend).
                        try
                        {
                            using var pw = CaptureWindow(h);
                            bool pwBad = IsDegenerate(pw);
                            FileLogger.Log($"[captureswap] printwindow primitive {pw.Width}x{pw.Height} degenerate={pwBad} (want False)");
                            if (pwBad) ok = false;
                        }
                        catch (Exception ex)
                        {
                            FileLogger.Log($"[captureswap] printwindow primitive FAILED: {ex.Message}");
                            ok = false;
                        }
                    }
                }
                finally
                {
                    try { w?.Close(); } catch { }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[captureswap] ERROR {ex.Message}");
                ok = false;
            }
            return ok;
        }
        // --fullscreenfg offline gate (revert: revert fullscreen-fg): proves
        // the focus-gate predicate on real windows of this process - the
        // foreground handle, visibility, and PID are read from actual USER32
        // state, not stubs. Cases: attached focused passes; companion passes;
        // windowed same-pid fg (dialog over game list) HOLDS (the phantom
        // guard); hidden attached + same-pid fg (PCSX2 F11 fullscreen) passes;
        // hidden attached + FOREIGN fg (person alt-tabbed away from the
        // fullscreen game) HOLDS. No emulator, no sidecar, no GPU; exit 0
        // only if every assertion holds.
        public static bool RunFullscreenFgOfflineTest()
        {
            bool ok = true;
            System.Windows.Window? attached = null;
            try
            {
                // Real on-screen window standing in for the emulator's main
                // window; a second one is unnecessary - the fullscreen case
                // is the SAME window hidden with a same-pid foreground, and
                // any other window of ours (or even this one shown again)
                // gives that. Instead reuse a taskbar/foreign handle for the
                // foreign case: enumerate the first visible top-level window
                // whose PID differs from ours.
                attached = new System.Windows.Window
                {
                    Width = 420,
                    Height = 300,
                    WindowStyle = System.Windows.WindowStyle.ToolWindow,
                    Left = 120,
                    Top = 120,
                    ShowInTaskbar = false,
                    Title = "fullscreen-fg gate (attached)",
                    Background = System.Windows.Media.Brushes.White,
                };
                attached.Show();
                attached.Activate();
                var deadline = DateTime.UtcNow.AddMilliseconds(300);
                while (DateTime.UtcNow < deadline)
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    System.Threading.Thread.Sleep(10);
                }
                IntPtr hAttached = new System.Windows.Interop.WindowInteropHelper(attached).Handle;
                if (hAttached == IntPtr.Zero)
                {
                    FileLogger.Log("[fullscreenfg] gate window has no HWND");
                    return false;
                }
                // Foreign visible window of a DIFFERENT process (taskbar,
                // shell, anything) - required for the alt-tab-hold case.
                IntPtr hForeign = IntPtr.Zero;
                GetWindowThreadProcessId(hAttached, out uint selfPid);
                EnumWindows((h, _) =>
                {
                    if (h == hAttached || !IsWindowVisible(h)) return true;
                    GetWindowThreadProcessId(h, out uint pid);
                    if (pid == 0 || pid == selfPid) return true;
                    hForeign = h;
                    return false;
                }, IntPtr.Zero);
                if (hForeign == IntPtr.Zero)
                {
                    FileLogger.Log("[fullscreenfg] no foreign visible window found (taskbar missing?)");
                    return false;
                }

                // Case 1: attached itself holds fg -> pass (baseline).
                bool c1 = FocusAllowed(hAttached, hAttached, null);
                FileLogger.Log($"[fullscreenfg] attached-focused pass={c1} (want True)");
                if (!c1) ok = false;

                // Case 2: companion fg -> pass (unchanged contract; the
                // predicate must not shadow the companion escape).
                bool c2 = FocusAllowed(hForeign, hAttached, h => h == hForeign);
                FileLogger.Log($"[fullscreenfg] companion pass={c2} (want True)");
                if (!c2) ok = false;

                // Case 3: VISIBLE attached + same-pid fg = windowed dialog
                // over the game list -> must HOLD (phantom guard). Same pid
                // is trivially true here (every window in this test is ours),
                // so the assertion lives entirely on visibility.
                bool c3 = FocusAllowed(hForeign, hAttached, null);
                FileLogger.Log($"[fullscreenfg] windowed-foreign hold={!c3} (want True)");
                if (c3) ok = false;
                // Same-pid-with-visible-attached: show is not even needed for
                // a second handle - use hAttached's process == itself via a
                // freshly shown second window of ours to make "same pid,
                // different hwnd" real rather than hypothetical.
                System.Windows.Window? sibling = null;
                try
                {
                    sibling = new System.Windows.Window
                    {
                        Width = 200, Height = 120,
                        WindowStyle = System.Windows.WindowStyle.ToolWindow,
                        Left = 600, Top = 600, ShowInTaskbar = false,
                        Title = "fullscreen-fg gate (sibling/dialog)",
                        Background = System.Windows.Media.Brushes.LightGray,
                    };
                    sibling.Show();
                    IntPtr hSibling = new System.Windows.Interop.WindowInteropHelper(sibling).Handle;
                    bool c4 = FocusAllowed(hSibling, hAttached, null);
                    FileLogger.Log($"[fullscreenfg] windowed same-pid dialog hold={!c4} (want True)");
                    if (c4) ok = false;

                    // Case 5: THE fix - attached hidden (emulator fullscreen)
                    // + same-pid fg on a different hwnd -> must PASS.
                    attached.Hide();
                    bool attachedHidden = !IsWindowVisible(hAttached);
                    bool c5 = FocusAllowed(hSibling, hAttached, null);
                    FileLogger.Log($"[fullscreenfg] hidden-attached same-pid pass={c5} hidden={attachedHidden} (want True/True)");
                    if (!c5 || !attachedHidden) ok = false;

                    // Case 6: hidden attached + FOREIGN fg (alt-tabbed away
                    // from the fullscreen game) -> must HOLD.
                    bool c6 = FocusAllowed(hForeign, hAttached, null);
                    FileLogger.Log($"[fullscreenfg] hidden-attached foreign hold={!c6} (want True)");
                    if (c6) ok = false;
                }
                finally
                {
                    try { sibling?.Close(); } catch { }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[fullscreenfg] ERROR {ex.Message}");
                ok = false;
            }
            finally
            {
                try { attached?.Close(); } catch { }
            }
            return ok;
        }

        // Gate-only USER32 (fs-capture gate): FindWindow for the taskbar's
        // foreign handle, SetWindowPos to move OUR OWN gate window onto it
        // in physical pixels (the probe's coordinate space).
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        // --fscapture offline gate (revert: revert fs-capture): proves the
        // fullscreen capture plan on REAL windows of this process - target
        // resolution, whole-frame geometry, and the re-anchored occlusion
        // probe - all from live USER32 state, no stubs. Cases: windowed
        // attached -> no fullscreen target even with a bigger same-pid
        // window visible (calibrated path untouched); hidden attached ->
        // the LARGEST visible same-pid window wins (a dialog opened over
        // the fullscreen game can never become the capture surface);
        // hiding candidates walks down to Zero (no target -> calibrated
        // fallback); CaptureWindowFull returns a frame matching the
        // target's OWN live rect; the occlusion probe reads False when clear
        // and NAMES the foreign window composited over that rect (its hold
        // return value awaits batch occlusion-see - see case 7). No emulator,
        // no sidecar, no GPU; exit 0 only if every assertion holds.
        public static bool RunFsCaptureOfflineTest()
        {
            bool ok = true;
            System.Windows.Window? attached = null;
            System.Windows.Window? small = null;
            System.Windows.Window? large = null;
            static void Pump(int ms)
            {
                var end = DateTime.UtcNow.AddMilliseconds(ms);
                while (DateTime.UtcNow < end)
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    System.Threading.Thread.Sleep(10);
                }
            }
            try
            {
                attached = new System.Windows.Window
                {
                    Width = 420,
                    Height = 300,
                    WindowStyle = System.Windows.WindowStyle.ToolWindow,
                    Left = 120,
                    Top = 120,
                    ShowInTaskbar = false,
                    Title = "fs-capture gate (attached)",
                    Background = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Colors.White, System.Windows.Media.Colors.SteelBlue, 45),
                };
                small = new System.Windows.Window
                {
                    Width = 200,
                    Height = 120,
                    WindowStyle = System.Windows.WindowStyle.ToolWindow,
                    Left = 1500,
                    Top = 200,
                    ShowInTaskbar = false,
                    Title = "fs-capture gate (dialog)",
                    Background = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Colors.LightGreen, System.Windows.Media.Colors.DarkGreen, 45),
                };
                large = new System.Windows.Window
                {
                    Width = 700,
                    Height = 500,
                    WindowStyle = System.Windows.WindowStyle.ToolWindow,
                    Left = 600,
                    Top = 400,
                    ShowInTaskbar = false,
                    Title = "fs-capture gate (game)",
                    Background = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Colors.Orange, System.Windows.Media.Colors.DarkRed, 45),
                };
                attached.Show();
                small.Show();
                large.Show();
                Pump(300);
                IntPtr hAtt = new System.Windows.Interop.WindowInteropHelper(attached).Handle;
                IntPtr hSmall = new System.Windows.Interop.WindowInteropHelper(small).Handle;
                IntPtr hLarge = new System.Windows.Interop.WindowInteropHelper(large).Handle;
                if (hAtt == IntPtr.Zero || hSmall == IntPtr.Zero || hLarge == IntPtr.Zero)
                {
                    FileLogger.Log($"[fscapture] gate windows have no HWND (att=0x{hAtt:X} small=0x{hSmall:X} large=0x{hLarge:X})");
                    return false;
                }

                // Case 1 (phantom guard): windowed attached -> Zero, even
                // though a BIGGER same-pid window is visible. Visibility of
                // the attached window dominates everything else.
                var t1 = FullscreenCaptureTarget(hAtt);
                FileLogger.Log($"[fscapture] windowed target=0x{t1:X} (want Zero, attached visible)");
                if (t1 != IntPtr.Zero) ok = false;

                // Case 2: THE signature - attached hidden -> largest visible
                // same-pid window (the fullscreen game's window).
                attached.Hide();
                Pump(200);
                var t2 = FullscreenCaptureTarget(hAtt);
                FileLogger.Log($"[fscapture] hidden-attached target=0x{t2:X} (want 0x{hLarge:X} largest)");
                if (t2 != hLarge) ok = false;

                // Case 3: hide the 'game' -> the remaining window wins
                // (candidate list walks down, no stale handle survives).
                large.Hide();
                Pump(200);
                var t3 = FullscreenCaptureTarget(hAtt);
                FileLogger.Log($"[fscapture] game hidden target=0x{t3:X} (want 0x{hSmall:X})");
                if (t3 != hSmall) ok = false;

                // Case 4: every candidate gone -> Zero (calibrated fallback,
                // the windowed machinery runs untouched).
                small.Hide();
                Pump(200);
                var t4 = FullscreenCaptureTarget(hAtt);
                FileLogger.Log($"[fscapture] no candidates target=0x{t4:X} (want Zero)");
                if (t4 != IntPtr.Zero) ok = false;

                // Case 5: whole-frame geometry - the bitmap matches the
                // target's OWN live rect (DWM bounds, or raw window rect
                // when the capture-swap adopts the PrintWindow compat
                // frame), never some stale or scaled slice.
                large.Show();
                Pump(300);
                bool hasDwm = TryGetWindowBounds(hLarge, out var dwm);
                bool hasRaw = TryGetUserRect(hLarge, out var raw);
                using (var frame = CaptureWindowFull(hLarge))
                {
                    bool matchDwm = hasDwm && Math.Abs(frame.Width - dwm.Width) <= 8 && Math.Abs(frame.Height - dwm.Height) <= 8;
                    bool matchRaw = hasRaw && Math.Abs(frame.Width - raw.Width) <= 8 && Math.Abs(frame.Height - raw.Height) <= 8;
                    FileLogger.Log($"[fscapture] full-frame {frame.Width}x{frame.Height} (dwm {dwm.Width}x{dwm.Height} raw {raw.Width}x{raw.Height}) matchDwm={matchDwm} matchRaw={matchRaw}");
                    if (!(matchDwm || matchRaw)) ok = false;
                }

                // Case 6: baseline at the resting spot. A foreign window may
                // legitimately sit over the gate window on a busy desktop
                // (proven live here: a Brave window was above us), so this
                // records WHO is there instead of demanding an empty screen
                // - the move test below compares against this baseline.
                IsWindowRegionOccluded(hLarge, out IntPtr occAtRest, out string occTitleAtRest);
                FileLogger.Log($"[fscapture] resting baseline occluder=0x{occAtRest:X} \"{occTitleAtRest}\" (recorded, environment-dependent)");

                // Case 7: a FOREIGN window composited over the target's
                // whole rect -> the probe FINDS and NAMES it (this is what
                // fs-capture owns: the re-anchored probe reads the same rect
                // the capture reads). We move OUR OWN window onto the taskbar
                // (physical px) - no foreign window is ever touched. The
                // probe's RETURN value stays False here by a PRE-EXISTING
                // defect proven by this gate: found=true makes the walk stop
                // before the target -> sawTarget=false -> found&&sawTarget
                // can never hold (zero occlusion-hold lines in all of
                // app2.log history). Fixing that starts real holds and must
                // ship with the own-window block filter (batch occlusion-see,
                // adjudication pending) - flip this case to assert True
                // there. Taskbar auto-hidden = invisible to the probe by
                // design -> log the skip instead of asserting an environment
                // we don't have.
                // Pick tolerance (revert: revert case7-env-tolerant): "== the
                // taskbar" alone assumed nothing fullscreen sits ABOVE the
                // taskbar in Z-order. On this desktop an F11 browser video
                // (Brave, whole primary screen, z above the non-topmost
                // tray) makes the probe return Brave - CORRECTLY: it is the
                // topmost qualifying occluder (desktop replay: Brave z=28
                // vs tray far below; case 7 was untouched by feed-selfmask's
                // diff, so this is pre-existing environment, not a
                // regression). The property case 7 owns is find+name a
                // foreign topmost occluder: accept the taskbar OR any
                // foreign named window ABOVE it in Z-order; a pick below
                // the tray (or self/zero/unnamed) still fails. return=False
                // stays as the pre-existing defect noted above.
                IntPtr hTray = FindWindow("Shell_TrayWnd", null);
                var trayRect = Rectangle.Empty;
                bool trayUp = hTray != IntPtr.Zero && IsWindowVisible(hTray) && TryGetUserRect(hTray, out trayRect) && trayRect.Width > 0;
                if (!trayUp)
                {
                    FileLogger.Log("[fscapture] taskbar not visible (auto-hide?) - occluded case skipped");
                }
                else if (hasDwm)
                {
                    var prev = dwm;
                    SetWindowPos(hLarge, IntPtr.Zero, trayRect.Left + 16, trayRect.Bottom - dwm.Height, 0, 0,
                        SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE);
                    Pump(250);
                    bool occTrue = IsWindowRegionOccluded(hLarge, out IntPtr occWin, out string occTitle);
                    GetWindowThreadProcessId(occWin, out uint occPid);
                    uint selfPid7 = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                    bool foreign = occWin != IntPtr.Zero && occPid != 0 && occPid != selfPid7;
                    bool named = !string.IsNullOrWhiteSpace(occTitle);
                    bool topPick = occWin == hTray || IsAboveInZOrder(occWin, hTray);
                    bool foundIt = foreign && named && topPick;
                    FileLogger.Log($"[fscapture] occluder found={foundIt} handle=0x{occWin:X} (want 0x{hTray:X} or above it in Z) pid={occPid} named={named} topPick={topPick} \"{occTitle}\" return={occTrue} (False = pre-existing found&&sawTarget defect, see comment)");
                    if (!foundIt) ok = false;
                    SetWindowPos(hLarge, IntPtr.Zero, prev.Left, prev.Top, 0, 0,
                        SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE);
                    Pump(150);
                    IsWindowRegionOccluded(hLarge, out var occWin2, out var occTitle2);
                    bool cleared = occWin2 == occAtRest;
                    FileLogger.Log($"[fscapture] occluder after move back=0x{occWin2:X} \"{occTitle2}\" baseline=0x{occAtRest:X} restored={cleared} (want True)");
                    if (!cleared) ok = false;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[fscapture] ERROR {ex.Message}");
                ok = false;
            }
            finally
            {
                try { attached?.Close(); } catch { }
                try { small?.Close(); } catch { }
                try { large?.Close(); } catch { }
            }
            return ok;
        }
    }
}