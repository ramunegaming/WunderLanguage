using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WunderLanguage.Host.Translation
{
    // Local cache so we never pay to translate the same line twice, and so
    // repeated Argos calls don't redo work either. Backed by a JSON file for
    // now - fine at this scale. We'll fold this into the SQLite-backed
    // notes/vocab database once that exists, since it's really the same
    // "text we've seen and translated" data.
    public class TranslationCache
    {
        private readonly string _filePath;
        private readonly Dictionary<string, string> _cache;

        public TranslationCache(string filePath = "translation_cache.json")
        {
            _filePath = filePath;
            _cache = LoadFromDisk();
        }

        public bool TryGet(string sourceText, string targetLang, out string translated)
        {
            return _cache.TryGetValue(MakeKey(sourceText, targetLang), out translated!);
        }

        public void Set(string sourceText, string targetLang, string translated)
        {
            _cache[MakeKey(sourceText, targetLang)] = translated;
            SaveToDisk();
        }

        // Single-file, namespaced cache: keys look like
        //   deepl-v2:EN:<japanese>
        //   py-xxxxxxxx:EN:<japanese>
        // Switching providers can never poison each other. Old scopes
        // (legacy deepl:, orphaned py-*) are deleted here to stop
        // unbounded growth. Idempotent - safe to call twice.
        public int PruneExcept(ISet<string> allowedScopes)
        {
            var toRemove = new List<string>();
            foreach (var key in _cache.Keys)
            {
                bool keep = false;
                foreach (var scope in allowedScopes)
                {
                    if (key.StartsWith(scope + ":", System.StringComparison.Ordinal))
                    {
                        keep = true;
                        break;
                    }
                }
                if (!keep)
                    toRemove.Add(key);
            }

            foreach (var key in toRemove)
                _cache.Remove(key);

            if (toRemove.Count > 0)
                SaveToDisk();

            return toRemove.Count;
        }

        private static string MakeKey(string sourceText, string targetLang) => $"{targetLang}:{sourceText}";

        private Dictionary<string, string> LoadFromDisk()
        {
            if (!File.Exists(_filePath))
                return new Dictionary<string, string>();

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch
            {
                // Corrupt cache file - start fresh rather than crash the app over it.
                return new Dictionary<string, string>();
            }
        }

        private void SaveToDisk()
        {
            var json = JsonSerializer.Serialize(_cache);
            File.WriteAllText(_filePath, json);
        }
    }
}