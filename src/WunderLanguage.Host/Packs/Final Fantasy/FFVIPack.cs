namespace WunderLanguage.Host.Packs.FFVI
{
    public static class FfviPack
    {
        public static readonly (string from, string to)[] OcrFixups =
        {
            ("魔勢アーマー", "魔導アーマー"),
            ("ボイント", "ポイント"),
            ("まどう ?", "まほう?"),
            ("ハイボーション", "ハイポーション"),
            ("セーブボイント", "セーブポイント"),
            ("セージすることができます", "セーブすることができます"),
            ("まみがえらせた", "よみがえらせた"),
            ("あやまちを９返そう", "あやまちをくり返そう"),
            ("唐草の力", "魔導の力"),
            ("鹿野の力", "魔導の力"),
            ("魔革アーマー", "魔導アーマー"),
            ("恐しい", "恐ろしい"),
            ("ロボガー", "エドガー"),
            ("とうしたの？", "どうしたの？"),
            ("思いだぜない", "思い出せない"),
            ("娘をだぜ", "娘をだせ"),
            ("ごっちじゃ", "こっちじゃ"),
            ("かっところで", "か。ところで"),
            ("かからだが", "からだが"),
            ("鹿草アーマー", "魔導アーマー"),
            ("ガストラ帝国", "ガストラール帝国"),
            ("ガストラ皇帝バンザ", "ガストラール皇帝バンザイ"),
            ("ログサー「", "ロック「"),
            ("ごっちは", "こっちは"),
            ("パナッ", "バナン"),
            ("ナルシェヘ行こう", "ナルシェへ行こう"),
            ("リターチー", "リターナー"),
            ("アフィガロ", "フィガロ"),
            ("魔活", "魔法"),
            ("間はますか", "要りますか"),
            ("もずかしい", "むずかしい"),
            ("しと月を同時に押せば", "LとRを同時に押せば"),
            ("造具屋", "道具屋"),
            ("通異星", "道具屋"),
            ("ガストラ帝国", "ガストラール帝国"),
            ("アッシュ", "マッシュ"),
            ("エドザー", "エドガー"),
            ("マッコ", "マッシュ"),
            ("パナン様", "バナン様"),
            ("娘をうれてまいりました", "娘をつれてまいりました"),
            ("ロボー", "エドガー"),
            ("実然", "突然"),
            ("急な出勤", "急な出撃"),
            ("チリケな城", "ちっぽけな城"),
            ("魔角士", "魔導士"),
            ("ナフォ", "ナフカ"),
            ("ナフカ", "ケフカ"),
            ("ケフォ", "ケフカ"),
            ("ェドガー", "エドガー"),
            ("ェどガー", "エドガー"),
            ("魔草の力", "魔導の力"),
            ("反布団体制", "反帝国体制"),
            ("リラーナー", "リターナー"),
            ("表向後は", "表向きは"),
            ("めすむ", "ぬすむ"),
            ("オートボクガン", "オートボウガン"),
            ("見送わめて", "見極めて"),
            ("エポクスプレス", "エクスプレス"),
            ("遠早屋", "道具屋"),
            ("道屋屋", "道具屋"),
            ("適具屋", "道具屋"),
            ("適男屋", "道具屋"),
            ("週男屋", "道具屋"),
            ("チョコボク", "チョコボの"),
            ("道皇屋", "道具屋"),
            ("道星屋", "道具屋"),
            ("適用屋", "道具屋"),
            ("通常屋", "道具屋"),
        };
        public static readonly (string from, string to)[] DisplayFixups = { };
        public const string TitleMarker = "FINAL FANTASY";
        public const string FullTitle = "FINAL FANTASY VI";
        public static readonly (string ja, string en)[] TitleMap =
        {
            ("FINAL FANTASY VI", "Final Fantasy VI"),
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
        public static string? ApplyTitleFixup(string japaneseSource) => null;
        public static string ApplyEnNounFixups(string english, string japaneseSource) => english;
        public static readonly string[] Signatures = { "ティナ", "ケフカ", "フィガロ", "ナルシェ", "リターナー", "バナン", "ガストラ" };
        public static bool KeepSpeakerBrackets => true;
        public static string ApplyGameOverRowFixups(string p) => p;
        public static bool IsMenuHeaderRow(string ascii) => UniversalPack.IsMenuHeaderRow(ascii);
    }
    public sealed class FfviPackInstance : IPack
    {
        public static readonly FfviPackInstance Shared = new();
        public string GameId => Packs.PackRegistry.FfviGameId;
        public System.Collections.Generic.IReadOnlyList<(string from, string to)> OcrFixups => FfviPack.OcrFixups;
        public System.Collections.Generic.IReadOnlyList<(string from, string to)> DisplayFixups => FfviPack.DisplayFixups;
        public string TitleMarker => FfviPack.TitleMarker;
        public string FullTitle => FfviPack.FullTitle;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> TitleMap => FfviPack.TitleMap;
        public bool IsTransitionBlend(string text) => FfviPack.IsTransitionBlend(text);
        public string ApplySweepFixups(string fixed_) => FfviPack.ApplySweepFixups(fixed_);
        public string ApplyGuardFixups(string fixed_) => FfviPack.ApplyGuardFixups(fixed_);
        public string ApplyDaughterFixups(string fixed_) => FfviPack.ApplyDaughterFixups(fixed_);
        public string ApplySacrificeFixups(string fixed_) => FfviPack.ApplySacrificeFixups(fixed_);
        public string ApplyDecisionFixups(string fixed_) => FfviPack.ApplyDecisionFixups(fixed_);
        public string ApplyRewardFixups(string fixed_) => FfviPack.ApplyRewardFixups(fixed_);
        public string ApplyButtonContextFixups(string fixed_) => FfviPack.ApplyButtonContextFixups(fixed_);
        public string ApplyVoiceTailFixups(string fixed_) => FfviPack.ApplyVoiceTailFixups(fixed_);
        public string? ApplyTitleFixup(string japaneseSource) => FfviPack.ApplyTitleFixup(japaneseSource);
        public string ApplyEnNounFixups(string english, string japaneseSource) => FfviPack.ApplyEnNounFixups(english, japaneseSource);
        public string ApplyGameOverRowFixups(string p) => FfviPack.ApplyGameOverRowFixups(p);
        public bool KeepSpeakerBrackets => FfviPack.KeepSpeakerBrackets;
        public System.Collections.Generic.IReadOnlyList<string> Signatures => FfviPack.Signatures;
        public bool IsMenuHeaderRow(string ascii) => FfviPack.IsMenuHeaderRow(ascii);
    }
}