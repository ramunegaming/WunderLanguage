using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WunderLanguage.Host.Translation
{
    public class DeepLQuotaExceededException : Exception
    {
        public DeepLQuotaExceededException(string message) : base(message) { }
    }

    public class DeepLProvider : ITranslationProvider
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        public string Name => "DeepL";

        // Auto-versioned like sidecar py-xxx: hash of provider shape.
        // Adding a noun fixup changes the hash -> old entries auto-prune,
        // no manual v2->v3 bump. Bump ShapeVersion only on major logic
        // change (re-adding model_type etc).
        private const string ShapeVersion = "v1";
        public static readonly string CacheScope;

        static DeepLProvider()
        {
            try
            {
                string shape = ShapeVersion + "|EN-US|ctx=prev|nouns:"
                    + string.Join(",", new[] { @"\b[Hh]iranal\b=>Hyrule", @"\bHilar\b=>Hyrule", @"\bAgnim\b=>Agahnim" })
                    + "|titles:" + string.Join(",", Packs.PackRegistry.Current.TitleMap.Select(t => t.ja + "=>" + t.en));
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(shape));
                string hex = Convert.ToHexString(hash).ToLowerInvariant();
                CacheScope = "deepl-" + hex.Substring(0, 6);
            }
            catch
            {
                CacheScope = "deepl-auto";
            }
        }

        public DeepLProvider(string apiKey, string baseUrl = "https://api-free.deepl.com")
        {
            _apiKey = apiKey;
            _baseUrl = baseUrl;
            _http = new HttpClient();
        }

        public async Task<string> TranslateAsync(string text, string sourceLang = "JA", string targetLang = "EN", string? context = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string mappedTarget = targetLang.Equals("EN", StringComparison.OrdinalIgnoreCase) ? "EN-US" : targetLang;

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/translate");
            request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");

            var form = new List<KeyValuePair<string, string>>
            {
                new("text", text),
                new("source_lang", sourceLang),
                new("target_lang", mappedTarget),
                // NOTE 2026-09-07: split_sentences + model_type both removed.
                // split_sentences=none 400s on api-free, model_type 400s on JA->EN free.
                // Default is the proven 11/11 working path.
            };
            if (!string.IsNullOrWhiteSpace(context))
                form.Add(new("context", context));
            request.Content = new FormUrlEncodedContent(form);

            var response = await _http.SendAsync(request);

            if ((int)response.StatusCode == 456)
                throw new DeepLQuotaExceededException("DeepL monthly character quota exceeded.");

            if (!response.IsSuccessStatusCode)
            {
                string body = "";
                try { body = await response.Content.ReadAsStringAsync(); } catch { }
                FileLogger.Log($"[WunderLanguage] DeepL {(int)response.StatusCode} for \"{text.Substring(0, Math.Min(60, text.Length))}\" body={body}");
                response.EnsureSuccessStatusCode();
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var translations = doc.RootElement.GetProperty("translations");
            string english = translations[0].GetProperty("text").GetString() ?? string.Empty;
            return ApplyZeldaTitleFixup(text) ?? ApplyZeldaNounFixups(english, text);
        }

        // M8 moved: title/noun pack lives in Packs/Zelda/ZeldaPack.cs. Same pairs, same gate.
        private static string? ApplyZeldaTitleFixup(string japaneseSource) => Packs.PackRegistry.Current.ApplyTitleFixup(japaneseSource);
        private static string ApplyZeldaNounFixups(string english, string japaneseSource) => Packs.PackRegistry.Current.ApplyEnNounFixups(english, japaneseSource);

        public async Task<(long used, long limit)> GetUsageAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v2/usage");
            request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            long used = doc.RootElement.GetProperty("character_count").GetInt64();
            long limit = doc.RootElement.GetProperty("character_limit").GetInt64();
            return (used, limit);
        }
    }
}