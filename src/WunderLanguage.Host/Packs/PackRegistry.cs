namespace WunderLanguage.Host.Packs
{
    public static class PackRegistry
    {
        public const string ZeldaGameId = "zelda";
        public const string FfviGameId = "ffvi";
        public const string PmGameId = "papermario";
        public static string DefaultGameId => ZeldaGameId;
        public static IPack Current { get; private set; } = Zelda.ZeldaPackInstance.Shared;
        public static bool IsKnown(string gameId) => Resolve(gameId) is not UniversalPackInstance;
        // Session hook (called once at controller Start): window titles carry ROM names
        // (Zelda_no_Densetsu..._-_Snes9x...). Unknown games get universals-only, never wrong-pack guesses.
        public static IPack Resolve(string gameId)
        {
            string u = (gameId ?? "").ToUpperInvariant();
            if (u.Contains("ZELDA") || u.Contains("DENSETSU")) return Zelda.ZeldaPackInstance.Shared;
            if (u.Contains("FFVI") || u.Contains("FANTASY_VI")) return FFVI.FfviPackInstance.Shared;
            if (u.Contains("PAPERMARIO") || u.Contains("ペーパーマリオ") || u.Contains("G8MJ01")) return PaperMario.PaperMarioPackInstance.Shared;
            return UniversalPackInstance.Shared;
        }
        public static void SetSession(string gameId) { Current = Resolve(gameId); try { FileLogger.Log($"[pack] session {gameId} -> {Current.GameId}"); } catch { } }
        public static void SetCurrent(IPack pack) { try { Current = pack; FileLogger.Log($"[pack] content switch -> {pack.GameId}"); } catch { } }
        // Content fingerprinting (fullscreen-proof identity): sustained proper-noun votes
        // overrule stale titles. Ties/zero keep current (sticky). Generic words never qualify.
        public static IPack? ScoreContent(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return null;
                int z = 0, f = 0, p = 0;
                foreach (var s in Zelda.ZeldaPack.Signatures) if (text.Contains(s, System.StringComparison.Ordinal)) z++;
                foreach (var s in FFVI.FfviPack.Signatures) if (text.Contains(s, System.StringComparison.Ordinal)) f++;
                foreach (var s in PaperMario.PaperMarioPack.Signatures) if (text.Contains(s, System.StringComparison.Ordinal)) p++;
                if (f >= 2 && f > z && f > p) return FFVI.FfviPackInstance.Shared;
                if (z >= 2 && z > f && z > p) return Zelda.ZeldaPackInstance.Shared;
                if (p >= 2 && p > z && p > f) return PaperMario.PaperMarioPackInstance.Shared;
                return null;
            }
            catch { return null; }
        }
    }
}