using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WunderLanguage.Host.Translation
{
    public enum TranslationProviderPreference
    {
        DeepL,
        Offline,
        [Obsolete("Renamed to Offline (was Argos, now CAT-Translate).")]
        Argos = Offline
    }

    // DeepL = accurate/paid tier (default). Offline = CAT-Translate 3.3b via
    // sidecar /translate (MIT, no key). Callers just call TranslateAsync.
    // Auto-fallbacks:
    //  - DeepL quota exceeded -> Offline until retry interval
    //  - Offline flagged unreliable (degenerate/Japanese-echo/negation-mismatch)
    //    -> DeepL for just that line when available (no speed cost on clean lines)
    public class TranslationManager
    {
        private readonly DeepLProvider? _deepL;
        private readonly OfflineTranslationProvider _offline;
        private readonly TranslationCache _cache;
        private readonly TranslationProviderPreference _preference;

        private bool _deepLQuotaExceeded = false;
        private DateTime _lastQuotaRetry = DateTime.MinValue;
        // Previous source line, passed as DeepL context (best-effort under concurrency).
        private string _lastSourceLine = string.Empty;
        // In-flight dedup: concurrent identical fires share one Task (double-fire costs one CAT).
        private readonly Dictionary<string, Task<string>> _inflight = new();
        private static readonly TimeSpan QuotaRetryInterval = TimeSpan.FromMinutes(30);

        // Pruned once per process, after we know the live sidecar version.
        private bool _cachePruned = false;

        public string ActiveProviderName { get; private set; } = "Unknown";

        // Perf batch: cache-hit visibility. Interval counters (Interlocked;
        // the loop-cost flush reads+resets). Revert: delete + Increments.
        private long _cacheReqs, _cacheHits;
        public (long req, long hit) CacheStats()
        {
            long r = Interlocked.Exchange(ref _cacheReqs, 0);
            long h = Interlocked.Exchange(ref _cacheHits, 0);
            return (r, h);
        }
        // Hygiene logging: cache size visibility (point-in-time, not reset).
        // Revert with the [spend] log line.
        public (int entries, long bytes) CacheSize()
        {
            try { return (_cache.EntryCount, _cache.FileBytes); }
            catch { return (-1, -1); }
        }

        public TranslationManager(
            TranslationProviderPreference preference,
            DeepLProvider? deepL,
            OfflineTranslationProvider offline,
            TranslationCache cache)
        {
            // Compat: old callers passing Argos get Offline (same value).
            _preference = preference;
            _deepL = deepL;
            _offline = offline;
            _cache = cache;

            if (preference == TranslationProviderPreference.DeepL && deepL is null)
                throw new InvalidOperationException(
                    "DeepL was selected as the preferred provider but no API key was configured.");
        }

        public async Task<string> TranslateAsync(string text, string sourceLang = "JA", string targetLang = "EN")
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            Interlocked.Increment(ref _cacheReqs);

            // Cache key is provider-scoped: Offline tracks the sidecar's
            // reported code version (any Python output-shaping change orphans
            // prior entries automatically), DeepL uses a versioned tag
            // (bump DeepLProvider.CacheScope when provider shaping changes).
            // Without this, switching models serves the other tier's stale text.
            await _offline.LearnSidecarVersionAsync();
            MaybePruneCache();
            string cacheScope = _preference == TranslationProviderPreference.DeepL
                ? DeepLProvider.CacheScope
                : _offline.LastCodeVersion;
            string versionedLang = cacheScope + ":" + targetLang;

            if (_cache.TryGet(text, versionedLang, out var cachedCurrent))
            {
                ActiveProviderName = "Cache";
                _lastSourceLine = text;
                Interlocked.Increment(ref _cacheHits);
                FileLogger.Log($"[translate-metrics] cache-hit chars={text.Length}");
                return cachedCurrent;
            }

            string key = versionedLang + "|" + text;
            Task<string> shared;
            lock (_inflight)
            {
                if (!_inflight.TryGetValue(key, out shared!))
                {
                    shared = TranslateMissAsync(text, sourceLang, targetLang, versionedLang);
                    _inflight[key] = shared;
                }
            }
            try { return await shared; }
            finally { lock (_inflight) { _inflight.Remove(key); } }
        }

        private async Task<string> TranslateMissAsync(string text, string sourceLang, string targetLang, string versionedLang)
        {
            long t0 = Environment.TickCount64;
            string result = _preference == TranslationProviderPreference.DeepL
                ? await TranslateViaDeepLWithFallback(text, sourceLang, targetLang)
                : await TranslateViaOfflineWithDeepLBackup(text, sourceLang, targetLang);
            FileLogger.Log($"[translate-metrics] miss provider={ActiveProviderName} ms={Environment.TickCount64 - t0} chars={text.Length}");

            // Never cache flagged lines: a suppressed Japanese echo must not
            // become tomorrow's instant wrong answer. Next run retries fresh.
            // Never cache fallback serves under the preferred tier's key.
            bool wasUnreliable = _preference != TranslationProviderPreference.DeepL && _offline.LastUnreliable;
            bool servedByPreferred = _preference != TranslationProviderPreference.DeepL
                || ActiveProviderName == (_deepL?.Name ?? string.Empty);
            bool wasGuide = false;
            // Guides are backend-only (future popup warms on this call).
            // Feed shows translations, never [word guide] text. Blanks stay
            // suppressed: silence over ugliness, never over accuracy.
            if (string.IsNullOrEmpty(result))
            {
                try { _ = TryWordGuideAsync(text); } catch { }
            }
            if (!wasUnreliable && servedByPreferred && !wasGuide)
                _cache.Set(text, versionedLang, result);
            _ = ObserveAsync(text);
            _lastSourceLine = text;
            return result;
        }

        // Tier-agnostic shadow: DeepL + CAT + future providers all observed.
        // Fire-and-forget, instant endpoint, never blocks translation.
        // Shared clients (micro-hygiene batch): per-call HttpClients churn
        // connections; these two timeouts differ so two statics. Loopback
        // only — no DNS-staleness concern.
        private static readonly HttpClient _observeHttp = new() { Timeout = TimeSpan.FromMilliseconds(800) };
        private static readonly HttpClient _guideHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
        private async Task ObserveAsync(string text)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { text });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                await _observeHttp.PostAsync($"{_offline.Endpoint}/observe", content);
            }
            catch { }
        }

        // Dictionary gloss fallback: blank model output becomes a word guide
        // instead of silence. Additive only - empty guide changes nothing.
        private async Task<string> TryWordGuideAsync(string text)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { text });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _guideHttp.PostAsync($"{_offline.Endpoint}/define", content);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("words", out var words) || words.ValueKind != JsonValueKind.Array)
                    return "";
                var parts = new List<string>();
                foreach (var w in words.EnumerateArray())
                {
                    string surf = w.TryGetProperty("surface", out var s) && s.ValueKind == JsonValueKind.String ? (s.GetString() ?? "") : "";
                    string reading = w.TryGetProperty("reading", out var r) && r.ValueKind == JsonValueKind.String ? (r.GetString() ?? "") : "";
                    string gloss = w.TryGetProperty("gloss", out var g) && g.ValueKind == JsonValueKind.String ? (g.GetString() ?? "") : "";
                    if (string.IsNullOrEmpty(surf) || string.IsNullOrEmpty(gloss))
                        continue;
                    parts.Add(string.IsNullOrEmpty(reading) ? $"{surf}: {gloss}" : $"{surf}({reading}): {gloss}");
                    if (parts.Count >= 6)
                        break;
                }
                if (parts.Count == 0)
                    return "";
                return "[word guide] " + string.Join("; ", parts);
            }
            catch { return ""; }
        }

        private void MaybePruneCache()
        {
            if (_cachePruned)
                return;
            // Don't prune until we know the live sidecar version, otherwise
            // we'd wipe the offline tier on every DeepL-only launch.
            if (string.IsNullOrEmpty(_offline.LastCodeVersion) || _offline.LastCodeVersion == "unknown")
                return;

            _cachePruned = true;
            try
            {
                var allowed = new HashSet<string>(System.StringComparer.Ordinal)
                {
                    DeepLProvider.CacheScope,
                    _offline.LastCodeVersion
                };
                int pruned = _cache.PruneExcept(allowed);
                if (pruned > 0)
                    FileLogger.Log($"[cache] pruned {pruned} orphaned entries, kept {string.Join(",", allowed)}");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[cache] prune skipped: {ex.Message}");
            }
        }

        private async Task<string> TranslateViaDeepLWithFallback(string text, string sourceLang, string targetLang)
        {
            bool shouldRetryDeepL = !_deepLQuotaExceeded
                || DateTime.UtcNow - _lastQuotaRetry > QuotaRetryInterval;

            if (shouldRetryDeepL && _deepL is not null)
            {
                try
                {
                    var result = await _deepL.TranslateAsync(text, sourceLang, targetLang, _lastSourceLine);
                    _deepLQuotaExceeded = false;
                    ActiveProviderName = _deepL.Name;
                    return result;
                }
                catch (DeepLQuotaExceededException)
                {
                    _deepLQuotaExceeded = true;
                    _lastQuotaRetry = DateTime.UtcNow;
                    FileLogger.Log("[WunderLanguage] DeepL quota exceeded - falling back to Offline until quota resets.");
                }
                catch (Exception ex)
                {
                    FileLogger.Log($"[WunderLanguage] DeepL call failed ({ex.Message}) - falling back to Offline for this line.");
                }
            }

            ActiveProviderName = _offline.Name;
            return await _offline.TranslateAsync(text, sourceLang, targetLang);
        }

        private async Task<string> TranslateViaOfflineWithDeepLBackup(string text, string sourceLang, string targetLang)
        {
            ActiveProviderName = _offline.Name;
            var offlineResult = await _offline.TranslateAsync(text, sourceLang, targetLang);

            // Only flagged lines pay DeepL latency/cost. Clean lines stay offline-fast.
            // Critical for autoscroll (Zelda intro) — normal lines never block on DeepL.
            if (_offline.LastUnreliable && _deepL is not null && !_deepLQuotaExceeded)
            {
                try
                {
                    Console.WriteLine($"[WunderLanguage] Offline flagged ({_offline.LastReason}) - using DeepL for this line.");
                    var deepLResult = await _deepL.TranslateAsync(text, sourceLang, targetLang, _lastSourceLine);
                    ActiveProviderName = _deepL.Name + " (unreliable-offline fallback)";
                    return deepLResult;
                }
                catch (DeepLQuotaExceededException)
                {
                    _deepLQuotaExceeded = true;
                    _lastQuotaRetry = DateTime.UtcNow;
                    Console.WriteLine("[WunderLanguage] DeepL quota exceeded during unreliable fallback - keeping offline result.");
                }
                // Any other DeepL error: keep offline result, don't break capture loop.
                catch (Exception ex)
                {
                    Console.WriteLine($"[WunderLanguage] DeepL fallback failed ({ex.Message}) - keeping offline result.");
                }
            }

            return offlineResult;
        }
    }
}