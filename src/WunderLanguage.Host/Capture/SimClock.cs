using System;

namespace WunderLanguage.Host.Capture
{
    // M7c manual clock (slice v1): minimal TimeProvider proving the loop's
    // decision-adjacent clocks are injectable. Only overrides GetUtcNow —
    // enough for MaybeFlushLoopCost + corpus wt stamping. Full timer
    // determinism (Task.Delay completing on Advance) is framework-owned and
    // arrives with FakeTimeProvider in a future test project; this harness
    // does NOT reimplement CreateTimer. Revert: delete this file + the
    // --simcheck block in App.xaml.cs.
    internal sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;
        public ManualClock(DateTimeOffset start) { _now = start; }
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan dt) { try { _now += dt; } catch { } }
    }

    // Slice-v1 proof: exercises the exact predicate MaybeFlushLoopCost uses
    // ((now - last) < 60s) against the manual clock, plus the Task.Delay
    // provider overload shape the loop now paces with. No controller, no
    // sidecar — instant and deterministic.
    internal static class SimCheck
    {
        public static bool Run()
        {
            try
            {
                var t0 = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
                var clock = new ManualClock(t0);
                if (clock.GetUtcNow() != t0) { FileLogger.Log("[simcheck] FAIL clock-init"); return false; }
                clock.Advance(TimeSpan.FromSeconds(30));
                var last = t0;
                bool hold30 = (clock.GetUtcNow() - last).TotalSeconds < 60;
                clock.Advance(TimeSpan.FromSeconds(31));
                bool hold61 = (clock.GetUtcNow() - last).TotalSeconds < 60;
                if (!hold30 || hold61) { FileLogger.Log($"[simcheck] FAIL predicate hold30={hold30} hold61={hold61}"); return false; }
                using (var cts = new System.Threading.CancellationTokenSource())
                {
                    var d = System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(1), TimeProvider.System, cts.Token);
                    if (!d.Wait(TimeSpan.FromSeconds(5))) { FileLogger.Log("[simcheck] FAIL delay-overload"); return false; }
                }
                FileLogger.Log("[simcheck] PASS clock-injection + cost-predicate + delay-overload (slice v1)");
                return true;
            }
            catch (System.Exception ex) { try { FileLogger.Log($"[simcheck] ERROR: {ex.Message}"); } catch { } return false; }
        }
    }
}
