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
            ("させばられる", "ささげられる"), ("ささばられる", "ささげられる"),
            // Intro 1:1 (revert: revert intro-punct): block-4 read inserts か
            // (消えていきました vs screen 消えていました, user-confirmed
            // 2026-10-20). Scoped to 消えて — real きました words untouched.
            ("消えていきました", "消えていました"),
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
            // 2026-10-22 live evidence (corpus L54, posted as a duplicate of
            // intro block 4 at 02:41:28): the 4→5 crossfade frame OCRs as
            // ハロハのノハ” + the block-4 line with a いきました tail — the
            // growth machinery then holds it as a NEW line (prefix grew) and
            // flushes it next fire. Strip the ghost prefix and normalize the
            // tail so the variant collapses into the already-fired line
            // (dedup catches it). Deeper fix = crossfade-frame suppression in
            // growth (G1 audit, CaptureController 1349-1392).
            // Revert: revert intro-dup.
            ("ハロハのノハ”", ""),
            ("聖地へと消えていきました", "聖地へと消えていました"),
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
            // Intro 1:1 punctuation (revert: revert intro-punct): the OCR's
            // language model fabricates punctuation the screen doesn't have —
            // commas at wide gaps (game renders 　), trailing 。 on
            // sentence-complete rows, trailing ！ where the screen has ….
            // Restore the game's true form. All keys are intro-unique
            // substrings (tail-agnostic where tails vary between reads).
            ("はるか昔、山と森にかこまれた美しき、ハイラル", "はるか昔　山と森にかこまれた美しき　ハイラル"),
            ("ハイラルの大地！", "ハイラルの大地…"),
            ("しかし、戻ってきた者", "しかし　戻ってきた者"),
            ("そこで、国の王は", "そこで　国の王は"),
            ("そこで国の王は", "そこで　国の王は"),
            ("封印させたそうです。", "封印させたそうです"),
            ("封印は、２度", "封印は　２度"),
            ("伝説となったころ！", "伝説となったころ…"),
            // 2026-10-21 live evidence (corpus L101): raw ends ！？ — the
            // pair above consumes ！ first, leaving ？ after …; the screen
            // ends at … (user spec: no ? here). Key is the post-pair state,
            // so it runs after the ！ pair regardless of raw variant.
            // Revert: revert intro-punct.
            ("伝説となったころ…？", "伝説となったころ…"),
            ("もうそこまでせまっていました。", "もうそこまでせまっていました"),
            // 2026-10-21 live evidence (corpus L181): OCR misread
            // せ→は on this row (もうそうまではまっていました。) — the
            // correct read (L173) lost the growth race to the misread.
            // Restore the screen's true row; trailing 。 dies via the
            // せまって above or included here. Revert: revert intro-punct.
            ("もうそこまではまっていました。", "もうそこまでせまっていました"),
            ("もうそうまではまっていました。", "もうそこまでせまっていました"),
            // Intro 1:1 (revert: revert intro-punct): しかし-line tail —
            // screen has no trailing 。 (user spec 2026-10-20); OCR
            // fabricates it 4/7 reads. Scoped to the わきでてきた clause.
            ("わきでてきたと言うことです。", "わきでてきたと言うことです"),
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
        // Z2 5-box inventory feed (revert: revert z2-5box-view): the
        // inventory screen's chrome + the one live field. Detection = the
        // static mid row (ペンダント — present in 100% of the corpus's
        // inventory fires) plus a second chrome marker in another row, so
        // ordinary menus that merely carry はなす/よむ as words never
        // route here. The item = first remaining row; anything else is
        // residual (label over omission — never dropped).
        private static readonly string[] InventoryActionMarkers = { "ひっぱる", "かつぐ", "よむ", "はなす" };
        // Mockup chrome (zelda-inventory-mockup.svg, user 2026-10-22):
        // visual lines as drawn — line 1 carries かつぐ.１ よむ はなす,
        // line 2 ひっぱる.
        private static readonly string[] InventoryActionRows = { "かつぐ.１　よむ　はなす", "ひっぱる" };
        private const string InventoryMidStatic = "ペンダント";
        private const string InventoryBottomRight = "EQUIPMENT";

        public static bool TryInventoryLayout(string jaJoined, string enJoined, out Packs.InventoryLayout layout)
        {
            layout = null!;
            if (string.IsNullOrWhiteSpace(jaJoined)) return false;
            string[] jaLines = jaJoined.Split('\n');
            string[] enLines = (enJoined ?? "").Split('\n');
            bool aligned = enLines.Length == jaLines.Length;

            int midIdx = -1;
            bool combo = false;
            for (int i = 0; i < jaLines.Length; i++)
            {
                if (midIdx < 0 && jaLines[i].Contains(InventoryMidStatic, StringComparison.Ordinal))
                {
                    midIdx = i;
                    continue;
                }
                if (IsInventoryAction(jaLines[i]) || IsInventoryLatin(jaLines[i])) combo = true;
            }
            if (midIdx < 0 || !combo) return false;

            int itemIdx = -1;
            var residual = new System.Collections.Generic.List<string>();
            for (int i = 0; i < jaLines.Length; i++)
            {
                if (i == midIdx) continue;
                string line = jaLines[i].Trim();
                if (line.Length == 0) continue;
                if (IsInventoryAction(line) || IsInventoryLatin(line)) continue;
                if (itemIdx < 0) itemIdx = i;
                else residual.Add(line);
            }

            string itemJa = itemIdx >= 0 ? jaLines[itemIdx].Trim() : "";
            // EN is row-aligned with JA (the controller joins both from the
            // same rows); the JP-first pending marker arrives unaligned —
            // fall back to the whole EN so the strip never drops it.
            string itemEn = (aligned && itemIdx >= 0 && !string.IsNullOrWhiteSpace(enLines[itemIdx]))
                ? enLines[itemIdx]
                : (enJoined ?? "");

            layout = new Packs.InventoryLayout
            {
                ItemJa = itemJa,
                ItemEn = itemEn,
                ActionRows = InventoryActionRows,
                MidText = InventoryMidStatic,
                BottomRight = InventoryBottomRight,
                Residual = string.Join("\n", residual),
            };
            return true;
        }

        private static bool IsInventoryAction(string line)
        {
            foreach (string m in InventoryActionMarkers)
                if (line.Contains(m, StringComparison.Ordinal)) return true;
            return false;
        }

        // EQUIPMENT garbles on the real screen (ＥＧＵＩＰＭＥＮＴ /
        // ＥａｂｌｅｔｎＥｘＴ / ＤＶＮＧＥＯＮＩＴＥＭ): >=4 latin letters,
        // fullwidth included — chrome, never the item.
        private static bool IsInventoryLatin(string line)
        {
            int latin = 0;
            foreach (char c in line)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                    || (c >= 'Ａ' && c <= 'Ｚ') || (c >= 'ａ' && c <= 'ｚ')) latin++;
            }
            return latin >= 4;
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
        public bool TryInventoryLayout(string jaJoined, string enJoined, out Packs.InventoryLayout layout) => ZeldaPack.TryInventoryLayout(jaJoined, enJoined, out layout);
        public bool IsMenuHeaderRow(string ascii) => ZeldaPack.IsMenuHeaderRow(ascii);
    }
}