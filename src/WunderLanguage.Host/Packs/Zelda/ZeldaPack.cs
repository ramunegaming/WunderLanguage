namespace WunderLanguage.Host.Packs.Zelda
{
    public static class ZeldaPack
    {
        public static readonly (string from, string to)[] OcrFixups =
        {
            ("Ｐ人", "７人"), ("『人", "７人"),
            ("段者", "賢者"), 
            ("批印", "封印"), ("樹印", "封印"), ("針印", "封印"),
            ("計印", "封印"), ("打印", "封印"), ("朝印", "封印"), ("抽印", "封印"), ("納印", "封印"),
            ("掛印", "封印"), ("同祭", "司祭"),
            ("もルダ", "ゼルダ"),
            ("ナジの同禁", "ナゾの司祭"), ("ナジの司祭", "ナゾの司祭"), ("ナツの司祭", "ナゾの司祭"),
            ("アゲニム", "アグニム"),
            ("させばられる", "ささげられる"),
            ("わきでできた", "わきでてきた"),
            ("みっかり", "みつかり"),
            ("いまました", "いました"),
            ("全知金能", "全知全能"),
            ("持つ勇金が", "持つ黄金が"),
            ("地かのとこかに", "地方のどこかに"),
            ("土方ゼルダ", "王女ゼルダ"), ("王女もルダ", "王女ゼルダ"),
            ("イケニュ", "イケニエ"), ("イケーエ", "イケニエ"),
            ("理論の中", "運命の時"),
            ("要しき", "悪しき"),
            ("一旦度", "２度"), ("全知金結", "全知全能"), ("責金", "黄金"), ("遺金", "黄金"),
            ("樹凪", "封印"), ("ナゾの同窓", "ナゾの司祭"), ("ナジの同窓", "ナゾの司祭"),
            ("カップの司祭", "ナゾの司祭"),
            ("カップの同窓", "ナゾの司祭"), ("仮説となった", "伝説となった"),
            ("ひく披露を", "ひく娘達を"),
            ("ひく焼きを", "ひく娘達を"), ("ささがあれる", "ささげられる"),
            ("運命の時点", "運命の時も"), ("運命の明る", "運命の時も"),
            ("ほうなり", "ほうむり"),
            ("望月は", "２度と"),
            ("たくらも、", "たくらむ、"), ("カジの司祭", "ナゾの司祭"),
            ("姉の兵達", "城の兵達"), ("姉達", "娘達"),
            ("ナゾの同禁", "ナゾの司祭"),
            ("イグニエ", "イケニエ"),
            ("全部全能", "全知全能"),
            ("おやつって", "あやつって"),
            ("こしかし", "しかし"),
            ("出しかし", "しかし"),
            ("ほうむ刀", "ほうむり"),
            ("かみつかり", "がみつかり"),
            ("どこがに", "どこかに"),
            ("腎諸", "賢者"),
            ("イゲニエ", "イケニエ"),
            ("脱毛", "時も"),
            ("習慣者", "７賢者"),
            ("エスセルダ", "王女ゼルダ"),
            ("理論の時", "運命の時"),
            ("もうそう", "もうそこ"),
            ("運命の嫌も", "運命の時も"),
            ("運命の晩も", "運命の時も"),
            ("ナツの同窓", "ナゾの司祭"),
            ("国王を戻ったのです。", "国王をほうむり…。"),
            ("ほうむ丁寧", "ほうむり…"),
            ("ぽうむりゃ", "ほうむり…"),
            ("ぼうむリー", "ほうむり…"),
            ("ぽうむリー", "ほうむり…"),
            ("ぼうむすぎ", "ほうむり…"),
            ("ぼうなり", "ほうむり"),
            ("ほうむリー", "ほうむり…"),
            ("ぼうむりー", "ほうむり…"),
            ("ほうむりー", "ほうむり…"),
            ("運命の塊も", "運命の時も"),
            ("ささがあられる", "ささげられる"),
            ("エ女セルダ", "王女ゼルダ"),
            ("エケゼルダ", "王女ゼルダ"),
            ("兵童", "兵達"),
            ("１ヶ二工", "イケニエ"),
            ("作戦者の皿をひく物音", "７賢者の血をひく娘達"),
            ("ほうをリー", "ほうむり"),
            ("社仰", "封印"),
            ("社印", "封印"),
            ("封印を再び聞こう", "封印を再び開こう"),
            ("人れんぞ", "入れんぞ"),
            ("剣と眉", "剣と盾"),
            ("島原ボタン", "Ｂボタン"),
            ("ロボタンを", "Ｂボタンを"),
            ("平日ボタンを", "Ｂボタンを"),
            ("線をみつけた", "娘をみつけた"),
            ("地下ろうに揃え", "地下ろうに捕え"),
            ("姉にやって来た司祭", "城にやって来た司祭"),
            ("カンララ", "カンテラ"),
            ("Ｗボタンでとうろう", "Ｙボタンでとうろう"),
            ("ゼルダ嫌", "ゼルダ姫"),
            ("ほうびとして１０００ルピー", "ほうびとして１００ルピー"),
            ("ドダンを押し続け", "Ｂボタンを押し続け"),
            ("ＰＨＳＨ", "ＰＵＳＨ"),
            ("セープ", "セーブ"),
            ("セップ", "セーブ"),
            ("セッブ", "セーブ"),
            ("もうそこまで止まっていました", "もうそこまでせまっていました"),
            ("剣と首", "剣と盾"),
            ("よくきげ", "よくきけ"),
            ("品はポタシ", "Ｂボタン"),
            ("ポタン", "ボタン"),
            ("ポタシ", "ボタン"),
            ("昨日Ｐを手に入れた", "ＭＡＰを手に入れた"),
            ("マップを手に入れた", "ＭＡＰを手に入れた"),
            ("ルビー", "ルピー"),
            ("とりせる", "とらせる"),
            ("ほっび", "ほうび"),
            ("４０人のイケニエ", "６人のイケニエ"),
            ("４ ０人のイケニエ", "６人のイケニエ"),
            ("Ｘポダン", "Ｘボタン"),
            ("出席ボタン", "Ｂボタン"),
            ("おホタン", "Ｂボタン"),
            ("ガギ", "カギ"),
            ("ブーダラン", "ブーメラン"),
            ("ＳＴＡＨＩＴ", "ＳＴＡＲＴ"),
            ("１０人のイケニエ", "６人のイケニエ"),
            ("きいこの１人", "さいごの１人"),
            ("プニメラン", "ブーメラン"),
            ("ブニメラン", "ブーメラン"),
            ("人のんぞ", "入れんぞ"),
            ("小牧んぞ", "入れんぞ"),
            ("っ、この剣", "こ、この剣"),
            ("たすけてください！", "たすけてください…"),
            ("たずけて", "たすけて"),
            ("ねおさい", "ねなさい"),
            ("黒板ボタン", "Ｂボタン"),
            ("ロボタン", "Ｂボタン"),
            ("目ボタン", "Ｂボタン"),
            ("自ボタン", "Ｂボタン"),
            ("自ポタン", "Ｂボタン"),
            ("日ボタン", "Ｂボタン"),
            ("日ポタン", "Ｂボタン"),
            ("昨年ＰＣ", "ＭＡＰ"),
            ("ゼルダ郷", "ゼルダ姫"),
            ("うう……", "うう…"),
            ("姫の……。", "姫の… … 。"),
            ("姫の……", "姫の… …"),
            ("ちゃんときこえた…う、ぜんぜん", "ちゃんときこえた…うん、ぜんぜん"),
            ("ちゃんときこえましたか？う、ぜんぜん", "ちゃんときこえましたか？うん、ぜんぜん"),
            ("ＨＴタン", "Ａボタン"),
            ("ＨＴボタン", "Ａボタン"),
            ("知ってるかし？", "知ってるかい？"),
            ("らせね", "らむね"),
            ("力ギ", "カギ"),
            ("せんぜん", "ぜんぜん"),
            ("セ一ブ", "セーブ"),
            ("賢くはいいんぞ", "城へは入れんぞ"),
            ("宝箱が、聞くはすだ", "宝箱が、開くはずだ"),
            ("ぽっぱり", "ほうむり…"),
            ("うどんぜん", "うん、ぜんぜん"),
            ("宝箱が、聞く", "宝箱が、開く"),
            ("小さなカギでは聞かない", "小さなカギでは開かない"),
            ("ショウ前を開く", "ジョウ前を開く"),
            ("物にわって", "物にさわって"),
            ("一つ、この剣と盾を持って行け", "…こ、この剣と盾を持って行け"),
        };
        public static readonly (string from, string to)[] DisplayFixups =
        {
            ("タメになるぞ……お前、知ってるだろ！", "タメになるぞ … お前、知ってるだろ！"),
            ("地図が見れるぜ……お前知ってるかい？", "地図が見れるぜ … … お前知ってるかい？"),
            ("地図が見れるぜ……お前、知ってるかい？", "地図が見れるぜ … … お前、知ってるかい？"),
            ("かつげたりするぜ……お前、知ってるかい？", "かつげたりするぜ … お前　知ってるかい？"),
            ("ブーメランだ…ＳＴＡＲＴボタンで、選んで使ってみよう…", "ブーメランだ！ＳＴＡＲＴボタンで、選んで使ってみよう！"),
            ("私の名前はゼルダ…６人のイケニエがささげられ私がさいごの１人…", "私の名前はゼルダ… …６人のイケニエがささげられ私がさいごの１人…"),
            ("たすけてください…私は、お城の地下ろうに捕えられています。", "たすけてください… 私は、お城の地下ろうに捕えられています。"),
            ("中でたすけて…", "中… たすけて… …"),
            ("ちゃんときこえた？う、ぜんぜん", "ちゃんときこえた？うん、ぜんぜん"),
            ("連れて行ってく。さあ", "連れて行って。さあ"),
            ("地図の上で光る点はたぶん、目的地だ。お前、知ってるかい？", "地図の上で光る点はたぶん　目的地だ　…　… お前　知ってるかい？"),
            ("姫の…。", "姫の… … 。"),
            ("… こら。こら、城へは入れんぞ子どもは、早く帰ってねなさい！", "こら こら 城へは入れんぞ 子どもは 早く帰ってねなさい！"),
            ("… こら、こら。城へは入れんぞ子どもは、早く帰ってねなさい！", "こら こら 城へは入れんぞ 子どもは 早く帰ってねなさい！"),
            ("… こら、こら。城へは入れんぞ子どもは早く帰ってねなさい！！", "こら こら 城へは入れんぞ 子どもは 早く帰ってねなさい！"),
        };
        public const string TitleMarker = "トライフォ";
        public const string FullTitle = "ゼルダの伝説 神々のトライフォース";
        public static bool IsTransitionBlend(string text)
        {
            return text.Contains("みつかり", System.StringComparison.Ordinal) && text.Contains("多くの", System.StringComparison.Ordinal);
        }
        public static bool IsMenuHeaderRow(string ascii)
        {
            string t = ascii.Trim();
            if (t.Length == 0) return false;
            string u = t.ToUpperInvariant();
            if (u == "MAP" || u == "START" || u == "PUSH" || u == "SELECT" || u == "COPY" || u == "KILL") return true;
            if (u.Contains("GAME OVER") || u.Contains("PLAYER") || u.Contains("SELECT") || u.Contains("EQUIPMENT") || u.Contains("DUNGEON") || u.Contains("ITEM")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"(?<![A-Z])DO(?![A-Z])", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Z ]{4,}$")) return true;
            return false;
        }
        public static string ApplySweepFixups(string fixed_)
        {
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"(?!さいご)[きさ]い[ごこ](?=の１人|の人)", "さいご");
            fixed_ = fixed_.Replace("物にきわって", "物にさわって", System.StringComparison.Ordinal);
            fixed_ = fixed_.Replace("現在地と主体が助かる", "現在地と全体がわかる", System.StringComparison.Ordinal);
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"[セゼ]ルダ[姫雑]", "ゼルダ姫");
            return fixed_;
        }
        public static string ApplyGuardFixups(string fixed_)
        {
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"[人入][れわね数物]んぞ", "入れんぞ");
            fixed_ = fixed_.Replace("小さなカキでは", "小さなカギでは", System.StringComparison.Ordinal);
            if (fixed_.Contains("開かない", System.StringComparison.Ordinal))
                fixed_ = fixed_.Replace("前を聞く", "前を開く", System.StringComparison.Ordinal);
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"爽やは[りれ][れわ]んぞ", "城へは入れんぞ");
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"嫁へは[人入][れわ]んぞ", "城へは入れんぞ");
            return fixed_;
        }
        public static string ApplyDaughterFixups(string fixed_)
        {
            fixed_ = fixed_.Replace("血をひく姉をみつけた", "血をひく娘をみつけた", System.StringComparison.Ordinal);
            return fixed_;
        }
        public static string ApplySacrificeFixups(string fixed_)
        {
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"\d+\s*人のイケニエがささげられ私がさいご", "６人のイケニエがささげられ私がさいご");
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"(?!６人)\d+人のイケニエがささげられ", "６人のイケニエがささげられ");
            return fixed_;
        }
        public static string ApplyDecisionFixups(string fixed_)
        {
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"ちゃんときこえた[…\.．]+う、ぜんぜん", "ちゃんときこえた…うん、ぜんぜん");
            return fixed_;
        }
        public static readonly (string ja, string en)[] TitleMap =
        {
            ("ゼルダの伝説 神々のトライフォース", "The Legend of Zelda: Triforce of the Gods"),
            ("ゼルダの伝説", "The Legend of Zelda"),
        };
        public static string? ApplyTitleFixup(string japaneseSource)
        {
            string t = japaneseSource.Trim();
            foreach (var (ja, en) in TitleMap)
                if (t == ja) return en;
            return null;
        }
        public static string ApplyEnNounFixups(string english, string japaneseSource)
        {
            if (!(japaneseSource.Contains("ハイラル", System.StringComparison.Ordinal)
                || japaneseSource.Contains("アグニム", System.StringComparison.Ordinal)
                || japaneseSource.Contains("ゼルダ", System.StringComparison.Ordinal)))
                return english;
            english = System.Text.RegularExpressions.Regex.Replace(english, @"\b[Hh]iranal\b", "Hyrule");
            english = System.Text.RegularExpressions.Regex.Replace(english, @"\bHilar\b", "Hyrule");
            english = System.Text.RegularExpressions.Regex.Replace(english, @"\bAgnim\b", "Agahnim");
            return english;
        }
        public static readonly string[] Signatures = { "ゼルダ", "ハイラル", "トライフォース", "アグニム" };
        public static readonly string[] BattleCommands = { };
        public static readonly string[] MonsterVocabulary = { };
        public static readonly string[] ItemVocabulary = { };
        public static readonly string[] WeaponLabels = { };
        public static readonly (string ja, string en)[] BattleGlossary = { };
        public static bool KeepSpeakerBrackets => false;
        public static string ApplyGameOverRowFixups(string p)
        {
            if (p.Contains("しないで続けろ", System.StringComparison.Ordinal)) p = p.Replace("しないで続けろ", "しないで続ける", System.StringComparison.Ordinal);
            return p;
        }
        public static string ApplyRewardFixups(string fixed_)
        {
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"ほ[うら]びとして１０００ルピー", "ほうびとして１００ルピー");
            fixed_ = fixed_.Replace("力をだめる", "力をためる", System.StringComparison.Ordinal);
            return fixed_;
        }
        public static string ApplyButtonContextFixups(string fixed_)
        {
            fixed_ = fixed_.Replace("物にさわってＢボタンを押す", "物にさわってＡボタンを押す", System.StringComparison.Ordinal);
            fixed_ = fixed_.Replace("宝箱の前でＢボタンを押す", "宝箱の前でＡボタンを押す", System.StringComparison.Ordinal);
            return fixed_;
        }
        public static string ApplyVoiceTailFixups(string fixed_)
        {
            fixed_ = System.Text.RegularExpressions.Regex.Replace(fixed_, @"お前、(?=知ってる|忘れる)", "お前 ");
            return fixed_;
        }
    }
    public sealed class ZeldaPackInstance : Packs.IPack
    {
        public static readonly ZeldaPackInstance Shared = new();
        public string GameId => Packs.PackRegistry.ZeldaGameId;
        public System.Collections.Generic.IReadOnlyList<(string from, string to)> OcrFixups => ZeldaPack.OcrFixups;
        public System.Collections.Generic.IReadOnlyList<(string from, string to)> DisplayFixups => ZeldaPack.DisplayFixups;
        public string TitleMarker => ZeldaPack.TitleMarker;
        public string FullTitle => ZeldaPack.FullTitle;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> TitleMap => ZeldaPack.TitleMap;
        public bool IsTransitionBlend(string text) => ZeldaPack.IsTransitionBlend(text);
        public string ApplySweepFixups(string fixed_) => ZeldaPack.ApplySweepFixups(fixed_);
        public string ApplyGuardFixups(string fixed_) => ZeldaPack.ApplyGuardFixups(fixed_);
        public string ApplyDaughterFixups(string fixed_) => ZeldaPack.ApplyDaughterFixups(fixed_);
        public string ApplySacrificeFixups(string fixed_) => ZeldaPack.ApplySacrificeFixups(fixed_);
        public string ApplyDecisionFixups(string fixed_) => ZeldaPack.ApplyDecisionFixups(fixed_);
        public string ApplyRewardFixups(string fixed_) => ZeldaPack.ApplyRewardFixups(fixed_);
        public string ApplyButtonContextFixups(string fixed_) => ZeldaPack.ApplyButtonContextFixups(fixed_);
        public string ApplyVoiceTailFixups(string fixed_) => ZeldaPack.ApplyVoiceTailFixups(fixed_);
        public string? ApplyTitleFixup(string japaneseSource) => ZeldaPack.ApplyTitleFixup(japaneseSource);
        public string ApplyEnNounFixups(string english, string japaneseSource) => ZeldaPack.ApplyEnNounFixups(english, japaneseSource);
        public string ApplyGameOverRowFixups(string p) => ZeldaPack.ApplyGameOverRowFixups(p);
        public bool KeepSpeakerBrackets => ZeldaPack.KeepSpeakerBrackets;
        public System.Collections.Generic.IReadOnlyList<string> Signatures => ZeldaPack.Signatures;
        public System.Collections.Generic.IReadOnlyList<string> BattleCommands => ZeldaPack.BattleCommands;
        public System.Collections.Generic.IReadOnlyList<string> MonsterVocabulary => ZeldaPack.MonsterVocabulary;
        public System.Collections.Generic.IReadOnlyList<string> ItemVocabulary => ZeldaPack.ItemVocabulary;
        public System.Collections.Generic.IReadOnlyList<string> WeaponLabels => ZeldaPack.WeaponLabels;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> BattleGlossary => ZeldaPack.BattleGlossary;
        public System.Collections.Generic.IReadOnlyList<(string ja, string en)> NameGlossary => new System.Collections.Generic.List<(string ja, string en)>();
        // Vision item 3 calibration: neutral (learning mode estimates,
        // publish requires — nothing tuned yet).
        public double BoxScoreThreshold => 0.0;
        public double BoxInsetPx => 0.0;
        public double NmsIou => 1.0;
        public double FontScale => 1.0;
        public bool IsMenuHeaderRow(string ascii) => ZeldaPack.IsMenuHeaderRow(ascii);
    }
}