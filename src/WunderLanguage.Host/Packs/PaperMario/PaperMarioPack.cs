namespace WunderLanguage.Host.Packs.PaperMario
{
    // Paper Mario pack v1 (2026-10-13, onboarding Step 8): checklist order.
    // 1. Signatures (evidenced proper nouns only — generic words never route).
    // 2. Title triple (logo fragment marker, canonical full title, EN map).
    // 3. Fixups: EMPTY (miner file accrues under the stable ROM-id; mined
    //    pairs promote via loop 1 when shared). 4. Vocabs: battle command
    //    + item tabs evidenced in screenshots/corpus. 5-7. Glossaries:
    //    EMPTY (EN forms unverified — user confirms before banking).
    // 8. Vision calibration: neutral (learning estimates, publish requires).
    // Everything unverified stays empty rather than guessed.
    // Revert: delete file + registry refs (falls back to universal).
    public static class PaperMarioPack
    {
        public static readonly (string from, string to)[] OcrFixups = { };
        public static readonly (string from, string to)[] DisplayFixups = { };
        public const string TitleMarker = "ペーパーマリオ";
        public const string FullTitle = "ペーパーマリオRPG";
        public static readonly (string ja, string en)[] TitleMap =
        {
            ("ペーパーマリオRPG", "Paper Mario: The Thousand-Year Door"),
        };
        public static bool IsTransitionBlend(string text) => false;
        public static string ApplySweepFixups(string fixed_) => fixed_;
        public static string ApplyGuardFixups(string fixed_) => fixed_;
        public static string ApplyDaughterFixups(string fixed_) => fixed_;
        public static string ApplySacrificeFixups(string fixed_) => fixed_;
        public static string ApplyDecisionFixups(string fixed_) => fixed_;
        public static string ApplyRewardFixups(string fixed_) => fixed_;
        public static string ApplyButtonContextFixups(string fixed_) => fixed_;
        public static string ApplyVoiceTailFixups(string fixed_) => fixed_;
        public static string? ApplyTitleFixup(string japaneseSource)
        {
            string t = japaneseSource.Trim();
            foreach (var (ja, en) in TitleMap)
                if (t == ja) return en;
            return null;
        }
        public static string ApplyEnNounFixups(string english, string japaneseSource) => english;
        public static string ApplyGameOverRowFixups(string p) => p;
        public static bool IsMenuHeaderRow(string ascii)
        {
            string t = ascii.Trim();
            if (t.Length == 0) return false;
            string u = t.ToUpperInvariant();
            if (u.Contains("GAME OVER") || u.Contains("PLAYER") || u.Contains("SELECT") || u.Contains("ITEM")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Z ]{4,}$")) return true;
            return false;
        }
        public static readonly string[] Signatures = { "マリオ", "ピーチ姫", "スターストーン", "ゴロツキタウン", "キノじい" };
        // ジャンプ: sole evidenced battle command (speech-bubble selector).
        public static readonly string[] BattleCommands = { "ジャンプ" };
        public static readonly string[] MonsterVocabulary = { };
        // Evidenced menu tabs (もちもの / アイテム / たいせつなもの / たびのきろく).
        public static readonly string[] ItemVocabulary = { "もちもの", "アイテム", "たいせつなもの", "たびのきろく" };
        public static readonly string[] WeaponLabels = { };
        public static readonly (string ja, string en)[] BattleGlossary = { };
        public static bool KeepSpeakerBrackets => false;
    }
    public sealed class PaperMarioPackInstance : IPack
    {
        public static readonly PaperMarioPackInstance Shared = new();
        public string GameId => "papermario";
        public System.Collections.Generic.IReadOnlyList<(string from, string to)> OcrFixups => PaperMarioPack.OcrFixups;
        public System.Collections.Generic.IReadOnlyList<(string from, string to)> DisplayFixups => PaperMarioPack.DisplayFixups;
        public string TitleMarker => PaperMarioPack.TitleMarker;
        public string FullTitle => PaperMarioPack.FullTitle;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> TitleMap => PaperMarioPack.TitleMap;
        public bool IsTransitionBlend(string text) => PaperMarioPack.IsTransitionBlend(text);
        public string ApplySweepFixups(string fixed_) => PaperMarioPack.ApplySweepFixups(fixed_);
        public string ApplyGuardFixups(string fixed_) => PaperMarioPack.ApplyGuardFixups(fixed_);
        public string ApplyDaughterFixups(string fixed_) => PaperMarioPack.ApplyDaughterFixups(fixed_);
        public string ApplySacrificeFixups(string fixed_) => PaperMarioPack.ApplySacrificeFixups(fixed_);
        public string ApplyDecisionFixups(string fixed_) => PaperMarioPack.ApplyDecisionFixups(fixed_);
        public string ApplyRewardFixups(string fixed_) => PaperMarioPack.ApplyRewardFixups(fixed_);
        public string ApplyButtonContextFixups(string fixed_) => PaperMarioPack.ApplyButtonContextFixups(fixed_);
        public string ApplyVoiceTailFixups(string fixed_) => PaperMarioPack.ApplyVoiceTailFixups(fixed_);
        public string? ApplyTitleFixup(string japaneseSource) => PaperMarioPack.ApplyTitleFixup(japaneseSource);
        public string ApplyEnNounFixups(string english, string japaneseSource) => PaperMarioPack.ApplyEnNounFixups(english, japaneseSource);
        public string ApplyGameOverRowFixups(string p) => PaperMarioPack.ApplyGameOverRowFixups(p);
        public bool KeepSpeakerBrackets => PaperMarioPack.KeepSpeakerBrackets;
        public System.Collections.Generic.IReadOnlyList<string> Signatures => PaperMarioPack.Signatures;
        public System.Collections.Generic.IReadOnlyList<string> BattleCommands => PaperMarioPack.BattleCommands;
        public System.Collections.Generic.IReadOnlyList<string> MonsterVocabulary => PaperMarioPack.MonsterVocabulary;
        public System.Collections.Generic.IReadOnlyList<string> ItemVocabulary => PaperMarioPack.ItemVocabulary;
        public System.Collections.Generic.IReadOnlyList<string> WeaponLabels => PaperMarioPack.WeaponLabels;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> BattleGlossary => PaperMarioPack.BattleGlossary;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> NameGlossary => new System.Collections.Generic.List<(string ja, string en)>();
        public double BoxScoreThreshold => 0.0;
        public double BoxInsetPx => 0.0;
        public double NmsIou => 1.0;
        public double FontScale => 1.0;
        public bool IsMenuHeaderRow(string ascii) => PaperMarioPack.IsMenuHeaderRow(ascii);
    }
}
