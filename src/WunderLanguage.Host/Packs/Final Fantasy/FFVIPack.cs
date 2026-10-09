namespace WunderLanguage.Host.Packs.FFVI
{
    public static class FfviPack
    {
        public static readonly (string from, string to)[] OcrFixups =
        {
            ("魔勢アーマー", "魔導アーマー"),
            ("ボイント", "ポイント"),
            ("まどう ?", "まほう?"),
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
            // Battle action garbles (cited: panel/screenshot loops this chat).
            ("ただからひっさつねざ", "たたかうひっさつわざ"),
            ("ただからひっきつねぎ", "たたかうひっさつわざ"),
            ("たたかうひっきつれだ", "たたかうひっさつわざ"),
            ("たたからひっさつねざ", "たたかうひっさつわざ"),
            ("ただからひっきつれざ", "たたかうひっさつわざ"),
            ("ただからひっきつねざ", "たたかうひっさつわざ"),
            // Standalone ability garbles (cited: 19/6/6 fires): normalize to
            // the canonical form so the glossary hits every loop (no CAT/DeepL
            // flicker). Substring shape also repairs merged forms containing them.
            ("ひっさつねざ", "ひっさつわざ"),
            ("ひっきつれざ", "ひっさつわざ"),
            ("ひっきうねぎ", "ひっさつわざ"),
            // Screenshot-cited 2026-09-26: merged + standalone forms of the
            // ひっきつかさ flicker (ability row came through raw, unpinned).
            ("ただからひっきつかさ", "たたかうひっさつわざ"),
            ("ひっきつかさ", "ひっさつわざ"),
            // Grey-item garbles (cited: dim battle-unusable rows — panel shot,
            // live log, crop replication each gave a different misread).
            ("スートボウク", "オートボウガン"),
            ("スートボウル", "オートボウガン"),
            // Panel-cited 2026-09-26: dim オートボウガン read ブートだった
            // (EN went "I was a boot."). Same shape, literal row as always.
            ("ブートだった", "オートボウガン"),
            // Panel-cited 2026-09-26: same row read ブートボウル ("Boot Bowl").
            ("ブートボウル", "オートボウガン"),
            // Log-cited 2026-09-26: same row read スートファンの.
            ("スートファンの", "オートボウガン"),
            // Log-cited 2026-09-28: grey オートボウガン stably misread
            // データの (10+ consecutive loops, panel-proven). Distinctive;
            // action-slot risk nil (that text never occurs there).
            ("データの", "オートボウガン"),
            // Log-cited 2026-09-28: お母＋ fragment read of ダッシューズ.
            ("お母ッシューズ", "ダッシューズ"),
            // Log-cited 2026-09-28: same row read オートだった / オートバック,
            // ハイポーション read ハイホーション, bare オートボウガン read
            // イートボック. Same shape, literal rows as always.
            ("オートだった", "オートボウガン"),
            ("オートバック", "オートボウガン"),
            ("イートボック", "オートボウガン"),
            // Scroll-cited 2026-09-29 (log, stable across loops): more
            // オートボウガン misreads (ボッカ―/ボウン/ネット variants).
            ("オートボッカ―", "オートボウガン"),
            ("オートボウン", "オートボウガン"),
            ("オートネット", "オートボウガン"),
            // Scroll-cited 2026-10-01 (log 00:35:44-50, alternating with
            // clean reads loop-to-loop): fullwidth-A-prefixed ハイポーション
            // read Ａイホージョン, オートボウガン read オートボッカー.
            ("Ａイホージョン", "ハイポーション"),
            ("オートボッカー", "オートボウガン"),
            // Bottom-cited 2026-10-01 (screenshots: bottom rows are
            // ナイトのこころえ/ダガー/レザーアーマー/ガントレット/
            // かわのぼうし/めぐすり): grey レザーアーマー reads
            // スレサーテーマー (00:36 fire, stable 16 loops),
            // サーフォーマー/サーフォーマーク (00:54:15-18); grey
            // オートボウガン reads プートだったら (00:53:54-54:01,
            // between ハイポーション and エアナイフ = its home row).
            ("スレサーテーマー", "レザーアーマー"),
            ("サーフォーマー", "レザーアーマー"),
            ("サーフォーマーク", "レザーアーマー"),
            ("プートだったら", "オートボウガン"),
            // Scroll-cited 2026-10-01 (log 01:14, stable across loops):
            // スートだったら (right row 2, between ハイポーション and
            // エアナイフ) and オートボウガンら (mined-fixup residual of
            // オートだったら) → オートボウガン; ナイトのころえ (4th
            // sighting shape of the ナイト row, after フェニックスのお =
            // its home) → ナイトのこころえ; シガー (3rd left row =
            // ダガー home, mid-list screenshot) → ダガー (dialogue risk:
            // cigar-talk would rewrite — zero cites in FFVI corpus;
            // revert phrase covers).
            ("スートだったら", "オートボウガン"),
            ("オートボウガンら", "オートボウガン"),
            ("ナイトのころえ", "ナイトのこころえ"),
            ("シガー", "ダガー"),
            // Bottom-cited 2026-10-01 (log 11:32:46-56, stable 6+ loops in
            // the ナイト slot after フェニックスのお): ごくごめんなさい
            // → ナイトのこころえ. Distinctive; apology-shaped soup never
            // occurs as a real item name.
            ("ごくごめんなさい", "ナイトのこころえ"),
            // Scroll-cited 2026-10-01 (log 12:14:07-17, stable across loops
            // in right row 2 = オートボウガン home): フートボウル →
            // オートボウガン (ブート/フート family). Pack-side permanence
            // for two mined/sidecar-covered reads: ハイボーション →
            // ハイポーション (ボ/ポ voicing, same shape as ハイホーション),
            // スートボック → オートボウガン (スート family + ボック).
            ("フートボウル", "オートボウガン"),
            ("ハイボーション", "ハイポーション"),
            ("スートボック", "オートボウガン"),
            // Scroll-cited 2026-10-01 (log 14:02, row-homes stable):
            // バイボーション (row 1 right ×2) → ハイポーション (voicing
            // family: ハイホーション/ハイボーション/Ａイホージョン
            // precedents). ホーション was cited here too but REVERTED
            // 2026-10-01: proven inventory-ambiguous (5-char shape reads as
            // ポーション-voicing-loss at P-slots just as often; corrupted
            // right row 1 + stole P's count two runs running). Ambiguous
            // shapes stay blank per policy — no literal row either way.
            // メブートボット (row 2 right ×2) → オートボウガン
            // (ブート×2 + フートボウル family).
            ("バイボーション", "ハイポーション"),
            ("メブートボット", "オートボウガン"),
            // Scroll-cited 2026-10-01 (log 14:11:24-39, row-2 home,
            // alternating stable ×4): オートボウカリ → オートボウガン
            // (ン/リ flicker). Bottom-cited 2026-10-01 (log 14:11:58+,
            // stable 6 loops in bottom-left rows): レヴーアーマー →
            // レザーアーマー (ザ/ヴ katakana confusion).
            ("オートボウカリ", "オートボウガン"),
            ("レヴーアーマー", "レザーアーマー"),
            // Scroll-cited 2026-10-01 (log 15:59:43+, stable ×6 in right
            // row 2): ムオートボット → オートボウガン (ム prefix +
            // オートボット; メブートボット/スートボック family).
            // Pack permanence for a mined-covered read (mine table already
            // maps it; host must own it too): オートだったら →
            // オートボウガン (プートだったら/スートだったら family).
            ("ムオートボット", "オートボウガン"),
            ("オートだったら", "オートボウガン"),
            // Weapon-open peeking tail (cited 2026-10-02 screenshots: bottom
            // item row [やまびこ|けむりだま] peeks under the weapon box,
            // stable 10+ loops): grey misreads map canonical so the tail
            // posts instead of blanking via the item canonical policy.
            // けむりたま carries the usual soup caveat (たま = ball is a real
            // word; dialogue about smoke balls ~never — same call as シガー).
            ("やまりこえんまくこっ", "やまびこえんまく"),
            ("けむりたま", "けむりだま"),
            // Weapon glove variant (cited 2026-10-03 panel: "シメタルナックル",
            // stable — glove misread as シ this time). Full-string distinctive;
            // generic シ-strip would eat shield-class names, so literal row.
            ("シメタルナックル", "メタルナックル"),
            // Scroll-cited 2026-10-01 (log 19:44:38+, stable 7 loops in
            // bottom-right last row): けむソだま → けむりだま (り/ソ
            // confusion). Bottom-cited same run (stable 7 loops, bottom-left
            // last row): やまびこえんまくうっ → やまびこえんまく (うっ
            // suffix). Both nonsense shapes outside items — zero risk.
            ("けむソだま", "けむりだま"),
            ("やまびこえんまくうっ", "やまびこえんまく"),
            // Pack permanence for the single most-read H form (cited dozens
            // of loops; sidecar/mine already cover it, host must own it —
            // without this row the keep-set blanks left row 1 entirely).
            ("ハイホーション", "ハイポーション"),
            // Scroll-cited 2026-10-01 (log 12:46:29-33, stable 3 loops in
            // right row 2): イートボット → オートボウガン (イートボック
            // family + ト).
            ("イートボット", "オートボウガン"),
            // Panel-cited 2026-10-09 (FFVI Narshe run screenshots, on-screen
            // truth): Biggs intro + guard 魔導アーマー lines. Narrow shapes;
            // outputs match no from-pattern (idempotent, verified).
            ("いて突っこむ", "して突っこむ"),
            ("突っこむサコには", "突っこむザコには"),
            ("かまっな", "かまうな"),
            ("鹿野アニマー", "魔導アーマー"),
            ("アニマー", "アーマー"),
            ("サルシェ", "ナルシェ"),
            ("どうとうこの", "とうとうこの"),
        };
        // Battle command vocabulary (cited): fight / ability / magic / item.
        // Row order is positional: [0] fight, [1..2] abilities, [3] magic, [4] item.
        public static readonly string[] BattleCommands = { "たたかう", "ひっさつわざ", "ぬすむ", "まほう", "アイテム" };
        // Battle monster vocabulary (cited: panel/screenshot/log loops this
        // chat — OCR flickers エビオルニス/エピオルニス/エヒオルニス/
        // エヒプルニス/エビオリニス/エロストリニス and シビーラビット/シピーラビット/
        // ンビーラビット, so all variants split). Pre-warms the session memo
        // so cold multi-type fights split one-line-each on first sight.
        public static readonly string[] MonsterVocabulary = { "エビオルニス", "エピオルニス", "エヒオルニス", "エヒプルニス", "エビオリニス", "エロストリニス", "シビーラビット", "シピーラビット", "ンビーラビット" };
        // Battle item vocabulary (cited: item-box screenshots, incl. scrolled
        // rows 2026-09-26). だっシューズ rides as a spelling variant (game
        // pixels read ダッシューズ; both split). デッシューズ (log-cited
        // 2026-09-28 misread) and タッシューズ (log-cited 2026-09-29
        // misread) ride too, split-only. Extend as scrolling reveals more.
        public static readonly string[] ItemVocabulary = { "ポーション", "ハイポーション", "ねぶくろ", "テント", "フェニックスのお", "オートボウガン", "エアナイフ", "ダッシューズ", "だっシューズ", "デッシューズ", "タッシューズ", "ナイトのこころえ", "ガントレット", "ダガー", "かわのぼうし", "レザーアーマー", "めぐすり", "やまびこえんまく", "どくけし", "けむりだま" };
        // Weapon-select slot labels (cited: equip box). ひだりで rides along:
        // OCR reads ひだりて's て as で about half the time.
        public static readonly string[] WeaponLabels = { "みぎて", "ひだりて", "ひだりで" };
        // Battle UI glossary (cited: FFVI official command/item names).
        // ひっさつわざ pinned per user: Special move (was Blitz).
        // Exact-match only — merged rows and names stay on the live path.
        public static readonly (string ja, string en)[] BattleGlossary =
        {
            ("たたかう", "Fight"),
            ("ひっさつわざ", "Special move"),
            ("ぬすむ", "Steal"),
            ("まほう", "Magic"),
            ("アイテム", "Item"),
            // Party name pins (user-cited 2026-10-09: official manual
            // literal EN forms — manual differs from Woolsey, manual wins.
            // Replaces the earlier Matthew pin per the same user decision.)
            ("マッシュ", "Mash"),
            ("ティナ", "Tina"),
            ("ロック", "Lock"),
            ("モグ", "Mog"),
            ("エドガー", "Edgar"),
            ("カイエン", "Cayenne"),
            ("ガウ", "Gau"),
            ("セリス", "Celes"),
            ("セッツァー", "Setzer"),
            ("シャドウ", "Shadow"),
            ("ストラゴス", "Stragus"),
            ("リルム", "Relm"),
            ("ポーション", "Potion"),
            ("ハイポーション", "Hi-Potion"),
            ("ねぶくろ", "Sleeping Bag"),
            ("テント", "Tent"),
            ("フェニックスのお", "Phoenix Down"),
            ("オートボウガン", "Auto Crossbow"),
            ("エアナイフ", "Air Knife"),
            ("ダッシューズ", "Dash Shoes"),
            ("だっシューズ", "Dash Shoes"),
            // Scrolled rows (cited 2026-09-26). ナイトのこころえ deliberately
            // unglossed (no certain EN) — live translation covers it.
            ("ガントレット", "Gauntlet"),
            ("ダガー", "Dagger"),
            ("かわのぼうし", "Leather Hat"),
            ("レザーアーマー", "Leather Armor"),
            ("めぐすり", "Eye Drops"),
            // Restored-save inventory (cited 2026-10-01 screenshot, exact
            // strings; official EN).
            ("やまびこえんまく", "Echo Screen"),
            ("どくけし", "Antidote"),
            ("けむりだま", "Smoke Bomb"),
            ("みぎて", "Right"),
            ("ひだりて", "Left"),
            ("ひだりで", "Left"),
            // Weapon-select equipped name (cited 2026-10-02 screenshot:
            // メタルナックル under みぎて, left hand empty). Official EN.
            ("メタルナックル", "Metal Knuckle"),
        };
        // Dialogue name glossary (user-cited 2026-10-09: official manual
        // literal EN). Masked through live translation (see MaskNames) —
        // NOT merged into BattleGlossary (row-exact mechanism differs).
        public static readonly (string ja, string en)[] NameGlossary =
        {
            ("ティナ", "Tina"),
            ("ロック", "Lock"),
            ("モグ", "Mog"),
            ("エドガー", "Edgar"),
            ("マッシュ", "Mash"),
            ("カイエン", "Cayenne"),
            ("ガウ", "Gau"),
            ("セリス", "Celes"),
            ("セッツァー", "Setzer"),
            ("シャドウ", "Shadow"),
            ("ストラゴス", "Stragus"),
            ("リルム", "Relm"),
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
        public static string ApplyEnNounFixups(string english, string japaneseSource)
        {
            // Party name pin (user-cited): マッシュ renders Matthew everywhere,
            // including dialogue lines that never pass the battle glossary.
            if (japaneseSource.Contains("マッシュ", System.StringComparison.Ordinal))
                english = System.Text.RegularExpressions.Regex.Replace(english, @"\bMassh\b|\bMash\b", "Matthew");
            return english;
        }
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
        public System.Collections.Generic.IReadOnlyList<string> BattleCommands => FfviPack.BattleCommands;
        public System.Collections.Generic.IReadOnlyList<string> MonsterVocabulary => FfviPack.MonsterVocabulary;
        public System.Collections.Generic.IReadOnlyList<string> ItemVocabulary => FfviPack.ItemVocabulary;
        public System.Collections.Generic.IReadOnlyList<string> WeaponLabels => FfviPack.WeaponLabels;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> BattleGlossary => FfviPack.BattleGlossary;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> NameGlossary => FfviPack.NameGlossary;        // Vision item 3 calibration: neutral (today's globals exactly —
        // no threshold, no inset, no suppression, unscaled). Tuned only
        // per citation, never preemptively.
        public double BoxScoreThreshold => 0.0;
        public double BoxInsetPx => 0.0;
        public double NmsIou => 1.0;
        public double FontScale => 1.0;
        public bool IsMenuHeaderRow(string ascii) => FfviPack.IsMenuHeaderRow(ascii);
    }
}