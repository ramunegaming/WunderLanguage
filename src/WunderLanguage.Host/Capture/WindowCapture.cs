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
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private const uint PW_RENDERFULLCONTENT = 2;
        private const uint CAPTURE_FLAGS = PW_RENDERFULLCONTENT;
        private const int SW_RESTORE = 9;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int DWMWA_CLOAKED = 14;
        private const uint SRCCOPY = 0x00CC0020;
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
                var rect = gameRegion.GetRelativeCropRect();
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
    }
}