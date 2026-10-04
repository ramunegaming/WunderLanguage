using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WunderLanguage.Host.Translation
{
    // Was ArgosProvider. Implementation is CAT-Translate via sidecar /translate.
    public class OfflineTranslationProvider : ITranslationProvider
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;

        public string Name => "Offline (CAT-Translate 3.3b)";

        public string Endpoint => _endpoint;
        // Perf batch (ramp-proven 2026-10-04: CAT generation serializes
        // on-GPU — 1/2/4/8 concurrent identical rows cost ~0.6/1.0/1.8/3.7s
        // EACH, so an 8-wide cold battle fan-out lands ~8s frames. Static so
        // publish + prefetch share one GPU budget). Warm paths (tcache,
        // prefetch, sticky, glossary) never reach here; single-row dialogue
        // never contends. Revert: delete gate + using block.
        private static readonly SemaphoreSlim _catGate = new(4, 4);
        // Set on every TranslateAsync. TranslationManager checks these to
        // decide DeepL fallback for just flagged lines.
        public bool LastUnreliable { get; private set; }
        public string LastReason { get; private set; } = "";
        public string LastCodeVersion { get; private set; } = "unknown";
        // Auto-versioned: sidecar reports its own hash, no manual expected const.

        // Micro-hygiene (perf batch): 127.0.0.1, same rationale as
        // OcrClient — this default serves every offline translate.
        public OfflineTranslationProvider(string endpoint = "http://127.0.0.1:8000")
        {
            _endpoint = endpoint;
            _http = new HttpClient();
        }

        private bool _versionLearned;
        public async Task LearnSidecarVersionAsync()
        {
            if (_versionLearned) return;
            try
            {
                var response = await _http.GetAsync($"{_endpoint}/health");
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("code_version", out var v) && v.ValueKind == JsonValueKind.String)
                    LastCodeVersion = v.GetString() ?? "unknown";
            }
            catch { }
            _versionLearned = true;
        }

                public async Task<string> TranslateAsync(string text, string sourceLang = "ja", string targetLang = "en", string? context = null)
        {
            LastUnreliable = false;
            LastReason = "";

            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var payload = JsonSerializer.Serialize(new
            {
                text,
                source_lang = sourceLang.ToLowerInvariant(),
                target_lang = targetLang.ToLowerInvariant()
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            // Gate covers the GPU work only (POST + body read); parsing and
            // bookkeeping below need no slot.
            await _catGate.WaitAsync();
            string json;
            try
            {
                using var resp = await _http.PostAsync($"{_endpoint}/translate", content);
                resp.EnsureSuccessStatusCode();
                json = await resp.Content.ReadAsStringAsync();
            }
            finally { _catGate.Release(); }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string translated = root.TryGetProperty("translated_text", out var t)
                ? (t.GetString() ?? string.Empty)
                : string.Empty;
            if (root.TryGetProperty("unreliable", out var u) && u.ValueKind == JsonValueKind.True)
                LastUnreliable = u.GetBoolean();
            if (root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                LastReason = r.GetString() ?? "";
            if (root.TryGetProperty("code_version", out var vv) && vv.ValueKind == JsonValueKind.String)
                LastCodeVersion = vv.GetString() ?? LastCodeVersion;
            // No drift check: version is informational only. Cache keys already include LastCodeVersion.

            return translated;
        }
    }
}