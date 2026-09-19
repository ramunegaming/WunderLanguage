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
        // Falls back to PrintWindow only if BitBlt fails outright.
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
                    return CaptureScreenRegion(screenRect);
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
    }
}