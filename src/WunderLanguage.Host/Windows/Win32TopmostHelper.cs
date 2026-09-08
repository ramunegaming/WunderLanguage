using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WunderLanguage.Host.Windows
{
    // The companion panels aggressively reassert their own topmost status
    // via raw Win32 calls every 150ms. A plain WPF Topmost="True" on some
    // other window can lose that race, since WPF's own topmost handling
    // only reasserts on a false->true transition rather than continuously.
    // Windows that need to reliably stay above the panels (calibration
    // overlay, the intro prompt) use this to fight on the same terms.
    public static class Win32TopmostHelper
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        public static void ForceTopmost(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
