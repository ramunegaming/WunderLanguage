using System.Drawing;

namespace WunderLanguage.Host.Capture
{
    // Calibration stored as fractions of the window, so moves AND resizes
    // follow automatically. Baseline captured once at calibration; every
    // frame resolves against live window bounds in the same coordinate space.
    public class GameRegion
    {
        private readonly Rectangle _baselineWindowBounds;
        private readonly Rectangle _calibratedGameBounds;

        public GameRegion(Rectangle baselineWindowBounds, Rectangle calibratedGameBounds)
        {
            _baselineWindowBounds = baselineWindowBounds;
            _calibratedGameBounds = calibratedGameBounds;
        }

        public Rectangle GetCurrentBounds(Rectangle currentWindowBounds)
        {
            int deltaX = currentWindowBounds.Left - _baselineWindowBounds.Left;
            int deltaY = currentWindowBounds.Top - _baselineWindowBounds.Top;
            return new Rectangle(
                _calibratedGameBounds.Left + deltaX,
                _calibratedGameBounds.Top + deltaY,
                _calibratedGameBounds.Width,
                _calibratedGameBounds.Height);
        }

        // Fixed offset - legacy, move-only. Use GetScaledCropRect for capture.
        public Rectangle GetRelativeCropRect()
        {
            return new Rectangle(
                _calibratedGameBounds.Left - _baselineWindowBounds.Left,
                _calibratedGameBounds.Top - _baselineWindowBounds.Top,
                _calibratedGameBounds.Width,
                _calibratedGameBounds.Height);
        }

        // Move + resize aware: scale offset/size by live window size ratio.
        // Guards zero baseline (minimized at calibration) to avoid div-zero.
        public Rectangle GetScaledCropRect(Rectangle currentWindowBounds)
        {
            if (_baselineWindowBounds.Width <= 0 || _baselineWindowBounds.Height <= 0)
                return GetRelativeCropRect();
            double sx = (double)currentWindowBounds.Width / _baselineWindowBounds.Width;
            double sy = (double)currentWindowBounds.Height / _baselineWindowBounds.Height;
            int ox = _calibratedGameBounds.Left - _baselineWindowBounds.Left;
            int oy = _calibratedGameBounds.Top - _baselineWindowBounds.Top;
            return new Rectangle(
                (int)(ox * sx),
                (int)(oy * sy),
                (int)(_calibratedGameBounds.Width * sx),
                (int)(_calibratedGameBounds.Height * sy));
        }
    }
}