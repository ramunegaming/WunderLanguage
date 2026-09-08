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
        // Set on every TranslateAsync. TranslationManager checks these to
        // decide DeepL fallback for just flagged lines.
        public bool LastUnreliable { get; private set; }
        public string LastReason { get; private set; } = "";
        public string LastCodeVersion { get; private set; } = "unknown";
        // Auto-versioned: sidecar reports its own hash, no manual expected const.

        public OfflineTranslationProvider(string endpoint = "http://localhost:8000")
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
            var response = await _http.PostAsync($"{_endpoint}/translate", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
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