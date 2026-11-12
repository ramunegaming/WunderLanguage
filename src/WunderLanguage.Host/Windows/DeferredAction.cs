using System;
using System.Windows.Threading;

namespace WunderLanguage.Host.Windows
{
    // Debounced action (revert: revert feed-defer): Schedule() restarts the
    // delay clock, so the action runs only once `delay` passes with NO new
    // schedule - i.e. after movement has STOPPED, not once per second while
    // it continues. The pop-out feed's position save rides this: DragMove
    // pumps a LocationChanged per pixel and Save() re-encrypts the key +
    // rewrites settings.json on the UI thread (the live drag freeze: one
    // 'Settings saved' per second DURING the drag, first on the first
    // pixel).
    public sealed class DeferredAction
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _action;

        public int ScheduleCount { get; private set; }
        public int FireCount { get; private set; }
        public bool IsPending => _timer.IsEnabled;

        public DeferredAction(TimeSpan delay, Action action)
        {
            _timer = new DispatcherTimer { Interval = delay };
            _action = action;
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                FireCount++;
                _action();
            };
        }

        // Restart the clock. Two timer ops - cheap enough to call per pixel.
        public void Schedule()
        {
            ScheduleCount++;
            _timer.Stop();
            _timer.Start();
        }

        // Run now if a schedule is outstanding: teardown paths (the feed
        // closing while a drag is <1s old) must not lose the position.
        public void Flush()
        {
            if (!_timer.IsEnabled) return;
            _timer.Stop();
            FireCount++;
            _action();
        }

        // Offline gate (2026-11-12): --feeddefer proves the debounce with
        // the REAL class: a burst of 500 schedules must produce 0 fires
        // (Schedule never fires synchronously, and the clock only runs
        // while unpumped), then a 1.3s quiet pump must produce exactly 1.
        // No emulator, no sidecar, no GPU; exit 0 only if both hold.
        public static bool RunOfflineTest()
        {
            int fires = 0;
            var d = new DeferredAction(TimeSpan.FromSeconds(1), () => fires++);
            // Burst: synchronous (the UI thread is blocked mid-drag, so no
            // tick can run - only an immediate-fire bug would show here).
            for (int i = 0; i < 500; i++)
                d.Schedule();
            FileLogger.Log($"[feeddefer] burst schedules={d.ScheduleCount} fires={d.FireCount} (want 0)");
            bool ok = d.ScheduleCount == 500 && d.FireCount == 0;
            // Quiet: clock started at the last schedule -> fires once at
            // t=1s, timer stops, nothing else can fire.
            Pump(1300);
            FileLogger.Log($"[feeddefer] quiet pump fires={d.FireCount} (want 1)");
            ok &= d.FireCount == 1;
            return ok;
        }

        private static void Pump(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                System.Threading.Thread.Sleep(10);
            }
        }
    }
}
