using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WunderLanguage.Host.Ocr;
using WunderLanguage.Host.Translation;

namespace WunderLanguage.Host.Capture
{
    public class CaptureController
    {
        private readonly IntPtr _hWnd;
        private readonly GameRegion _gameRegion;
        private readonly OcrClient _ocrClient;
        private readonly TranslationManager _translationManager;
        private readonly Func<IntPtr, bool>? _isCompanionWindow;
        private CancellationTokenSource? _cts;
        private readonly string _id = Guid.NewGuid().ToString("N")[..6];
        private volatile bool _stopped;
        private string _gameId;
        // Quiet-title detector (FFVI textless logo): prolonged zero-substance frames + logo hints.
        private int _quietCount;
        private bool _quietHint;
        private string _quietSeen = "";
        private readonly object _mineLock = new();
        private string _successorText = string.Empty;
        private int _successorCount;
        private int _weakHoldCount;
        private string _weakHoldFor = string.Empty;
        // S1.2 honest min-spec: worst-case warmup timings pick the profile once per session.
        // Full bar=12, reduced bar=18 (slower bubbles, never wrong ones). Omission-only if ever wrong.
        private int _weakHoldThreshold = 12;
        private double _warmDetSum;
        private double _warmRecSum;
        private int _warmSamples;
        private bool _minSpecLogged;
        private void MaybeEmitMinSpecVerdict()
        {
            try
            {
                if (_minSpecLogged || _settledOcrLoops < StartupWarmupOcrLoops || _warmSamples == 0) return;
                _minSpecLogged = true;
                double det = _warmDetSum / _warmSamples, rec = _warmRecSum / _warmSamples;
                bool reduced = det > 250 || rec > 800;
                if (reduced) _weakHoldThreshold = 18;
                FileLogger.Log($"[min-spec] profile={(reduced ? "reduced" : "full")} (warmup-mean det={det:F0}ms rec={rec:F0}ms over {_warmSamples}, weak-hold bar={_weakHoldThreshold})");
            }
            catch { }
        }
        private bool _flashFired;
        private string _lastOcrText = string.Empty;
        private int _lastOcrContent;
        private int _lastOcrTotal;
        private bool _focusHoldActive;

        public event Action<string, string>? TextTranslated;
        public event Action<string, string, string>? MenuTranslated; // (kind TITLE/MENU, jaJoined, enJoined)
        public event Action<string, string, List<string>, List<string>, List<string>, List<string>, List<string>, List<string>>? BattleFramePublished; // (topJa, topEn, monJa, monEn, actJa, actEn, rightJa, rightEn)
        public event Action<string, string, string, string>? MenuUpdated; // (expectedOldJa, kind, jaJoined, enJoined) update-only, never appends
        private string _menuSignature = "";
        private int _menuStableCount;
        private bool _frameIsTitle;
        // Battle-frame size for BattleFrame.cs (0 = unknown, battle logic stays off).
        private int _battleFrameW;
        private int _battleFrameH;
        // Battle-frame slot stability: 2 consistent reads to fire, then every
        // consistent change updates in place (never appends, so no feed spam).
        private string _battlePrevSig = "";
        private string _battleFiredSig = "";
        // Last seen monster rows within the current fight. The action menu covers
        // the monster box when open; stickiness keeps the frame steady instead of
        // blanking every time the player picks a command.
        private List<string> _battleMonsterSticky = new();
        // Menu grace after battle: transitions clear the fired mark before the
        // frame refires; menus stay silent a few loops more so no MENU dupe
        // lands mid-fight. Decremented per menu pass; battle frames never see it.
        private int _battleCooldown;
        // Slot EN stickiness: identical JA reuses last published EN verbatim.
        // Unreliable-tier translations are never cached by policy, so without
        // this every refire rolls fresh nondeterministic EN and the frame flickers.
        private string _lastBattleTopJa = "";
        private string _lastBattleTopEn = "";
        private List<string> _lastBattleMonJa = new();
        private List<string> _lastBattleMonEn = new();
        private List<string> _lastBattleActJa = new();
        private List<string> _lastBattleActEn = new();
        private List<string> _lastBattleRightJa = new();
        private List<string> _lastBattleRightEn = new();
        private string _lastPublishedMenu = "";
        // Newest DECISION sig (choice screens). Dialogue yields to it (D2/D3);
        // similarity-gated so no expiry bookkeeping needed.
        private string _lastDecisionSig = "";
        public event Action<string, string, string>? TextUpdated; // (expectedOldJa, newJa, en)
        public event Action? WindowLost;

        private int _consecutiveCaptureFailures;
        private const int MaxConsecutiveCaptureFailures = 5;

        private string? _lastFrameHash;
        private string? _pendingFrameHash;
        private int _pendingFrameStableCount;
        private const int RequiredPreOcrStablePolls = 1;
        // S1.1 startup warmup: settled-OCR count since Start (not wall time, so tired
        // laptops get the same loop-count warmup — slower in seconds, never wrong).
        private int _settledOcrLoops;
        private const int StartupWarmupOcrLoops = 8;

        private string _candidateText = string.Empty;
        private int _candidateStableCount;
        private bool _candidateGrewThisRead;
        private string _lastTranslatedText = string.Empty;
        // R1 oscillation memory: previously shown line (one slot). A stable candidate matching _prev
        // while another line is shown = same-box A-B-A flicker; the middle fire was the intruder.
        // Updated on every new-bubble path below; update-in-place paths keep lineage (untouched).
        private string _prevTranslatedText = string.Empty;
        // R2a shown-age: loop index of the last new-bubble fire (-100 = none yet, first fire immediate).
        // A held page flushing moments after a fire (corpus loops 457-458) is flicker, not a page turn.
        private int _lastFireLoop = -100;
        private const int RequiredStableReadings = 3;
        private const int GrowthBreakthroughPolls = 10;
        private const int MinCandidateContentLength = 2;

        private const int IdleDelayMs = 400;
        private const int SettlingDelayMs = 150;

        // M8-1 moved: table lives in Packs/Zelda/ZeldaPack.cs, delegate here. No behavior change.
        // M8f: tables read live from CurrentPack every call (a static snapshot froze the first pack forever — fixed).

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int nMaxCount);

        private static string ResolveGameId(IntPtr hWnd)
        {
            try
            {
                var sb = new StringBuilder(256);
                if (GetWindowText(hWnd, sb, sb.Capacity) > 0)
                {
                    string t = sb.ToString().Trim();
                    foreach (char c in Path.GetInvalidFileNameChars())
                        t = t.Replace(c, '_');
                    t = t.Replace(' ', '_');
                    if (!string.IsNullOrWhiteSpace(t))
                        return t.Length > 60 ? t.Substring(0, 60) : t;
                }
            }
            catch { }
            return $"hwnd-{hWnd:X}";
        }

        // Game identity heals mid-session (M8f): ctor may run pre-ROM-load ("Snes9x 1.60").
        // Cheap string compare only while stable; on ROM change, switch pack + miner map + rotate corpus.
        private void RefreshGameSession()
        {
            try
            {
                string fresh = ResolveGameId(_hWnd);
                var content = Packs.PackRegistry.ScoreContent(_lastOcrText ?? "");
                if (content != null && content.GameId != Packs.PackRegistry.Current.GameId)
                {
                    Packs.PackRegistry.SetCurrent(content);
                    try { LoadMinedCorrections(); } catch { }
                    _corpusPath = "";
                    // New game, new timeline: stale quiet must not mint a TITLE.
                    _quietCount = 0; _quietSeen = "";
                    return;
                }
                if (fresh == _gameId) return;
                FileLogger.Log($"[pack] game switch {_gameId} -> {fresh}");
                _gameId = fresh;
                try { Packs.PackRegistry.SetSession(_gameId); } catch { }
                try { LoadMinedCorrections(); } catch { }
                _corpusPath = "";
                _quietCount = 0; _quietSeen = "";
            }
            catch { }
        }

        private Dictionary<string, string> _minedCorrections = new();

        // M8 miner-as-pack-data: corrections/<gameId>.json is pack data (per-game, never core).
        // Dedupe: entries the current pipeline already reproduces are dropped at load
        // (universals absorb the rows — file preserved, effective map shrinks).
        private void LoadMinedCorrections()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage", "corrections", _gameId + ".json");
                if (File.Exists(path))
                    _minedCorrections = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>();
                int before = _minedCorrections.Count;
                _minedCorrections = _minedCorrections
                    .Where(kv => ApplyDisplayFixups(NormalizePunctForStability(ApplyZeldaOcrFixups(kv.Key))) != kv.Value)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                if (_minedCorrections.Count != before)
                    FileLogger.Log($"[miner] deduped {before - _minedCorrections.Count}/{before} redundant rows (pipeline absorbs them)");
            }
            catch { }
        }
        private string? LookupMined(string raw)
        {
            try
            {
                if (_minedCorrections.TryGetValue(raw, out string? f))
                    return f;
            }
            catch { }
            return null;
        }

        private void MineCorrection(string raw, string fixed_)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(raw) || raw == fixed_)
                    return;
                string stripRaw = StripForOverlap(raw).Replace("・", "").Replace("！", "").Replace("!", "").Replace("？", "").Replace("?", "").Replace("＂", "").Replace("\"", "").Replace("「", "").Replace("」", "").Replace("〈", "").Replace("〉", "").Replace("『", "").Replace("』", "").Replace("【", "").Replace("】", "").Replace("…", "").Replace("．", "").Replace(".", "");
                string stripFixed = StripForOverlap(fixed_).Replace("・", "").Replace("！", "").Replace("!", "").Replace("？", "").Replace("?", "").Replace("＂", "").Replace("\"", "").Replace("「", "").Replace("」", "").Replace("〈", "").Replace("〉", "").Replace("『", "").Replace("』", "").Replace("【", "").Replace("】", "").Replace("…", "").Replace("．", "").Replace(".", "");
                if (stripRaw == stripFixed)
                    return;
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage", "corrections");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, _gameId + ".json");
                lock (_mineLock)
                {
                    Dictionary<string, string> map;
                    try
                    {
                        if (File.Exists(path))
                            map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>();
                        else
                            map = new Dictionary<string, string>();
                    }
                    catch { map = new Dictionary<string, string>(); }
                    if (map.TryGetValue(raw, out string? prev) && prev == fixed_)
                        return;
                    FileLogger.Log($"[mine] \"{raw}\" -> \"{fixed_}\"");
                    map[raw] = fixed_;
                    File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch { }
        }

        private static string ApplyZeldaOcrFixups(string text)
        {
            string fixed_ = text;
            foreach (var (from, to) in Packs.PackRegistry.Current.OcrFixups)
            {
                if (fixed_.Contains(from, StringComparison.Ordinal))
                    fixed_ = fixed_.Replace(from, to, StringComparison.Ordinal);
            }
            if (!fixed_.Contains("封印", StringComparison.Ordinal))
                fixed_ = Regex.Replace(fixed_, @".{1,3}をとこう", "封印をとこう");
            fixed_ = fixed_.Replace("たくらも、", "たくらむ、", StringComparison.Ordinal);
            fixed_ = fixed_.Replace("カジの司祭", "ナゾの司祭", StringComparison.Ordinal);
            fixed_ = Regex.Replace(fixed_, @"イケ[ァ-ヶー]+", "イケニエ");
            // Generic families: shapes, not instances. New variants same shape fix day one.
            fixed_ = Regex.Replace(fixed_, @"[腎覧貿貮]者", "賢者");
            fixed_ = Regex.Replace(fixed_, @"[Ｐ『不]賢者", "７賢者");
            fixed_ = Regex.Replace(fixed_, @"司[宗同禁祭]", "司祭");
            fixed_ = Regex.Replace(fixed_, @"聖地への人[口力]", "聖地への入口");
            fixed_ = Regex.Replace(fixed_, @"[バパ]イラル", "ハイラル");
            fixed_ = Regex.Replace(fixed_, @"エ[女土]ゼルダ", "王女ゼルダ");
            fixed_ = Regex.Replace(fixed_, @"[選連通]命", "運命");
            // Sasage shape (corrected, log-named loop 277): ざ/ば/が insert before け — ささ[X]けられ → ささげられ.
            // Previous single-char class missed the insertion shape. Idempotent fixed-point (output re-matches to itself).
            fixed_ = Regex.Replace(fixed_, @"ささ[ざばが]?[けげ]られ", "ささげられ");
            fixed_ = Regex.Replace(fixed_, @"イケ[三二]工", "イケニエ");
            fixed_ = Regex.Replace(fixed_, @"[狼塊]達", "娘達");
            // M8-4b moved: daughter lives in Packs/Zelda/ZeldaPack.cs. Same position.
            fixed_ = Packs.PackRegistry.Current.ApplyDaughterFixups(fixed_);
            fixed_ = Regex.Replace(fixed_, @"カンラ[ラテ]ラ", "カンテラ");
            fixed_ = Regex.Replace(fixed_, @"[訪初射]印", "封印");
            // L1.3 glyph pair by token context (universal): seals open, visits don't.
            // 訪問 + open-verb → 封印. True 訪問 (no 開 after) passes through untouched.
            // Idempotent: output 封印 contains no 訪問, so no rematch.
            fixed_ = Regex.Replace(fixed_, @"訪問を再び開", "封印を再び開");
            fixed_ = Regex.Replace(fixed_, @"訪問を開", "封印を開");
            fixed_ = Regex.Replace(fixed_, @"司[^祭]{1,2}アグニム", "司祭アグニム");
            fixed_ = Regex.Replace(fixed_, @"[カガ]ンテラ", "カンテラ");
            fixed_ = Regex.Replace(fixed_, @"[プブ][ニ一]メラン", "ブーメラン");
            // M8-4b moved: sacrifice lives in Packs/Zelda/ZeldaPack.cs. Same position, same order.
            fixed_ = Packs.PackRegistry.Current.ApplySacrificeFixups(fixed_);
            // M8-4a moved: sweep lives in Packs/Zelda/ZeldaPack.cs. Same position, same order.
            fixed_ = Packs.PackRegistry.Current.ApplySweepFixups(fixed_);
            fixed_ = Packs.PackRegistry.Current.ApplyVoiceTailFixups(fixed_);
            fixed_ = Regex.Replace(fixed_, @"[日自目白ロ]ボタン", "Ｂボタン");
            fixed_ = Regex.Replace(fixed_, @"[HＨ][TＴ](?:オ)?(?:タン|ボタン)", "Ａボタン");
            fixed_ = Regex.Replace(fixed_, @"一官[ボポ]タン", "Ｂボタン");
            // L1.1 button-token rule (universal): <kanji/kana-token>ボタン → Ｂボタン.
            // B unless pack says otherwise. Fullwidth-Latin tokens (A/B/X/Y, START) never match here.
            // Idempotent: output Ｂボタン uses Latin Ｂ, outside [一-鿿ぁ-んァ-ヶ], so no rematch.
            // L1.1 repaired: hiragana excluded — verb endings/particles (て/け/で) are never button tokens.
            // Kanji/katakana only (二層/日本/黒田/ロ/日 all covered). Idempotent: output Ｂ is Latin, no rematch.
            fixed_ = Regex.Replace(fixed_, @"[一-鿿ァ-ヶ]{1,3}ボタン", "Ｂボタン");
            // L1.4 repaired (log-named loops 48/49/62/94): (?<![ボポ]) stops タン matching inside ボタン (no more ＢＢ);
            // hiragana excluded (no more て-eating: さわって/よくきけ/前で survive).
            // Idempotent: output Ｂボタン contains no bare タン outside ボタン, so no rematch.
            fixed_ = Regex.Replace(fixed_, @"[一-鿿ァ-ヶ]{1,3}(?:オ)?(?<![ボポ])タン(?=.*押す)", "Ｂボタン");
            // Pack-context buttons (screenshots pair 5/6 + ref image 4): lift/chest guides use Ａ. Output Ａ is Latin, no rule rematches.
            fixed_ = Packs.PackRegistry.Current.ApplyButtonContextFixups(fixed_);
            fixed_ = Regex.Replace(fixed_, @"９ＴＡＲＴ", "ＳＴＡＲＴ");
            // M8-4b moved: decision instance lives in Packs/Zelda/ZeldaPack.cs. Same position.
            fixed_ = Packs.PackRegistry.Current.ApplyDecisionFixups(fixed_);
            fixed_ = Regex.Replace(fixed_, @"[MＭｍｍ][AＡａａ][PＰｐｐ]", "ＭＡＰ");
            fixed_ = fixed_.Replace("昨年ＰＣ", "ＭＡＰ", StringComparison.Ordinal);
            // M8-4b moved: reward lives in Packs/Zelda/ZeldaPack.cs. Same position, same order.
            fixed_ = Packs.PackRegistry.Current.ApplyRewardFixups(fixed_);
            // L1.5 MAP-by-obtain-context (universal, log-named drift-95): you obtain MAP, never 昨年Ｐ.
            // Covers 昨年Ｐ/昨年ＰＣ/昨日Ｐ shapes with zero new rows per variant. Idempotent: output ＭＡＰ never rematches.
            fixed_ = Regex.Replace(fixed_, @"[昨映][年午日]?ＰＣ?を、?手に入れた", "ＭＡＰを手に入れた");
            // M8-4a moved: guard lives in Packs/Zelda/ZeldaPack.cs. Same position, same order.
            fixed_ = Packs.PackRegistry.Current.ApplyGuardFixups(fixed_);
            if (!ReferenceEquals(fixed_, text) && fixed_ != text)
                FileLogger.Log($"[ocr-fixup] \"{text}\" -> \"{fixed_}\"");
            return fixed_;
        }

        // M8-0 moved: table lives in Packs/Zelda/ZeldaPack.cs, delegate here. No behavior change.
        // M8f: see above — display table likewise live.

        private static string ApplyDisplayFixups(string text)
        {
            string fixed_ = text;
            foreach (var (from, to) in Packs.PackRegistry.Current.DisplayFixups)
            {
                if (fixed_.Contains(from, StringComparison.Ordinal))
                    fixed_ = fixed_.Replace(from, to, StringComparison.Ordinal);
            }
            return fixed_;
        }

        private static string NormalizePunctForStability(string text)
        {
            string s = text.Replace('・', '…').Replace('＂', '…').Replace('"', '…');
            // Doubled ！ is OCR doubling a true single (boomerang/MAP/cell):
            // collapse at line end, never convert to …. Mid-line runs are
            // dot-confusion → … (Zelda-4, uncle 剣よくきけ).
            s = Regex.Replace(s, @"[!！]{2,}$", "！");
            s = Regex.Replace(s, @"[!！]{2,}", "…");
            // JA 1:1 — ? runs stay verbatim (??? is real punctuation, never dot-confusion).
                        if (!Packs.PackRegistry.Current.KeepSpeakerBrackets)
                            s = s.Replace("「", "").Replace("」", "").Replace("〈", "").Replace("〉", "").Replace("『", "").Replace("』", "").Replace("【", "").Replace("】", "");
            // Trailing bare numbers (universal, FFVI battle artifacts ９２２５２５): damage/HP figures
            // glued to speech are never dialogue. Dates/coords carry non-digits, untouched.
            s = Regex.Replace(s, @"\s+[0-9０-９ ]{2,}\s*$", "");
            s = s.Replace('．', '…').Replace('.', '…');
            // Spaced fullwidth Latin is one token (Ｍ Ａ Ｐ → ＭＡＰ).
            string prev;
            do { prev = s; s = Regex.Replace(s, @"(?<=[Ａ-Ｚ]) (?=[Ａ-Ｚ])", ""); } while (s != prev);
            // Dot counts: 4+ → …… (opening), exactly 3 → … (uncle 剣),
            // 2+ before 。 → single (ほうむり…。), trailing run → single.
            s = Regex.Replace(s, @"…{4,}", "……");
            s = Regex.Replace(s, @"…{3}", "…");
            s = Regex.Replace(s, @"…{2,}(?=。)", "…");
            s = Regex.Replace(s, @"…{2,}$", "…");
            // … eats a following ！ (なかった……！ → なかった…); true ！ untouched.
            s = Regex.Replace(s, @"…+！", "…");
            return s;
        }

        public CaptureController(IntPtr hWnd, GameRegion gameRegion, OcrClient ocrClient, TranslationManager translationManager, Func<IntPtr, bool>? isCompanionWindow = null)
        {
            _hWnd = hWnd;
            _gameRegion = gameRegion;
            _ocrClient = ocrClient;
            _translationManager = translationManager;
            _isCompanionWindow = isCompanionWindow;
            _gameId = ResolveGameId(hWnd);
            FileLogger.Log(
                                $"CaptureController created id={_id}. hWnd=0x{hWnd:X}, gameRegion baseline=(L{_gameRegion.GetRelativeCropRect().Left},T{_gameRegion.GetRelativeCropRect().Top},W{_gameRegion.GetRelativeCropRect().Width},H{_gameRegion.GetRelativeCropRect().Height})");
        }

        public void Start()
        {
            FileLogger.Log("CaptureController.Start called");
            // Build capability line: logs self-identify which behavior set is live,
            // so a stale exe can never again masquerade as the current build.
            // Bump the tag on every behavior batch.
            try { FileLogger.Log($"[build] battle18 split+guard+grace+sticky+titleguard+overlap+rows+fanout+prefetch+romaji+rewardclean+hintgate+1read+adopt+digitsplit+fastrefresh+namesonly+topgate+gapblank+onerow+simmemo+siglog+fixedframe+fireorder+actionslot+wideact+splitmon+autofit+countattach+fitlog active (pack={Packs.PackRegistry.Current.GameId})"); } catch { }
            try { Packs.PackRegistry.SetSession(_gameId); } catch { }
            try { LoadMinedCorrections(); } catch { }
            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            FileLogger.Log($"CaptureController.Stop called id={_id}");
            _stopped = true;
            _cts?.Cancel();
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            FileLogger.Log("CaptureController loop starting (cc-converge-overlap)");
            int iteration = 0;

            while (!token.IsCancellationRequested)
            {
                iteration++;
                try
                {
                    // Focus gate: minimized or behind another window -> hold.
                    // No OCR, no publish, until the emulator is foreground.
                    if (WindowCapture.IsMinimized(_hWnd))
                    {
                        if (iteration % 20 == 0)
                            FileLogger.Log($"[loop {iteration}] emulator minimized, holding");
                        await Task.Delay(IdleDelayMs, token);
                        continue;
                    }
                    IntPtr fgFocus = WindowCapture.GetForegroundWindowHandle();
                    if (fgFocus != _hWnd && !(_isCompanionWindow?.Invoke(fgFocus) ?? false))
                    {
                        _focusHoldActive = true;
                        if (iteration % 20 == 0)
                            FileLogger.Log($"[loop {iteration}] emulator not focused (fg=0x{fgFocus:X}), holding");
                        await Task.Delay(IdleDelayMs, token);
                        continue;
                    }
                    if (_focusHoldActive)
                    {
                        _focusHoldActive = false;
                        _lastFrameHash = null;
                        _pendingFrameHash = null;
                        FileLogger.Log($"[loop {iteration}] refocus-catchup (stale frame dropped, OCR now)");
                    }
                    if (iteration % 20 == 0) RefreshGameSession();
                    using var bitmap = WindowCapture.CaptureWindowRegion(_hWnd, _gameRegion);
                    _consecutiveCaptureFailures = 0;

                    string frameHash = ComputeFrameHash(bitmap);
                    if (frameHash == _lastFrameHash)
                    {
                        if (iteration % 20 == 0)
                            FileLogger.Log($"[loop {iteration}] frame unchanged, skipping OCR");
                        string? quietTitleStatic = CheckQuietTitle(iteration, null);
                        // Same battle guard as the settled path: static cooldown screens
                        // must never mint a TITLE mid-fight (forces a dupe frame).
                        if (quietTitleStatic != null && string.IsNullOrEmpty(_battleFiredSig) && _battleCooldown <= 0) _ = TranslateAndPublishMenuAsync("TITLE", new List<(int y, string t, bool ja)> { (0, "", false) });
                        // Static dwell confirms a clean held candidate. Menu screens
                        // (sig set) and titles stay out; dupes stay out via guards.
                        if (!string.IsNullOrEmpty(_candidateText)
                            && _candidateStableCount >= 2
                            && _candidateStableCount < RequiredStableReadings
                            && string.IsNullOrEmpty(_menuSignature)
                            && !_frameIsTitle
                            && !IsDwellDashNoise(_candidateText)
                            && HasAnchorSubstance(_candidateText)
                            && HasJapanese(_candidateText)
                            && !IsJunkOcr(_candidateText) && !IsEllipsisOnly(_candidateText)
                            && !IsScrollBlend(_candidateText) && !IsZeldaTransitionBlend(_candidateText)
                            && !EndsWithContinuation(_candidateText) && !IsIncompleteSacrifice(_candidateText)
                            && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                            && (string.IsNullOrEmpty(_lastTranslatedText) || !_lastTranslatedText.Contains(_candidateText, StringComparison.Ordinal)))
                        {
                            _candidateStableCount++;
                            if (_candidateStableCount >= RequiredStableReadings)
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=publish-on-dwell (static frame confirms held line)");
                                _lastTranslatedText = _candidateText;
                                string dwellText = _candidateText;
                                _ = TranslateAndPublishAsync(dwellText);
                            }
                        }
                        await Task.Delay(IdleDelayMs, token);
                        continue;
                    }

                    if (frameHash == _pendingFrameHash)
                    {
                        _pendingFrameStableCount++;
                    }
                    else
                    {
                        _pendingFrameHash = frameHash;
                        _pendingFrameStableCount = 1;
                    }

                    if (_pendingFrameStableCount < RequiredPreOcrStablePolls)
                    {
                        await Task.Delay(SettlingDelayMs, token);
                        continue;
                    }

                    _lastFrameHash = frameHash;
                    FileLogger.Log($"[loop {iteration}] frame settled - running OCR (bitmap {bitmap.Width}x{bitmap.Height})");

                    var (rawText, ocrBlocks, contentWords, totalWords) = await _ocrClient.ExtractTextWithBlocksAsync(bitmap);
                    _settledOcrLoops++;
                    if (_settledOcrLoops <= StartupWarmupOcrLoops)
                    {
                        _warmDetSum += _ocrClient.LastDetMs;
                        _warmRecSum += _ocrClient.LastRecMs;
                        _warmSamples++;
                    }
                    _lastOcrText = rawText.Trim();
                    MaybeEmitMinSpecVerdict();
                    _lastOcrContent = contentWords;
                    _lastOcrTotal = totalWords;
                    string rawTrim = rawText.Trim();
                    string? remembered = null;
                    try { remembered = LookupMined(rawTrim); } catch { }
                    string extractedText = ApplyDisplayFixups(NormalizePunctForStability(ApplyZeldaOcrFixups(remembered ?? rawTrim)));
                    try { MineCorrection(rawTrim, extractedText); } catch { }
                    try { CorpusRecordOcr(iteration, rawTrim, ocrBlocks, extractedText, remembered != null); } catch { }
                    // Phase 1a probe only: shape signal for menus/titles, dialogue path below unchanged.
                    try
                    {
                        int jaBlocks = 0;
                        foreach (var b in ocrBlocks)
                            if (HasJapanese(b.Text)) jaBlocks++;
// REPLACE
                        var preview = new System.Text.StringBuilder();
                        int shown = 0;
                        int fw = 1, fh = 1;
                        try { fw = System.Math.Max(1, bitmap.Width); fh = System.Math.Max(1, bitmap.Height); } catch { }
                        try { _battleFrameW = fw; _battleFrameH = fh; } catch { }
                        foreach (var b in ocrBlocks)
                        {
                            if (shown >= 12) break;
                            string t = b.Text.Replace("\n", " ");
                            if (t.Length > 28) t = t.Substring(0, 28) + "…";
                            double nx = (double)b.X / fw, ny = (double)b.Y / fh;
                            preview.Append($" [b{shown} x={b.X} y={b.Y} w={b.Width} h={b.Height} nx={nx:0.00} ny={ny:0.00} ja={HasJapanese(b.Text)} \"{t}\"]");
                            shown++;
                        }
                        FileLogger.Log($"[blocks] total={ocrBlocks.Count} jaBlocks={jaBlocks} primaryJa={HasJapanese(extractedText)} frame={fw}x{fh}{preview}");
                        string? quietTitleSettled = CheckQuietTitle(iteration, ocrBlocks);
                        // A live battle owns the screen: the logo/title detector must
                        // never fire mid-fight (it forces a bogus TITLE plus a dupe
                        // frame). Battle-grace covers transitions too.
                        if (quietTitleSettled != null && string.IsNullOrEmpty(_battleFiredSig) && _battleCooldown <= 0) _ = TranslateAndPublishMenuAsync("TITLE", new List<(int y, string t, bool ja)> { (0, "", false) });
                    }
                    catch { }
                    // Battle-frame dialogue guard: bottom-band battle blocks never become
                    // dialogue; top-band rewards isolate and flow on. Wiring only — all
                    // geometry lives in BattleFrame.cs. Non-battle frames fall through.
                    try
                    {
                        string battleTop = "";
                        int battleClass = BattleFrame.Classify(ocrBlocks, _battleFrameW, _battleFrameH, HasJapanese, out battleTop);
                        if (battleClass == 2)
                        {
                            // Rewards live in the battle frame now (UpdateBattleFrame
                            // carries the top bar); they no longer dialogue-fire.
                            FileLogger.Log($"[battle] rewards frame owns top-bar (dialogue suppressed)");
                            extractedText = string.Empty;
                        }
                        else if (battleClass == 1)
                        {
                            if (!string.IsNullOrEmpty(extractedText))
                                FileLogger.Log($"[battle] bottom-frame suppressed from dialogue ({ocrBlocks.Count} blocks)");
                            extractedText = string.Empty;
                        }
                    }
                    catch { }
                    // Kana-chart gate (name entry etc.): merged-blob shape.
                    // Suppresses whole screen in both tiers: header, arrows,
                    // END and blobs cost nothing and post nothing.
                    if (IsKanaChartScreen(ocrBlocks))
                    {
                        FileLogger.Log($"[loop {iteration}] chart-suppressed (kana input grid)");
                        _menuStableCount = 0;
                        _menuSignature = "";
                        _candidateText = string.Empty;
                        _candidateStableCount = 0;
                        _candidateGrewThisRead = false;
                        _flashFired = false;
                        await Task.Delay(IdleDelayMs, token);
                        continue;
                    }
                    // Phase 1b: menu/title tracker. Dialogue path below frozen.
                    try { UpdateMenuTracker(ocrBlocks); } catch (Exception mex) { FileLogger.Log($"[menu] tracker error: {mex.Message}"); }
                    try { UpdateBattleFrame(ocrBlocks); } catch (Exception bex) { FileLogger.Log($"[battle] tracker error: {bex.Message}"); }
                    token.ThrowIfCancellationRequested();
                    FileLogger.Log($"[loop {iteration}] OCR result: \"{extractedText}\"");

                    if (string.IsNullOrWhiteSpace(extractedText))
                    {
                        if (_candidateStableCount >= RequiredStableReadings && !string.IsNullOrEmpty(_candidateText) && !_frameIsTitle
                            && _candidateText != _lastTranslatedText
                            && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                            && !IsEllipsisOnly(_candidateText) && !IsJunkOcr(_candidateText) && !IsScrollBlend(_candidateText) && !EndsWithContinuation(_candidateText) && !IsIncompleteSacrifice(_candidateText))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=flush-on-blank (publishing held line)");
                            _lastTranslatedText = _candidateText;
                            string flushText = _candidateText;
                            _ = TranslateAndPublishAsync(flushText);
                        }
                        _candidateText = string.Empty;
                        _candidateStableCount = 0;
                        _candidateGrewThisRead = false;
                        _flashFired = false;
                    }
                    else if (!string.IsNullOrEmpty(_candidateText)
                        && (ComputeSimilarity(extractedText, _candidateText) >= 0.65
                            || extractedText.Contains(_candidateText, StringComparison.Ordinal)
                            || _candidateText.Contains(extractedText, StringComparison.Ordinal)
                            || HasCommonPrefix(extractedText, _candidateText, 10)
                            || HasCommonPrefix(StripForOverlap(extractedText), StripForOverlap(_candidateText), 6)))
                    {
                        // Blend-stuck escape: dirty blend must not pin clean line.
                        if ((IsScrollBlend(_candidateText) || IsZeldaTransitionBlend(_candidateText))
                            && !(IsScrollBlend(extractedText) || IsZeldaTransitionBlend(extractedText)))
                        {
                            _candidateText = extractedText;
                            _candidateStableCount = 1;
                            _candidateGrewThisRead = false;
                            _flashFired = false;
                        }
                        else
                        {
                        // Same line re-read, growth step, or scroll settling.
                        // Growth (longer) and contained (cleaner) adopt at once
                        // so autoscroll tracks with no lag. Same-length variants
                        // (scroll-blur/flicker) settle-first: held until the SAME
                        // variant repeats, so one garbled frame can't overwrite.
                        _candidateStableCount++;
                        if (_candidateText.Contains(extractedText, StringComparison.Ordinal))
                        {
                            // R1 (steadiness): never trade a complete held line
                            // for a much-shorter contained tail (OCR head-loss).
                            // Normalization jitter (<6) still adopts as before.
                            if (IsCompleteDialogueLine(_candidateText)
                                && StripForOverlap(_candidateText).Length - StripForOverlap(extractedText).Length >= 6)
                            {
                                if (_successorText == extractedText)
                                    _successorCount++;
                                else
                                {
                                    _successorText = extractedText;
                                    _successorCount = 1;
                                }
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-short-tail (keeping complete held line)");
                                _candidateGrewThisRead = false;
                            }
                            else
                            {
                                _candidateText = extractedText;
                                _candidateGrewThisRead = false;
                                _successorText = string.Empty;
                                _successorCount = 0;
                            }
                        }
                        else if (extractedText.Length > _candidateText.Length)
                        {
                            // Flash-post (pair-2): a stable fragment absorbed by
                            // growth was really shown (telepathy flashes). Post
                            // once per lineage on substantial jumps only (no
                            // typewriter spam); the full line follows via update.
                            if (!_flashFired && _candidateStableCount >= RequiredStableReadings
                                && StripForOverlap(extractedText).Length - StripForOverlap(_candidateText).Length >= 6
                                && HasJapanese(_candidateText) && !IsJunkOcr(_candidateText)
                                && !IsEllipsisOnly(_candidateText) && !IsScrollBlend(_candidateText)
                                && !IsZeldaTransitionBlend(_candidateText) && !EndsWithContinuation(_candidateText)
                                && !IsIncompleteSacrifice(_candidateText) && IsCompleteDialogueLine(_candidateText) && !_frameIsTitle
                                && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                                && (string.IsNullOrEmpty(_lastTranslatedText) || !_lastTranslatedText.Contains(_candidateText, StringComparison.Ordinal)))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=flash-post (stable fragment absorbed by growth)");
                                _lastTranslatedText = _candidateText;
                                string flashText = _candidateText;
                                _ = TranslateAndPublishAsync(flashText);
                                _flashFired = true;
                            }
                            _candidateText = extractedText;
                            _candidateGrewThisRead = true;
                            _successorText = string.Empty;
                            _successorCount = 0;
                        }
                        else if (_successorText == extractedText)
                        {
                            _successorCount++;
                            if (_successorCount >= 2)
                            {
                                _candidateText = extractedText;
                                _successorText = string.Empty;
                                _successorCount = 0;
                            }
                            _candidateGrewThisRead = false;
                        }
                        else
                        {
                            _successorText = extractedText;
                            _successorCount = 1;
                            _candidateGrewThisRead = false;
                        }
                        }
                    }
                    else
                    {
                        // MILESTONE 4 (page-turn flush): substantial held line
                        // replaced by a dissimilar substantial line = the player
                        // turned the page. Publish the held page now; it will
                        // never stabilize again. Noise can't trigger this: both
                        // sides must be substantial, and menu screens are out.
                        if (!string.IsNullOrEmpty(_candidateText)
                            && IsSubstantialLine(_candidateText)
                            && IsSubstantialLine(extractedText)
                            && !IsGameOverText(extractedText)
                            && string.IsNullOrEmpty(_menuSignature)
                            && !_frameIsTitle
                            && !EndsWithContinuation(_candidateText)
                            && !IsScrollBlend(_candidateText) && !IsZeldaTransitionBlend(_candidateText)
                            && !IsIncompleteSacrifice(_candidateText)
                            && IsCompleteDialogueLine(_candidateText)
                            && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                            && (string.IsNullOrEmpty(_lastTranslatedText) || !_lastTranslatedText.Contains(_candidateText, StringComparison.Ordinal))
                            // R2a: shown line younger than 3 loops + dissimilar held page = lone intruder, hold it.
                            // True turns arrive with an old shown line and fire instantly, as before.
                            && (iteration - _lastFireLoop >= 3))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=flush-on-pageturn (publishing turned page)");
                            try { CorpusRecordBubble("dialogue-decision:flush-on-pageturn", _candidateText, ""); } catch { }
                            _prevTranslatedText = _lastTranslatedText;
                            _lastTranslatedText = _candidateText;
                            _lastFireLoop = iteration;
                            string pageText = _candidateText;
                            _ = TranslateAndPublishAsync(pageText);
                            _candidateText = extractedText;
                            _candidateStableCount = 1;
                            _candidateGrewThisRead = false;
                            _flashFired = false;
                            _successorText = string.Empty;
                            _successorCount = 0;
                            _weakHoldCount = 0;
                            _weakHoldFor = string.Empty;
                        }
                        // MILESTONE 4 (challenger guard): weak incoming
                        // (junk, anchorless, or fragment) can never evict a
                        // substantial held line. Tracked as successor so a
                        // real scene change still breaks through below.
                        // MILESTONE 5 step 1 (recall): survival is stability.
                        // A held line outliving the weak-hold bar (12 full / 18 reduced, see S1.2) posts on
                        // its own instead of waiting 70+ loops for the next
                        // page-turn (the 昨年Ｐ/ブーメラン stall).
                        else if (!string.IsNullOrEmpty(_candidateText)
                            && IsSubstantialLine(_candidateText)
                            && !IsSubstantialLine(extractedText)
                            && !IsGameOverText(extractedText))
                        {
                            if (_successorText == extractedText)
                                _successorCount++;
                            else
                            {
                                _successorText = extractedText;
                                _successorCount = 1;
                            }
                            if (_weakHoldFor != _candidateText)
                            {
                                _weakHoldFor = _candidateText;
                                _weakHoldCount = 0;
                            }
                            _weakHoldCount++;
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-weak-challenger (keeping held line, weakHold={_weakHoldCount})");
                            if (_weakHoldCount >= _weakHoldThreshold
                                && !StartsHeadlessFragment(_candidateText)
                                && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                                && (string.IsNullOrEmpty(_lastTranslatedText) || !_lastTranslatedText.Contains(_candidateText, StringComparison.Ordinal)))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=publish-weak-hold (held line survived churn)");
                                try { CorpusRecordBubble("dialogue-decision:publish-weak-hold", _candidateText, ""); } catch { }
                                _prevTranslatedText = _lastTranslatedText;
                                _lastTranslatedText = _candidateText;
                                _lastFireLoop = iteration;
                                string weakHoldText = _candidateText;
                                _ = TranslateAndPublishAsync(weakHoldText);
                                _candidateStableCount = RequiredStableReadings;
                                _weakHoldCount = 0;
                                _weakHoldFor = string.Empty;
                                _successorText = string.Empty;
                                _successorCount = 0;
                            }
                        }
                        // Scroll-flush: the held line just scrolled (its successor
                        // starts with its tail). Publish the full held text now --
                        // it will never stabilize again.
                        else if (_candidateStableCount >= 2
                            && !string.IsNullOrEmpty(_candidateText)
                            && StartsWithSuffixOfStrip(extractedText, _candidateText, 12)
                            && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                            && !IsEllipsisOnly(_candidateText) && !IsJunkOcr(_candidateText)
                            && !IsScrollBlend(_candidateText) && !IsZeldaTransitionBlend(_candidateText)
                            && !EndsWithContinuation(_candidateText) && !IsIncompleteSacrifice(_candidateText)
                            && HasJapanese(_candidateText))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=flush-on-scroll (publishing scrolled line)");
                            try { CorpusRecordBubble("dialogue-decision:flush-on-scroll", _candidateText, ""); } catch { }
                            _prevTranslatedText = _lastTranslatedText;
                            _lastTranslatedText = _candidateText;
                            _lastFireLoop = iteration;
                            string flushText = _candidateText;
                            _ = TranslateAndPublishAsync(flushText);
                            _candidateText = extractedText;
                            _candidateStableCount = 1;
                            _candidateGrewThisRead = false;
                            _flashFired = false;
                        }
                        // Single garbled scroll-out must not wipe a held line.
                        else if (_candidateStableCount >= RequiredStableReadings
                            && !IsJunkOcr(_candidateText) && !IsEllipsisOnly(_candidateText)
                            && (IsScrollBlend(extractedText) || IsJunkOcr(extractedText) || extractedText.Length < 6))
                        {
                            // Guard-break: a challenger stabilizing twice means
                            // the scene really changed (Zelda cell blocks) -
                            // adopt it instead of holding forever.
                            if (_successorText == extractedText)
                            {
                                _successorCount++;
                                if (_successorCount >= 2)
                                {
                                    FileLogger.Log($"[loop {iteration}] STABLE-DECISION=guard-break (challenger stabilized, adopting)");
                                    _candidateText = extractedText;
                                    _candidateStableCount = 1;
                                    _candidateGrewThisRead = false;
                                    _successorText = string.Empty;
                                    _successorCount = 0;
                                    _flashFired = false;
                                }
                            }
                            else
                            {
                                _successorText = extractedText;
                                _successorCount = 1;
                            }
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-new-guard (keeping held line)");
                        }
                        else
                        {
                            // Ellipsis-dismissal flush: outgoing line was stable and clean,
                            // incoming read is dots only. Publish the held line instead of
                            // letting it die (same gates as breakthrough-publish).
                            if (IsEllipsisOnly(extractedText)
                                && _candidateStableCount >= RequiredStableReadings
                                && !_candidateGrewThisRead
                                && !string.IsNullOrEmpty(_candidateText)
                                && HasJapanese(_candidateText)
                                && !IsJunkOcr(_candidateText)
                                && !IsEllipsisOnly(_candidateText)
                                && !IsScrollBlend(_candidateText) && !IsZeldaTransitionBlend(_candidateText)
                                && !EndsWithContinuation(_candidateText) && !IsIncompleteSacrifice(_candidateText)
                                && !_frameIsTitle
                                && (string.IsNullOrEmpty(_lastTranslatedText) || !_lastTranslatedText.Contains(_candidateText, StringComparison.Ordinal))
                                && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                                && !(_lastOcrTotal >= 4 && _lastOcrContent == 0
                                    && !string.IsNullOrEmpty(_lastOcrText)
                                    && ComputeSimilarity(_lastOcrText, _candidateText) >= 0.80))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=flush-on-ellipsis (publishing dismissed line)");
                                _lastTranslatedText = _candidateText;
                                string ellipsisFlush = _candidateText;
                                _ = TranslateAndPublishAsync(ellipsisFlush);
                            }
                            // Replacement flush (uncle-2nd/3rd, escape line):
                            // stable clean held line, incoming is a different
                            // real line (not ellipsis) -> post the held one.
                            if (!IsEllipsisOnly(extractedText)
                                && _candidateStableCount >= RequiredStableReadings
                                && !_candidateGrewThisRead
                                && !string.IsNullOrEmpty(_candidateText)
                                && HasJapanese(_candidateText)
                                && !IsJunkOcr(_candidateText)
                                && !IsEllipsisOnly(_candidateText)
                                && !IsScrollBlend(_candidateText) && !IsZeldaTransitionBlend(_candidateText)
                                && !EndsWithContinuation(_candidateText) && !IsIncompleteSacrifice(_candidateText)
                                && !_frameIsTitle
                                && _candidateText != _lastTranslatedText
                                && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                                && (string.IsNullOrEmpty(_lastTranslatedText) || !_lastTranslatedText.Contains(_candidateText, StringComparison.Ordinal)))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=flush-on-replace (publishing replaced line)");
                                _lastTranslatedText = _candidateText;
                                string replaceFlush = _candidateText;
                                _ = TranslateAndPublishAsync(replaceFlush);
                            }
                            // Genuinely new line: track the LATEST read fresh.
                            _candidateText = extractedText;
                            _candidateStableCount = 1;
                            _candidateGrewThisRead = false;
                            _flashFired = false;
                        }
                    }
                    FileLogger.Log($"[loop {iteration}] candidateText=\"{_candidateText}\" stableCount={_candidateStableCount}");

                    if (_candidateStableCount >= RequiredStableReadings)
                    {
                        if (_candidateGrewThisRead && _candidateStableCount < GrowthBreakthroughPolls)
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-growth (still lengthening)");
                        }
                        else if (_candidateGrewThisRead)
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-growth (releasing long-held line)");
                            _candidateGrewThisRead = false;
                            string breakthroughText = _candidateText;
                            if (IsIncompleteSacrifice(breakthroughText))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-sacrifice");
                            }
                            else if (EndsWithContinuation(breakthroughText))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-continuation");
                            }
                            else if (IsScrollBlend(breakthroughText) || IsZeldaTransitionBlend(breakthroughText))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-blend");
                            }
                            else if (IsEllipsisOnly(breakthroughText))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-ellipsis");
                            }
                            else if (IsJunkOcr(breakthroughText))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-junk");
                            }
                            else if (!HasJapanese(breakthroughText))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-nonja");
                            }
                            else if (_frameIsTitle)
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-title");
                            }
                            else if (!string.IsNullOrEmpty(_lastTranslatedText)
                                && _lastTranslatedText.Contains(breakthroughText, StringComparison.Ordinal))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-subpart");
                            }
                            else if (ComputeSimilarity(breakthroughText, _lastTranslatedText) >= NearDuplicateSimilarityThreshold)
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-dupe");
                            }
                            else if (_lastOcrTotal >= 4 && _lastOcrContent == 0
                                && !string.IsNullOrEmpty(_lastOcrText)
                                && ComputeSimilarity(_lastOcrText, breakthroughText) >= 0.80)
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-contentless");
                            }
                            else if (StripForOverlap(breakthroughText).Length < 6)
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-held-fragment");
                            }
                            else
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=breakthrough-publish");
                                try { CorpusRecordBubble("dialogue-decision:breakthrough-publish", breakthroughText, ""); } catch { }
                                _lastTranslatedText = breakthroughText;
                                string breakthroughFire = breakthroughText;
                                _ = TranslateAndPublishAsync(breakthroughFire);
                            }
                        }
                        else if (IsIncompleteSacrifice(_candidateText))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-sacrifice (つぎつぎ without ささげ)");
                        }
                        else if (EndsWithContinuation(_candidateText))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-continuation (mid-sentence)");
                        }
                        else if (IsScrollBlend(_candidateText) || IsZeldaTransitionBlend(_candidateText))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-blend (scroll transition)");
                        }
                        else if (IsEllipsisOnly(_candidateText))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-ellipsis");
                            if (iteration % 20 == 0)
                                FileLogger.Log($"[loop {iteration}] candidate is ellipsis-only (\"{_candidateText}\") - holding, not publishing as its own line");
                        }
                        else if (IsJunkOcr(_candidateText))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=suppress-junk: \"{_candidateText}\"");
                        }
                        else
                        {
                        // ANCHOR-SHADOW (log-only): would a substance gate hold this?
                        // Observational only - the decision below runs unchanged.
                        if (!HasAnchorSubstance(_candidateText))
                            FileLogger.Log($"[anchor-shadow] would-hold \"{_candidateText}\" (no kanji/katakana)");
                        double similarity = ComputeSimilarity(_candidateText, _lastTranslatedText);
                        bool isNearDuplicate = similarity >= NearDuplicateSimilarityThreshold;

                        bool isGrowth = !string.IsNullOrEmpty(_lastTranslatedText)
                            && _candidateText.Length > _lastTranslatedText.Length
                            && (_candidateText.StartsWith(_lastTranslatedText, StringComparison.Ordinal)
                                || _candidateText.Contains(_lastTranslatedText, StringComparison.Ordinal));

                        bool isOverlapContinuation = !isNearDuplicate && _candidateText != _lastTranslatedText
                            && StartsWithSuffixOfStrip(_candidateText, _lastTranslatedText, 12);

                        // R1: return-to-predecessor with another line shown = oscillation, not a new page.
                        // Same trust shape as update-neardupe (>=0.90, or >=0.70 with common affix), aimed backward.
                        double prevSimilarity = string.IsNullOrEmpty(_prevTranslatedText) ? 0.0 : ComputeSimilarity(_candidateText, _prevTranslatedText);
                        bool isOscillationReturn = !string.IsNullOrEmpty(_prevTranslatedText)
                            && _prevTranslatedText != _lastTranslatedText
                            && (prevSimilarity >= NearDuplicateSimilarityThreshold
                                || (prevSimilarity >= 0.70
                                    && (HasCommonPrefix(_candidateText, _prevTranslatedText, 6)
                                        || StartsWithSuffixOfStrip(_candidateText, _prevTranslatedText, 6)
                                        || StartsWithSuffixOfStrip(_prevTranslatedText, _candidateText, 6))));

                        if (isGrowth)
                        {
                            string prev = _lastTranslatedText;
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=update-in-place");
                            _lastTranslatedText = _candidateText;

                            string textToTranslate = _candidateText;
                            _ = TranslateAndUpdateAsync(prev, textToTranslate);
                        }
                        else if (isOverlapContinuation)
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-overlap (scroll tail glued to next head)");
                        }
                        else if (!string.IsNullOrEmpty(_lastTranslatedText)
                            && _lastTranslatedText.Contains(_candidateText, StringComparison.Ordinal))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=subpart-suppressed (tail of shown line)");
                        }
                        else if ((similarity >= 0.70
                            && (HasCommonPrefix(_candidateText, _lastTranslatedText, 6)
                                || StartsWithSuffixOfStrip(_candidateText, _lastTranslatedText, 6)
                                || StartsWithSuffixOfStrip(_lastTranslatedText, _candidateText, 6)))
                            || HasLongCommonMiddle(_candidateText, _lastTranslatedText, 10))
                        {
                            // Flicker variant (こらこら ねなさい/ねえさい): same
                            // bubble, update in place - never a second post.
                            string prev = _lastTranslatedText;
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=update-neardupe (flicker variant, single bubble)");
                            _lastTranslatedText = _candidateText;
                            string textToTranslate = _candidateText;
                            _ = TranslateAndUpdateAsync(prev, textToTranslate);
                        }
                        else if (!isNearDuplicate)
                        {
                            // Density gate: 4+ words, zero content (POS), matching the
                            // candidate -> chrome, not dialogue. Short lines exempt.
                            if (_lastOcrTotal >= 4 && _lastOcrContent == 0
                                && !string.IsNullOrEmpty(_lastOcrText)
                                && ComputeSimilarity(_lastOcrText, _candidateText) >= 0.80)
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=suppress-contentless (words {_lastOcrContent}/{_lastOcrTotal})");
                            }
                            else if (!HasJapanese(_candidateText))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=suppress-non-ja");
                            }
                            // MILESTONE 2 (enforced, shadow-verified): no kanji/
                            // katakana, no bubble. Omission-only if ever wrong.
                            else if (!HasAnchorSubstance(_candidateText))
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-anchor (no kanji/katakana)");
                            }
                            else if (_frameIsTitle)
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=title-guard (title-screen misread, holding)");
                            }
                            else if (isOscillationReturn)
                            {
                                // R1 (corpus loops 555-557): the returnee proves the middle fire was the intruder.
                                // Single bubble: rewrite it back, never a second post. Omission-only if ever wrong.
                                string prev = _lastTranslatedText;
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=update-oscillation (return flicker, single bubble)");
                                _prevTranslatedText = _lastTranslatedText;
                                _lastTranslatedText = _candidateText;
                                string textToTranslate = _candidateText;
                                _ = TranslateAndUpdateAsync(prev, textToTranslate);
                            }
                            else if (IsUnterminatedComposite(_candidateText))
                            {
                                FileLogger.Log($"[translate] suppressed scroll-composite for \"{Truncate(_candidateText)}\"");
                                return;
                            }
                            else
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=publish-new (similarity to last shown: {similarity:F2})");
                                try { CorpusRecordBubble("dialogue-decision:publish-new", _candidateText, ""); } catch { }
                                _prevTranslatedText = _lastTranslatedText;
                                _lastTranslatedText = _candidateText;
                                _lastFireLoop = iteration;

                                string textToTranslate = _candidateText;
                                _ = TranslateAndPublishAsync(textToTranslate);
                            }
                        }
                        else
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=near-duplicate (similarity {similarity:F2})");
                        }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    FileLogger.Log("CaptureController loop cancelled");
                    break;
                }
                catch (WindowUnavailableException ex)
                {
                    _consecutiveCaptureFailures++;
                    FileLogger.Log(
                        $"[loop {iteration}] WINDOW CAPTURE FAILED ({_consecutiveCaptureFailures}/{MaxConsecutiveCaptureFailures}): " +
                        $"{ex.Message}");

                    if (!ex.HandleStillValid && _consecutiveCaptureFailures >= MaxConsecutiveCaptureFailures)
                    {
                        FileLogger.Log("CaptureController: window handle confirmed dead after repeated failures - stopping capture.");
                        WindowLost?.Invoke();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.Log($"[loop {iteration}] ERROR: {ex}");
                }

                try
                {
                    await Task.Delay(IdleDelayMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            FileLogger.Log("CaptureController loop exited");
        }

        private async Task TranslateAndPublishAsync(string text)
        {
            try
            {
                if (IsLatinChrome(text))
                {
                    FileLogger.Log($"[translate] suppressed latin-chrome for \"{Truncate(text)}\"");
                    return;
                }
                if (HasUntrustedLatinRun(text))
                {
                    FileLogger.Log($"[translate] suppressed untrusted-latin for \"{Truncate(text)}\"");
                    return;
                }
                if (IsGameOverText(text))
                {
                    FileLogger.Log($"[translate] suppressed gameover-dupe for \"{Truncate(text)}\" (menu pack owns it)");
                    return;
                }
                // Double-fire symmetric guard (FFVI Gil doubles): dialogue exactly matching the
                // currently-stable menu holds — menu owns it (menu-side already holds the reverse).
                // Omission-only; _menuSignature empties off-menu so holds never go stale.
                if (!string.IsNullOrEmpty(_menuSignature) && _menuSignature.Split('\n').Select(r => NormalizePunctForStability(r)).Contains(text))
                {
                    FileLogger.Log($"[translate] suppressed menu-dupe for \"{Truncate(text)}\" (menu owns it)");
                    return;
                }
                if (!string.IsNullOrEmpty(_lastDecisionSig)
                    && (ComputeSimilarity(StripForOverlap(text), StripForOverlap(_lastDecisionSig)) >= 0.60
                        || StripForOverlap(_lastDecisionSig).Contains(StripForOverlap(text), StringComparison.Ordinal)
                        || StripForOverlap(text).Contains(StripForOverlap(_lastDecisionSig), StringComparison.Ordinal)))
                {
                FileLogger.Log($"[translate] suppressed decision-dupe for \"{Truncate(text)}\" (decision pack owns it)");
                    return;
                }
                // S1.1: pre-game chrome (emulator menus, other windows) never becomes a bubble.
                // Menus/titles/decisions bypass this (navigation flows); dialogue waits out warmup.
                // Omission-only if ever wrong.
                if (_settledOcrLoops < StartupWarmupOcrLoops)
                {
                    FileLogger.Log($"[translate] suppressed warmup for \"{Truncate(text)}\" (settled {_settledOcrLoops}<{StartupWarmupOcrLoops})");
                    return;
                }
                // S1.3 title-screen guard (log-named loops 28/32/33 この人の正規): logo screens carry
                // no story dialogue — any dialogue-height read there is art strokes. TITLE/MENU still
                // publish via the menu path. Omission-only if ever wrong. (トライフォ string is M8 pack debt.)
                if (_frameIsTitle)
                {
                    FileLogger.Log($"[translate] suppressed title-screen-dialogue for \"{Truncate(text)}\"");
                    return;
                }
                // MILESTONE 3 (single publish gate): unanchored text never
                // becomes a bubble, whichever path fired it. Kills よろしくお,
                // …いた and the grunt family at every door at once.
                // Omission-only if ever wrong.
                if (!HasAnchorSubstance(text))
                {
                    FileLogger.Log($"[translate] suppressed anchorless for \"{Truncate(text)}\" (no kanji/katakana)");
                    return;
                }
                FileLogger.Log($"[translate-fire] \"{Truncate(text)}\"");
                string translated = await _translationManager.TranslateAsync(text);
                FileLogger.Log($"[translate-provider] {_translationManager.ActiveProviderName} for \"{Truncate(text)}\"");
                if (IsUnpublishable(translated))
                {
                    FileLogger.Log($"[translate] untranslatable -> ------ for \"{text}\" (DeepL fallback exhausted/no key)");
                    if (_stopped) { FileLogger.Log($"[translate] id={_id} dropped (stopped)"); return; }
                    TextTranslated?.Invoke(text, "------");
                    return;
                }
                // Stale guard: a longer version already shown covers this one
                // (truncated completing after its full line). Don't re-append.
                if (!string.IsNullOrEmpty(_lastTranslatedText)
                    && _lastTranslatedText != text
                    && _lastTranslatedText.Contains(text, StringComparison.Ordinal))
                {
                    FileLogger.Log($"[translate] stale-suppressed (covered by shown line)");
                    return;
                }
                FileLogger.Log($"[translate] id={_id} \"{text}\" -> \"{translated}\"");
                if (_stopped) { FileLogger.Log($"[translate] id={_id} dropped (stopped)"); return; }
                try { CorpusRecordBubble("dialogue-fire", text, translated); } catch { }
                TextTranslated?.Invoke(text, translated);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[translate] ERROR translating \"{text}\": {ex}");
            }
        }

        private async Task TranslateAndUpdateAsync(string expectedOldJa, string text)
        {
            try
            {
                if (!string.IsNullOrEmpty(_lastDecisionSig)
                    && (ComputeSimilarity(StripForOverlap(text), StripForOverlap(_lastDecisionSig)) >= 0.60
                        || StripForOverlap(_lastDecisionSig).Contains(StripForOverlap(text), StringComparison.Ordinal)
                        || StripForOverlap(text).Contains(StripForOverlap(_lastDecisionSig), StringComparison.Ordinal)))
                {
                FileLogger.Log($"[translate] suppressed decision-dupe update for \"{Truncate(text)}\" (decision pack owns it)");
                    return;
                }
                if (HasUntrustedLatinRun(text))
                {
                    FileLogger.Log($"[translate] suppressed untrusted-latin update for \"{Truncate(text)}\"");
                    return;
                }
                if (_settledOcrLoops < StartupWarmupOcrLoops)
                {
                    FileLogger.Log($"[translate] suppressed warmup update for \"{Truncate(text)}\" (settled {_settledOcrLoops}<{StartupWarmupOcrLoops})");
                    return;
                }
                // R2 updates: growth legitimately lacks terminals, but mid-word-cut blends must never morph
                // a bubble (lever line proved the fire-only bypass). Hiragana growth passes via the carve-out.
                if (IsUnterminatedComposite(text))
                {
                    FileLogger.Log($"[translate] suppressed scroll-composite update for \"{Truncate(text)}\"");
                    return;
                }
                FileLogger.Log($"[translate-fire] \"{Truncate(text)}\" (update)");
                string translated = await _translationManager.TranslateAsync(text);
                if (IsUnpublishable(translated))
                {
                    FileLogger.Log($"[translate] untranslatable update -> ---- for \"{text}\"");
                    TextUpdated?.Invoke(expectedOldJa, text, " ------");
                    return;
                }
                FileLogger.Log($"[translate-update] \"{text}\" -> \"{translated}\"");
                try { CorpusRecordBubble("dialogue-update", text, translated, expectedOldJa); } catch { }
                TextUpdated?.Invoke(expectedOldJa, text, translated);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[translate] ERROR translating \"{text}\": {ex}");
            }
        }

        private static string Truncate(string s, int max = 80)
            => s.Length <= max ? s : s.Substring(0, max) + "...";

        // M7a corpus recorder (automatic, text-only, ~2KB/loop). One JSONL per session under
        // %AppData%/WunderLanguage/corpus/. M7b replays raw+blocks through the real fixup/
        // stability/publish path with translation stubbed; exit = identical Japanese bubbles.
        // Off-switch: RecordCorpus=false. Never throws; never blocks OCR.
        private static readonly bool RecordCorpus = true;
        private string _corpusPath = "";
        private readonly object _corpusLock = new();
        private void CorpusEnsure()
        {
            if (_corpusPath != "") return;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage", "corpus");
            Directory.CreateDirectory(dir);
            _corpusPath = Path.Combine(dir, _gameId + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jsonl");
        }
        private void CorpusLine(string line)
        {
            try
            {
                if (!RecordCorpus) return;
                CorpusEnsure();
                lock (_corpusLock) File.AppendAllText(_corpusPath, line + "\n");
            }
            catch { }
        }
        private void CorpusRecordOcr(int loop, string raw, List<OcrBlock> blocks, string extracted, bool mined = false)
        {
            try
            {
                if (!RecordCorpus) return;
                var bs = new List<object>(blocks.Count);
                foreach (var b in blocks) bs.Add(new { t = b.Text, x = b.X, y = b.Y, w = b.Width, h = b.Height });
                CorpusLine(JsonSerializer.Serialize(new { v = 1, type = "ocr", loop = loop, raw = raw, blocks = bs, extracted = extracted, mined = mined }));
            }
            catch { }
        }
        private void CorpusRecordBubble(string kind, string ja, string en, string old = "")
        {
            try
            {
                if (!RecordCorpus) return;
                CorpusLine(JsonSerializer.Serialize(new { v = 1, type = "bubble", kind = kind, old = old, ja = ja, en = en }));
            }
            catch { }
        }

        private static readonly Regex SingleCharRun = new(@"(?:\b\w\b\s*){8,}", RegexOptions.Compiled);
        private static readonly Regex AlphaRun = new(@"([A-Za-z0-9])\1{6,}", RegexOptions.Compiled);
        private static readonly Regex FullWidthLatin = new(@"[Ａ-Ｚａ-ｚ]", RegexOptions.Compiled);
        private static readonly Regex PromptEcho = new(@"(translat\w*|transform\w*).{0,25}following Japanese text.{0,30}english|following Japanese text.{0,40}translat\w+ into english|ready to translate|provide the (Japanese )?(passage|text)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JapaneseChars = new(@"[぀-ヿ一-鿿]", RegexOptions.Compiled);

        private static bool IsJunkOcr(string text)
        {
            if (Regex.IsMatch(text.Trim(), @"^([uｕ]|い) ?[FＦ] ?[EＥ]$|^いた$|^はいぜ、?$|^そこ$")) return true;
            int content = 0;
            int fullWidthLatin = 0;
            bool hasHiragana = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || c == '.' || c == '．' || c == '…' || c == '・' || c == '、' || c == '。')
                    continue;
                content++;
                if (c >= 'ぁ' && c <= 'ん')
                    hasHiragana = true;
                if ((c >= 'Ａ' && c <= 'Ｚ') || (c >= 'ａ' && c <= 'ｚ') || (c >= '０' && c <= '９'))
                    fullWidthLatin++;
            }
            if (content < MinCandidateContentLength)
                return true;
            // Doubled-grunt noise: identical kana twice (tall-block ああ
            // sprite misread, no textbox) is never a dialogue line. Shape
            // only: distinct pairs (はい, うん) and punctuated doubles
            // (うう…) pass through untouched. Omission-only if wrong.
            if (content == 2)
            {
                string bare = text.Trim();
                if (bare.Length == 2 && bare[0] == bare[1]
                    && ((bare[0] >= 'ぁ' && bare[0] <= 'ん') || (bare[0] >= 'ァ' && bare[0] <= 'ヶ')))
                    return true;
            }
            if (!hasHiragana && (fullWidthLatin >= 3 || (content > 0 && (double)fullWidthLatin / content > 0.3)))
                return true;
            // Digit-dominated lines are HUD noise (scores, counters), never
            // dialogue: real lines carry kana context (１００ルピー… sits at
            // ~25%, safely below). Kills the ２０５０００ bubble + junk MENU.
            int digits = 0, substance = 0;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || c == '.' || c == '．' || c == '…' || c == '・' || c == '、' || c == '。'
                    || c == 'ー' || c == '─' || c == '-' || c == 'っ' || c == 'ッ')
                    continue;
                substance++;
                if ((c >= '0' && c <= '9') || (c >= '０' && c <= '９'))
                    digits++;
            }
            if (digits > 0 && substance > 0 && digits * 2 >= substance)
                return true;
            return false;
        }

        // GAME OVER dialogue-side: multi-セーブ text belongs to the menu pack.
        private static bool IsGameOverText(string text)
        {
            int n = 0, k = 0;
            while ((k = text.IndexOf("セーブ", k, StringComparison.Ordinal)) >= 0) { n++; k += 3; if (n >= 2) return true; }
            return false;
        }

        // L1.2 Latin-allowlist (universal, hold-only): exact MAP/START/PUSH/SELECT/COPY/KILL
        // + coords (1F/B1) pass. Any other A-Z run len>=2 (PIGH, Gmazow, iPhone) fails closed.
        // Never rewrites garbage into plausible text. Idempotent: pure predicate, no text change.
        private static readonly HashSet<string> LatinAllowlist = new(StringComparer.OrdinalIgnoreCase)
        {
            "MAP", "START", "PUSH", "SELECT", "COPY", "KILL"
        };

        private static bool HasUntrustedLatinRun(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= 'Ａ' && c <= 'Ｚ') sb.Append((char)(c - 'Ａ' + 'A'));
                else if (c >= 'ａ' && c <= 'ｚ') sb.Append((char)(c - 'ａ' + 'A'));
                else if (c >= '０' && c <= '９') sb.Append((char)(c - '０' + '0'));
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(char.ToUpperInvariant(c));
                else sb.Append(' ');
            }
            foreach (Match m in Regex.Matches(sb.ToString(), @"[A-Z0-9]{2,}"))
            {
                string run = m.Value;
                if (!Regex.IsMatch(run, @"[A-Z]")) continue;
                if (LatinAllowlist.Contains(run)) continue;
                if (Regex.IsMatch(run, @"^[0-9]+[A-Z]+$")) continue;
                if (Regex.IsMatch(run, @"^[A-Z]+[0-9]+$")) continue;
                return true;
            }
            return false;
        }

        // Emulator chrome (ROM info, netplay, menu English): Latin-dominated
        // lines are never dialogue. Zelda dialogue never exceeds ~15% Latin
        // (MAP/PUSH lines), so this shape can't eat real lines.
        private static bool IsLatinChrome(string text)
        {
            int latin = 0, other = 0;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || c == '…' || c == '．' || c == '.' || c == '、' || c == '。')
                    continue;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= 'Ａ' && c <= 'Ｚ') || (c >= 'ａ' && c <= 'ｚ'))
                    latin++;
                else
                    other++;
            }
            return latin >= 4 && latin > other;
        }

        private static bool IsUnpublishable(string translated)
        {
            if (translated.StartsWith("[word guide", StringComparison.Ordinal))
                return false;
            if (translated.Length >= 30 && translated.ToLower().Distinct().Count() <= 12)
                return true;
            if (AlphaRun.IsMatch(translated))
                return true;
            if (string.IsNullOrWhiteSpace(translated))
                return true;
            if (translated.Contains("can’t help with that", StringComparison.OrdinalIgnoreCase)
                || translated.Contains("can't help with that", StringComparison.OrdinalIgnoreCase))
                return true;
            if (PromptEcho.IsMatch(translated))
                return true;
            if (JapaneseChars.IsMatch(translated))
                return true;
            var words = translated.ToLower().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 12)
            {
                var bigramCounts = new Dictionary<string, int>();
                for (int i = 0; i + 1 < words.Length; i++)
                {
                    string bg = words[i] + "" + words[i + 1];
                    bigramCounts.TryGetValue(bg, out int c);
                    bigramCounts[bg] = c + 1;
                }
                int top = 0;
                foreach (int c in bigramCounts.Values) if (c > top) top = c;
                if (top >= System.Math.Max(4, words.Length / 3))
                    return true;
            }
            var toks = translated.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length >= 20 && toks.Distinct().Count() <= Math.Max(3, toks.Length / 4))
                return true;
            if (SingleCharRun.IsMatch(translated))
                return true;
            return false;
        }

        private static readonly char[] EllipsisChars = { '．', '.', '…', ' ', '　' };

        private static bool IsEllipsisOnly(string text)
        {
            foreach (char c in text)
            {
                if (Array.IndexOf(EllipsisChars, c) < 0)
                    return false;
            }
            return text.Length > 0;
        }

        // Dwell-dash noise: static-frame fade remnants (LIFE HUD "— LIFE —"
        // OCRs as …ーーーっ + pixel-font gasps like はぁ). Shape: dash run
        // with zero kanji/katakana. Dwell-only gate: real lines still publish
        // via the normal 3-read path, so this never alters or kills text.
        private static bool IsDwellDashNoise(string text)
        {
            if (!Regex.IsMatch(text, @"[ー─-]{2,}")) return false;
            foreach (char c in text)
            {
                if ((c >= '一' && c <= '鿿') || (c >= 'ァ' && c <= 'ヶ')) return false;
            }
            return true;
        }

        // Anchor-substance probe (SHADOW, log-only): real utterances carry
        // kanji or katakana; pixel-noise grunts (はぁ, ああ) never do.
        // Enforcement comes later, only on clean shadow evidence.
        private static bool HasAnchorSubstance(string text)
        {
            foreach (char c in text)
            {
                if ((c >= '一' && c <= '鿿') || (c >= 'ァ' && c <= 'ヶ')) return true;
            }
            // Long-hiragana escape (FFVI shop speech loops 117-120): pixel grunts are short
            // (はぁっ、はぁぁぁ peaks at ~8); a 12+ hiragana line is speech, never a grunt.
            // All downstream gates (dwell/stability/recall/translation) still apply — this only
            // admits long kana lines to them. Omission-only if ever wrong.
            int clean = 0;
            foreach (char c in text)
            {
                if (c >= 'ぁ' && c <= 'ん') clean++;
                else if (char.IsWhiteSpace(c) || c == '…' || c == '．' || c == '.' || c == '、' || c == '。' || c == '！' || c == '!' || c == '？' || c == '?' || c == '「' || c == '」' || c == '・') continue;
                else return false;
            }
            return clean >= 12;
        }

        // MILESTONE 4: publish-worthy shape. Anchored, clean, and long
        // enough to be a real line (grunts and fragments fail length).
        // Challenger guard and page-turn flush both build on this.
        private static bool IsSubstantialLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!HasAnchorSubstance(text)) return false;
            if (IsJunkOcr(text)) return false;
            if (IsEllipsisOnly(text)) return false;
            return StripForOverlap(text).Length >= 6;
        }

        // MILESTONE 5 (recall shape): a publishable page starts clean and
        // ends terminated. Kills を-headless tails (を再び…) and unterminated
        // growth fragments (伝わる…できる) without touching publish-new.
        private static bool StartsHeadlessFragment(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string t = text.TrimStart('…', '.', '．', ' ', '　', '、', '。', '「', '」', '『', '』', '〈', '〉', '【', '】', '・');
            if (t.Length == 0) return true;
            char c = t[0];
            return c == 'を' || c == 'に' || c == 'が' || c == 'は' || c == 'も' || c == 'で' || c == 'と' || c == 'へ' || c == 'の' || c == 'や';
        }

        private static bool EndsCompleteSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            char last = text.TrimEnd()[^1];
            return last == '。' || last == '！' || last == '？' || last == '…' || last == '!' || last == '?';
        }

        private static bool IsCompleteDialogueLine(string text)
        {
            if (!IsSubstantialLine(text)) return false;
            if (StartsHeadlessFragment(text)) return false;
            if (!EndsCompleteSentence(text)) return false;
            return true;
        }

        private static bool IsIncompleteSacrifice(string text)
        {
            return text.Contains("つぎつぎ", StringComparison.Ordinal)
                && !text.Contains("ささげ", StringComparison.Ordinal);
        }

        private static bool EndsWithContinuation(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            if (text.EndsWith("てい", StringComparison.Ordinal))
                return true;
            if (text.EndsWith("もうそ", StringComparison.Ordinal))
                return true;
            char last = text.TrimEnd()[^1];
            return last == '、' || last == '，' || last == ','
                || last == 'は' || last == 'が' || last == 'を' || last == 'に' || last == 'へ' || last == 'と' || last == 'も' || last == 'で' || last == 'の';
        }

        // Scroll transitions glue tail+head with 。 mid-string
        // (…そうです。封印は…). Single Zelda lines never do this.
        // Hold them: firing burns a full GPU translation on garbage.
        private static bool IsScrollBlend(string text)
        {
            string t = text.TrimEnd();
            if (t.EndsWith("?") || t.EndsWith("？") || t.EndsWith("!") || t.EndsWith("！")) return false;
            int lastPeriod = text.LastIndexOf('。');
            if (lastPeriod < 0)
                return false;
            return text.Substring(0, lastPeriod).Trim().Length >= 6
                && text.Substring(lastPeriod + 1).Trim().Length >= 6;
        }

        // M8-2 moved: game-specific blend lives in Packs/Zelda/ZeldaPack.cs. Core name kept so 8 call sites unchanged.
        private static bool IsZeldaTransitionBlend(string text) => Packs.PackRegistry.Current.IsTransitionBlend(text);
        // Quiet-title detector (universal machinery): textless logo screens yield long zero-substance
        // stretches. Fires pack FullTitle ONCE per stretch (edge-trigger at bar). Fades/loads never
        // qualify (too short, no logo hints). Zelda unaffected (readable titles fire first and reset).
        // Omission-only if ever wrong. Empty FullTitle (Universal) never fires.
        private const int QuietTitleLoops = 30;
        private string? CheckQuietTitle(int iteration, List<OcrBlock>? blocks)
        {
            try
            {
                // Battle liveness breaks the streak: any frame fire or slot change
                // resets the counter, so long battles can never mint a TITLE.
                string sig = (_lastTranslatedText ?? "") + "\n" + (_menuSignature ?? "") + "\n" + (_lastPublishedMenu ?? "") + "\n" + (_battleFiredSig ?? "") + "\n" + (_battlePrevSig ?? "");
                if (sig != _quietSeen) { _quietSeen = sig; _quietCount = 0; _quietHint = false; return null; }
                if (iteration % 40 == 0 && _quietCount > 0) FileLogger.Log($"[menu] quiet-title watch quiet={_quietCount} hint={_quietHint}");
                if (blocks != null)
                {
                    foreach (var b in blocks)
                    {
                        string bt = (b.Text ?? "").Trim();
                        if (bt.Length == 0) continue;
                        if (System.Text.RegularExpressions.Regex.IsMatch(bt, @"[A-ZＡ-Ｚ©]{4,}|(?:19|20)\d\d|(?:１９|２０)[０-９][０-９]")) _quietHint = true;
                    }
                }
                _quietCount++;
                string packTitle = Packs.PackRegistry.Current.FullTitle;
                // Hint-gated bars: logo evidence (caps/years/copyright in frame)
                // fires at 30; bare quiet needs 120. Pre-logo black and idle fields
                // accumulate quiet without hints and must never mint a TITLE.
                int titleBar = _quietHint ? QuietTitleLoops : QuietTitleLoops * 4;
                if (_quietCount == titleBar && !string.IsNullOrEmpty(packTitle)
                    && string.IsNullOrEmpty(_menuSignature) && string.IsNullOrEmpty(_candidateText))
                {
                    FileLogger.Log($"[menu] quiet-title publish TITLE (logo screen, {titleBar} quiet loops hint={_quietHint})");
                    return packTitle;
                }
                return null;
            }
            catch { return null; }
        }

        // R2 scroll-composite (。。-less sibling of IsScrollBlend): a long line with a …/。 boundary buried
        // mid-string but an unterminated tail (corpus 140904 loops 301-303: …１人…城にやって来た司祭アグニ)
        // is a scroll caught mid-glide, not a line. Legit long lines terminate (。！？…!?); short fragments
        // and continuation tails are owned upstream (fragment/continuation gates). Omission-only if ever wrong.
        private static bool IsUnterminatedComposite(string text)
        {
            string t = text.TrimEnd();
            if (t.Length == 0) return false;
            char last = t[t.Length - 1];
            if (last == '。' || last == '！' || last == '？' || last == '…' || last == '!' || last == '?') return false;
            // R2 refined: hiragana endings (verb/continuation forms, complete-at-boundary words) are genuine.
            // Mid-word cuts strand on katakana/kanji (アグニ) or bare tails. Stated hole: hiragana-cut blends
            // pass — indistinguishable from continuations host-side.
            if (last >= 'ぁ' && last <= 'ん') return false;
            if (StripForOverlap(text).Length < 24) return false;
            int ell = t.IndexOf('…');
            if (ell > 0 && ell < t.Length - 1) return true;
            int per = t.IndexOf('。');
            if (per >= 0 && per < t.Length - 1) return true;
            return false;
        }

        private const double NearDuplicateSimilarityThreshold = 0.90;

        private static string StripForOverlap(string s)
        {
            return s.Replace("。", "").Replace("、", "").Replace(",", "").Replace("，", "")
                .Replace("…", "").Replace("．", "").Replace(".", "").Replace(" ", "").Replace("　", "");
        }

        private static bool StartsWithSuffixOfStrip(string candidate, string last, int minLen)
        {
            string c = StripForOverlap(candidate);
            string l = StripForOverlap(last);
            if (string.IsNullOrEmpty(c) || string.IsNullOrEmpty(l)) return false;
            if (c.Length <= minLen || l.Length < minLen) return false;
            if (c == l) return false;
            for (int len = minLen; len <= Math.Min(l.Length, c.Length); len++)
            {
                if (c.StartsWith(l.Substring(l.Length - len), StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool HasCommonPrefix(string a, string b, int minLen)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            int n = 0;
            int max = Math.Min(a.Length, b.Length);
            while (n < max && a[n] == b[n]) n++;
            return n >= minLen;
        }

        // R-deep mid-flicker (guard scene: stable 入れんぞ子どもは早く帰って middle, wobbling heads+t consensuses):
        // a shared stripped run of minLen anywhere (not just the ends) = same line refining, update in place.
        // minLen 10 = the evidenced core (子どもは早く帰って); shorter would merge boilerplate. Omission-neutral.
        private static bool HasLongCommonMiddle(string a, string b, int minLen)
        {
            string sa = StripForOverlap(a), sb = StripForOverlap(b);
            if (sa.Length < minLen || sb.Length < minLen) return false;
            string short_ = sa.Length <= sb.Length ? sa : sb;
            string long_ = sa.Length <= sb.Length ? sb : sa;
            for (int i = 0; i + minLen <= short_.Length; i++)
                if (long_.Contains(short_.Substring(i, minLen), StringComparison.Ordinal)) return true;
            return false;
        }

        private static double ComputeSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
                return 1.0;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0.0;

            int distance = LevenshteinDistance(a, b);
            int maxLen = Math.Max(a.Length, b.Length);
            return 1.0 - (double)distance / maxLen;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            var dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            }

            return dp[a.Length, b.Length];
        }

        // GAME OVER pack: fixed screen (big GAME OVER + 3 セーブ rows, often
        // merged into one OCR block). Shape: GAME*OVER present + 2+ セーブ rows.
        private static bool TryBuildGameOverRows(List<OcrBlock> blocks, out List<(int y, string t, bool ja)> rows)
        {
            rows = new List<(int y, string t, bool ja)>();
            bool hasGameOver = false;
            foreach (var b in blocks)
            {
                string flat = ToHalfwidth(b.Text).Replace(" ", "").Replace("　", "");
                if (flat.Contains("GAME", StringComparison.OrdinalIgnoreCase) && flat.Contains("OVER", StringComparison.OrdinalIgnoreCase))
                { hasGameOver = true; break; }
            }
            // Headerless fallback: the GAME OVER title OCRs as junk too often,
            // and the 3 rows merge into ONE tall block (h>150 skips the generic
            // path). Count occurrences, not blocks - one merged block holds 3.
            if (!hasGameOver)
            {
                // Any セーブ misread shape counts (raw is usually 1 clean +
                // 2 misread: セープ/セ一ブ). Regex count, no log spam.
                int saveHits = 0;
                foreach (var b in blocks)
                    saveHits += Regex.Count(b.Text, @"セ[ー一ッっ][プブ]");
                if (saveHits < 2) return false;
            }
            var jaBlocks = new List<OcrBlock>();
            foreach (var b in blocks)
                if (HasJapanese(b.Text)) jaBlocks.Add(b);
            jaBlocks.Sort((a, b2) => a.Y.CompareTo(b2.Y));
            rows.Add((0, "GAME OVER", false));
            rows.Add((1, "", false));
            int y = 10;
            foreach (var b in jaBlocks)
            {
                string t = b.Text.Trim();
                if (!Regex.IsMatch(t, @"セ[ー一ッっ][プブ]")) continue;
                foreach (var part in Regex.Split(t, @"(?=セ[ー一ッっ][プブ])"))
                {
                    string p = Packs.PackRegistry.Current.ApplyGameOverRowFixups(ApplyZeldaOcrFixups(part.Trim()));
                    if (p.Length >= 4) { rows.Add((y, p, true)); y += 10; }
                }
            }
            rows.Add((y, "", false));
            int optionCount = 0;
            foreach (var r in rows) if (r.ja) optionCount++;
            return optionCount >= 2;
        }

        // In-text decisions (choice screens): a ?-terminated question plus
        // short non-sentence option rows. Generic shape - Zelda yes/no today,
        // N options / arrow-or-not / centered layouts tomorrow, no per-game
        // strings. Runs on raw Y-grouped lines (pre junk-filter) so 1-char
        // options survive; remainder splits on punctuation only (spaces are
        // game truth, e.g. う　ん). Future v2: same-row wide-gap splitting.
        private static bool TryBuildDecisionRows(
            List<(int y, List<OcrBlock> parts)> lines,
            out List<(int y, string t, bool ja)> decision)
        {
            decision = new List<(int y, string t, bool ja)>();
            var texts = new List<(int y, string t)>();
            foreach (var ln in lines.OrderBy(l => l.y))
            {
                string j = string.Join(" ", ln.parts.OrderBy(p => p.X).Select(p => p.Text.Trim()));
                if (!string.IsNullOrWhiteSpace(j)) texts.Add((ln.y, j));
            }
            int qi = -1, qpos = -1;
            for (int i = 0; i < texts.Count; i++)
            {
                int p = texts[i].t.IndexOf('？');
                if (p < 0) p = texts[i].t.IndexOf('?');
                if (p >= 0) { qi = i; qpos = p; break; }
            }
            if (qi < 0) return false;
            var qparts = new List<string>();
            for (int i = 0; i < qi; i++)
            {
                string t = texts[i].t.Trim();
                if (t.Length == 0 || texts[qi].t.Contains(t, StringComparison.Ordinal)) continue;
                if (HasJapanese(t) && !IsJunkOcr(t) && !IsEllipsisOnly(t)) qparts.Add(t);
            }
            qparts.Add(texts[qi].t.Substring(0, qpos + 1).Trim());
            string question = string.Join(" ", qparts).Trim();
            if (question.Length < 2) return false;
            var tails = new List<string>();
            string tail = texts[qi].t.Substring(qpos + 1);
            if (!string.IsNullOrWhiteSpace(tail)) tails.Add(tail);
            for (int i = qi + 1; i < texts.Count; i++)
            {
                string t = texts[i].t.Trim();
                if (string.IsNullOrWhiteSpace(t) || IsEllipsisOnly(t)) continue;
                // Stray HUD rows (いた, dates) must not become options;
                // single chars (う) can't be judged, always keep.
                if (t.Length > 1 && IsJunkOcr(t)) continue;
                tails.Add(t);
            }
            var options = new List<string>();
            foreach (var part in tails)
            {
                foreach (var piece in part.Split(new[] { '、', '。', '，', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string o = piece.Trim().TrimStart('▶', '>', '＞', '→', '・');
                    // Single chars can't be judged (う) - only filter longer junk.
                    if (o.Length == 0 || IsEllipsisOnly(o)) continue;
                    if (o.Length > 1 && IsJunkOcr(o)) continue;
                    options.Add(o);
                }
            }
            if (options.Count < 1 || options.Count > 6) return false;
            foreach (var o in options)
            {
                if (o.Length > 12) return false;
                if (o.Contains('。') || o.Contains('！') || o.Contains('!') || o.Contains('？') || o.Contains('?')) return false;
            }
            // Same pipeline as dialogue display: fixups + normalize, so the
            // DECISION bubble matches dialogue spelling (せんぜん→ぜんぜん).
            string fixedQ = NormalizePunctForStability(ApplyZeldaOcrFixups(question));
            decision.Add((texts[qi].y, fixedQ, HasJapanese(fixedQ)));
            int y = texts[qi].y;
            foreach (var o in options)
            {
                y += 10;
                string fixedO = NormalizePunctForStability(ApplyZeldaOcrFixups(o));
                if (fixedO.Length == 0) continue;
                decision.Add((y, fixedO, HasJapanese(fixedO)));
            }
            return true;
        }

        // Generic menu/title: small JA blocks, art filtered. No per-game strings.
        private void UpdateMenuTracker(List<OcrBlock> blocks)
        {
            _frameIsTitle = blocks.Count <= 3 && blocks.Any(b => b.Text.Contains(Packs.PackRegistry.Current.TitleMarker));
            if (TryBuildGameOverRows(blocks, out var goRows))
            {
                string goSig = string.Join("\n", goRows.ConvertAll(o => o.t));
                if (goSig == _lastPublishedMenu) return;
                double goOverlap = MenuRowOverlap(goSig, _lastPublishedMenu);
                if (!string.IsNullOrEmpty(_lastPublishedMenu) && (goOverlap >= 0.50 || goSig.Contains(_lastPublishedMenu) || _lastPublishedMenu.Contains(goSig)))
                {
                    string goPrev = _lastPublishedMenu;
                    _lastPublishedMenu = goSig;
                    _ = TranslateAndUpdateMenuAsync(goPrev, goRows);
                    return;
                }
                _lastPublishedMenu = goSig;
                FileLogger.Log($"[menu] publish MENU lines={goRows.Count} (gameover pack)");
                _ = TranslateAndPublishMenuAsync("MENU", goRows);
                return;
            }
            if (IsInputGrid(blocks)) { _menuStableCount = 0; _menuSignature = ""; return; }
            // Group same-line blocks by Y overlap, top-to-bottom lines.
            // Battle wiring only: party-status cells drop, tall X-disjoint columns
            // never merge. Geometry lives in BattleFrame.cs; gated on battle shape
            // so non-battle frames group exactly as before.
            bool battleFrame = BattleFrame.IsBattle(blocks, _battleFrameW, _battleFrameH, HasJapanese);
            if (_battleCooldown > 0) _battleCooldown--;
            // Battle owns its rows while a frame is live (plus grace): menu stays
            // silent so the feed shows one frame, never a frame plus a MENU dupe.
            // Non-battle and pre-frame menus flow exactly as before.
            if (battleFrame && (_battleFiredSig != "" || _battleCooldown > 0)) return;
            var lines = new List<(int y, List<OcrBlock> parts)>();
            foreach (var b in blocks.OrderBy(b => b.Y))
            {
                if (b.Height > 150 && !HasJapanese(b.Text)) continue;
                if (b.Width < 10 || b.Height < 12) continue;
                string t = b.Text.Trim();
                if (t.Length < 1) continue;
                if (battleFrame && BattleFrame.IsPartyStatusBlock(b, _battleFrameW, _battleFrameH, HasJapanese))
                    continue;
                bool placed = false;
                foreach (var ln in lines)
                {
                    int ly = ln.y;
                    if (Math.Abs(b.Y - ly) < Math.Min(b.Height, 60) / 2 + 8)
                    {
                        bool tallColumnSplit = false;
                        if (battleFrame)
                        {
                            foreach (var p in ln.parts)
                            {
                                if (BattleFrame.IsTallColumnSplit(b, p)) { tallColumnSplit = true; break; }
                            }
                        }
                        if (!tallColumnSplit)
                        {
                            ln.parts.Add(b);
                            placed = true;
                            break;
                        }
                    }
                }
                if (!placed) lines.Add((b.Y, new List<OcrBlock> { b }));
            }
                        // Menu chrome filter: copyright/Presents never published (EN already).
            lines.RemoveAll(ln =>
            {
                string allT = string.Join(" ", ln.parts.ConvertAll(p => p.Text));
                string asciiT = ToHalfwidth(allT);
                if (asciiT.Contains("resent", StringComparison.OrdinalIgnoreCase)) return true;
                bool hasYear = System.Text.RegularExpressions.Regex.IsMatch(asciiT, @"(19|20)\d\d");
                bool hasNin = asciiT.Contains("NTENDO", StringComparison.OrdinalIgnoreCase)
                    || asciiT.Contains("NTEND", StringComparison.OrdinalIgnoreCase)
                    || asciiT.Contains("WINT", StringComparison.OrdinalIgnoreCase);
                return hasYear && hasNin;
            });
            lines.Sort((a, b2) => a.y.CompareTo(b2.y));
            var rows = new List<(int y, string t, bool ja)>();
            foreach (var ln in lines)
            {
                var ordered = ln.parts.OrderBy(p => p.X).Select(p => p.Text.Trim()).ToList();
                string joined = string.Join(" ", ordered);
                // Menu numbers: bare 1-9 (comma dropped) -> full １、 style.
                string joinedH = ToHalfwidth(joined).Trim();
                if (joinedH.Length == 1 && joinedH[0] >= '1' && joinedH[0] <= '9')
                    joined = char.ConvertFromUtf32('１' + (joinedH[0] - '1')) + "、";
                // Slot numeral split from its name across blocks ("２"+"らむね"):
                // reattach the dropped 、.
                if (ordered.Count > 1)
                {
                    string firstH = ToHalfwidth(ordered[0]).Trim();
                    if (firstH.Length == 1 && firstH[0] >= '1' && firstH[0] <= '9' && !joined.Contains("、"))
                    {
                        string rest = joined.Substring(ordered[0].Length).TrimStart();
                        joined = char.ConvertFromUtf32('１' + (firstH[0] - '1')) + "、" + (rest.Length > 0 ? " " + rest : "");
                    }
                }
                // Slot names: ellipsis-only tail (misread Latin like JOHN -> ...)
                // with a name block present = filled but unreadable -> "------".
                // Bare number alone = truly empty. Never show dot-garbage.
                string slotH = ToHalfwidth(joined).Trim();
                if (slotH.Length >= 1 && slotH[0] >= '1' && slotH[0] <= '9')
                {
                    string tail = slotH.Length > 1 ? slotH.Substring(1).TrimStart('、', '.', '．', ' ', ',', '，') : "";
                    if (tail.Length == 0 || IsEllipsisOnly(tail))
                    {
                        bool hasNameBlock = ordered.Count > 1 && ordered.Skip(1).Any(p => !string.IsNullOrWhiteSpace(p));
                        string bare = char.ConvertFromUtf32('１' + (slotH[0] - '1')) + "、";
                        joined = hasNameBlock ? bare + " ----" : bare;
                    }
                }
                // Zelda menu pack: arrows + title fill. OCR is fullwidth, packs check halfwidth.
                string ascii = ToHalfwidth(joined);
                if ((ascii.Contains("PLAYER") && ascii.Contains("SELECT")) || IsPlayerSelectHeader(ascii))
                    joined = "< PLAYER SELECT >";
                if (joined == "◆ＰＬＡＹＥＲ" || joined == "ＳＥＬＥＣＴＡ" || joined == "ＳＥＬＥＣＴ＞") { /* merged below */ }
                // M8 moved: title fill lives at FullTitle site below; this no-op block deleted.
                if (string.IsNullOrWhiteSpace(joined)) continue;
                if (IsEllipsisOnly(joined)) continue;
                bool ja = HasJapanese(joined);
                string asciiJa = ToHalfwidth(joined);
                bool isNumberCandidate = joined == "ま、" || joined == "は、" || joined == "ほ、" || joined == "ぱ、" || joined == "…" || joined == "...";
                if (ja && IsJunkOcr(joined) && !isNumberCandidate && !asciiJa.Contains("KILL", StringComparison.OrdinalIgnoreCase)) continue;
                string gridCore = System.Text.RegularExpressions.Regex.Replace(joined.Trim(), @"[．.… 　、。，]+", "");
                if (ja && System.Text.RegularExpressions.Regex.IsMatch(gridCore, @"^[ぁ-ん]{1,2}$")) continue;
                if (!ja && !Packs.PackRegistry.Current.IsMenuHeaderRow(asciiJa)) continue;
                joined = System.Text.RegularExpressions.Regex.Replace(joined, @"\s+[0-9０-９ ]{2,}\s*$", "");
                rows.Add((ln.y, joined, ja));
            }
            if (blocks.Count < 3) { _menuStableCount = 0; _menuSignature = ""; return; }
            if (!rows.Any(a => a.ja)) { _menuStableCount = 0; _menuSignature = ""; return; }
            // Single blank line between sections only (header/numbers/modes).
            var jaRows = new List<(int y, string t, bool ja)>();
            for (int i = 0; i < rows.Count; i++)
            {
                jaRows.Add(rows[i]);
                if (i + 1 < rows.Count && rows[i + 1].y - rows[i].y > 200)
                    jaRows.Add((rows[i].y + 1, "", rows[i].ja));
            }
            // Zelda title pack: single line. Tolerant トライフォ prefix (トライフォス misread).
            for (int i = 0; i < jaRows.Count; i++)
                if (jaRows[i].t.Contains(Packs.PackRegistry.Current.TitleMarker) && !jaRows[i].t.Contains("ゼルダ"))
                    jaRows[i] = (jaRows[i].y, Packs.PackRegistry.Current.FullTitle, true);
            // Split header repair: PLAYER row + SELECT row adjacent -> one header.
            for (int i = 0; i + 1 < jaRows.Count; i++)
            {
                string aH = ToHalfwidth(jaRows[i].t);
                string bH = ToHalfwidth(jaRows[i + 1].t);
                bool aP = aH.Contains("PLAYER");
                bool bS = bH.Contains("SELECT");
                bool aS = aH.Contains("SELECT");
                bool bP = bH.Contains("PLAYER");
                if ((aP && bS) || (aS && bP))
                {
                    jaRows[i] = (jaRows[i].y, "< PLAYER SELECT >", false);
                    jaRows.RemoveAt(i + 1);
                    break;
                }
            }
            RepairMenuNumberSequence(jaRows);
            DropStrayNumberCandidates(jaRows);
            InsertMenuSectionBlank(jaRows);
            // In-text decisions claim priority over dialogue (choice screens
            // would otherwise dwell-post as merged dialogue, e.g. Zelda cell).
            // Tall dialogue blocks skip Y-grouping (art filter), so re-attach
            // them here - the question mark usually lives inside the big block.
            bool isDecision = false;
            var decLines = lines.ToList();
            foreach (var b in blocks)
            {
                if (b.Height > 150 && HasJapanese(b.Text))
                    decLines.Add((b.Y, new List<OcrBlock> { b }));
            }
            if (TryBuildDecisionRows(decLines, out var decRows))
            {
                jaRows = decRows;
                isDecision = true;
            }
            bool isTitleFrame = (blocks.Count <= 3 && jaRows.Count(a => a.ja && !string.IsNullOrWhiteSpace(a.t)) <= 2 && jaRows.Any(a => a.t.Contains(Packs.PackRegistry.Current.TitleMarker)));
            // Fragments on dialogue screens are not menus: few rows, little JA,
            // little total content. (Title + kanji-dense single headers exempt.)
            if (!isTitleFrame)
            {
                var contentRows = jaRows.Where(a => !string.IsNullOrWhiteSpace(a.t)).ToList();
                int _totalChars = 0;
                foreach (var r in contentRows) _totalChars += r.t.Length;
                bool headerOnly = contentRows.Count == 1 && _totalChars < 12 && contentRows[0].t.Count(c => c >= '一' && c <= '鿿') >= 2;
                int totalChars = 0;
                foreach (var r in contentRows) totalChars += r.t.Length;
                if (!headerOnly && (contentRows.Count < 2 || contentRows.Count(a => a.ja) < 2 || totalChars < 12)) { _menuStableCount = 0; _menuSignature = ""; return; }
            }
            string sig = string.Join("\n", jaRows.ConvertAll(o => o.t));
            // Dialogue owns shared rows: menu duplicating shown OR held dialogue
            // stays held, either direction (menu rows arrive junk-padded).
            // Never lets menus pre-empt dialogue mid-growth.
            string _refA = _lastTranslatedText ?? "";
            string _refB = _candidateText ?? "";
            if (!isDecision)
            foreach (var r in jaRows)
            {
                if (string.IsNullOrWhiteSpace(r.t)) continue;
                bool exactOwned = r.t == _refA || r.t == _refB;
                if (exactOwned) { _menuStableCount = 0; _menuSignature = ""; return; }
                if (r.t.Length < 10) continue;
                bool owned = (!string.IsNullOrEmpty(_refA) && (_refA.Contains(r.t, StringComparison.Ordinal) || r.t.Contains(_refA, StringComparison.Ordinal)))
                    || (!string.IsNullOrEmpty(_refB) && (_refB.Contains(r.t, StringComparison.Ordinal) || r.t.Contains(_refB, StringComparison.Ordinal)));
                if (owned) { _menuStableCount = 0; _menuSignature = ""; return; }
            }
            if (sig == _menuSignature) _menuStableCount++;
            else { _menuSignature = sig; _menuStableCount = 1; }
            if (_menuStableCount < 2) return;
            // Growth (3./KILL appearing, save names) updates in place.
            // Same menu evolved = high similarity -> update, not suppress.
            // Different screen = low similarity -> fall through to new bubble.
            if (!string.IsNullOrEmpty(_lastPublishedMenu) && sig != _lastPublishedMenu)
            {
                double sim0 = ComputeSimilarity(sig, _lastPublishedMenu);
                double overlap = MenuRowOverlap(sig, _lastPublishedMenu);
                if (sim0 >= 0.70 || overlap >= 0.60 || sig.Contains(_lastPublishedMenu) || _lastPublishedMenu.Contains(sig))
                {
                    string prev = _lastPublishedMenu;
                    _lastPublishedMenu = sig;
                    if (isDecision) _lastDecisionSig = sig;
                    _ = TranslateAndUpdateMenuAsync(prev, jaRows);
                    return;
                }
                // Same kind, high row overlap = OCR jitter, not a new screen.
                // Update in place so stable save-select never duplicates.
                if (overlap >= 0.50)
                {
                    string prev = _lastPublishedMenu;
                    _lastPublishedMenu = sig;
                    if (isDecision) _lastDecisionSig = sig;
                    _ = TranslateAndUpdateMenuAsync(prev, jaRows);
                    return;
                }
            }
            if (sig == _lastPublishedMenu) return;
            _lastPublishedMenu = sig;
            if (isDecision) _lastDecisionSig = sig;
                        bool isTitle = (blocks.Count <= 3 && jaRows.Count(a => a.ja && !string.IsNullOrWhiteSpace(a.t)) <= 2 && jaRows.Any(a => a.t.Contains(Packs.PackRegistry.Current.TitleMarker)));
            string kind = isTitle ? "TITLE" : (isDecision ? "DECISION" : "MENU");
            FileLogger.Log($"[menu] publish {kind} lines={jaRows.Count}");
            _ = TranslateAndPublishMenuAsync(kind, jaRows);
        }

        // Battle-frame pipeline: slot extraction (BattleFrame.cs), 2-consistent-read
        // fire, consistent changes update in place (panel owns new-vs-refresh via
        // the last-entry rule). Rewards no longer dialogue-fire; the frame owns them.
        // This bit: monster + party only. Action content is extracted but withheld
        // from the frame until the overlap bit; monster rows go sticky so opening
        // the action menu never blanks them.
        private void UpdateBattleFrame(List<OcrBlock> blocks)
        {
            string topRaw;
            int cls = BattleFrame.Classify(blocks, _battleFrameW, _battleFrameH, HasJapanese, out topRaw);
            // Leaving battle clears the fired mark: an identical fight later refires
            // (panel spawns new only if other text came between), menus flow again.
            if (cls == 0) { _battlePrevSig = ""; _battleFiredSig = ""; _battleMonsterSticky = new List<string>(); return; }
            // Any battle-classified loop refreshes grace, so the countdown only
            // starts once battle truly ends (fires set it too, same value).
            _battleCooldown = 6;
            var slots = BattleFrame.ExtractSlots(blocks, _battleFrameW, _battleFrameH, HasJapanese, t => IsEllipsisOnly(t));
            // Party slot is names-only (HP/timebar digits complicate without
            // informing); monster counts stay. Reward tops need substance:
            // まえば-class flicker stays dialogue-suppressed but never posts,
            // so the top bar sits empty until real post-fight rewards arrive.
            var rightNames = slots.Right.ConvertAll(r => { SplitRowDigits(r, out string n, out _); return string.IsNullOrWhiteSpace(n) ? r : n; });
            slots = slots with { Top = slots.Top.Length < 4 ? "" : slots.Top, Right = rightNames };
            // Overlap bit: action rows normalize through pack fixups, then split
            // on pack command vocabulary into positional rows (blank magic gap).
            var normAction = slots.Action.ConvertAll(a => ApplyZeldaOcrFixups(a));
            // Tall-block disambiguation: geometry alone files a tall monster
            // list (two names, one OCR block — same height as the command box)
            // into the action slot. Only lines carrying command vocabulary (or
            // short enough to be a command fragment) stay; the rest spill back
            // to the monster column with their count. Unknown game (no vocab):
            // geometry as before.
            var commands = Packs.PackRegistry.Current.BattleCommands ?? new System.Collections.Generic.List<string>();
            var actionLines = new List<string>();
            var spillMonster = new List<string>();
            if (commands.Count < 5) actionLines = new List<string>(normAction);
            else
            {
                int maxCmd = 0;
                foreach (var c in commands) if (!string.IsNullOrEmpty(c) && c.Length > maxCmd) maxCmd = c.Length;
                foreach (var a in normAction)
                {
                    if (ContainsBattleCommand(a, commands) || a.Length <= maxCmd + 4) actionLines.Add(a);
                    else if (!string.IsNullOrWhiteSpace(a)) spillMonster.Add(a);
                }
            }
            var monsterLines = new List<string>(spillMonster);
            monsterLines.AddRange(slots.Monster);
            monsterLines = SplitMergedMonsters(monsterLines);
            monsterLines = AttachMonsterCounts(monsterLines);
            if (monsterLines.Count > 0) _battleMonsterSticky = new List<string>(monsterLines);
            var shownMonster = actionLines.Count > 0 && _battleMonsterSticky.Count > 0 ? _battleMonsterSticky : monsterLines;
            var actionRows = BattleFrame.SplitActionRows(actionLines, commands);
            var shown = new BattleFrame.BattleSlots(slots.Top, shownMonster, actionRows, slots.Right);
            string sig = shown.Top + "\n" + string.Join("\n", shown.Monster) + "\n" + string.Join("\n", shown.Action) + "\n" + string.Join("\n", shown.Right);
            // Prefetch on first sight: translations run during the stability wait,
            // so the fire completes near-instantly. Same strings and providers as
            // the publish path — strictly earlier, never different.
            if (sig != _battlePrevSig) PrefetchBattleRows(shown);
            // prev updates BEFORE the fire: a fully-cached fast leg completes
            // synchronously on this same thread and must see the current mark
            // at its check — otherwise every instant fire suicides (fire==cur
            // drops) and only yielding fires ever publish. Stuck action boxes
            // and missing rewards traced to exactly this ordering.
            _battlePrevSig = sig;
            // Battle fires on first sight: the frame updates in place and stale
            // publishes drop, so a lone misread self-corrects next loop instead of
            // waiting out a stability bar while the game moves on. Dialogue and
            // menu keeps their frozen gates; battle is the live surface.
            // Action rides the sig, so open/close always counts as a change.
            if (sig != _battleFiredSig && (shown.Top.Length > 0 || shown.Monster.Count > 0 || shown.Action.Count > 0))
            {
                _battleFiredSig = sig;
                _battleCooldown = 6;
                FileLogger.Log($"[battle] fire top={shown.Top.Length}ch mon={shown.Monster.Count} act={shown.Action.Count} right={shown.Right.Count} sig={SigTag(sig)}");
                _ = TranslateAndPublishBattleAsync(sig, shown);
            }
        }

        // One row translated at most once per unique text: exact JA matches reuse
        // last published EN verbatim, so refires are byte-identical and free.
        // Prefetch memo: first-sight translations land here while stability waits,
        // so fires complete near-instantly. Exact-text keyed, cleared on pack
        // switch (translations are provider-versioned; the memo is not).
        private readonly Dictionary<string, string> _prefetch = new();
        private readonly object _prefetchLock = new();
        private string _prefetchPack = "";
        private void PrefetchBattleRows(BattleFrame.BattleSlots slots)
        {
            try
            {
                string pack = Packs.PackRegistry.Current.GameId;
                var fresh = new List<(string ja, System.Func<System.Threading.Tasks.Task<string>> tx)>();
                lock (_prefetchLock)
                {
                    if (_prefetchPack != pack) { _prefetch.Clear(); _battleNameMemo.Clear(); _prefetchPack = pack; }
                    if (!string.IsNullOrWhiteSpace(slots.Top) && !_prefetch.ContainsKey(slots.Top))
                    {
                        string t = slots.Top;
                        _prefetch[t] = "";
                        fresh.Add((t, () => _translationManager.TranslateAsync(t)));
                    }
                    foreach (var l in slots.Monster) PrefetchNeed(l, () => TranslateMenuJaRowAsync(l), fresh);
                    foreach (var l in slots.Action) PrefetchNeed(l, () => TranslateMenuJaRowAsync(l), fresh);
                    foreach (var n in slots.Right) PrefetchNeed(n, () => _translationManager.TranslateAsync(n), fresh);
                    if (_prefetch.Count > 1000) _prefetch.Clear();
                }
                foreach (var (ja, tx) in fresh)
                    _ = PrefetchOneAsync(ja, tx);
            }
            catch { }
        }
        private void PrefetchNeed(string ja, System.Func<System.Threading.Tasks.Task<string>> tx, List<(string ja, System.Func<System.Threading.Tasks.Task<string>> tx)> fresh)
        {
            if (string.IsNullOrWhiteSpace(ja) || _prefetch.ContainsKey(ja)) return;
            _prefetch[ja] = "";
            fresh.Add((ja, tx));
        }
        private async Task PrefetchOneAsync(string ja, System.Func<System.Threading.Tasks.Task<string>> tx)
        {
            try
            {
                string en = await tx();
                lock (_prefetchLock) { _prefetch[ja] = en; }
            }
            catch { lock (_prefetchLock) { _prefetch.Remove(ja); } }
        }
        private bool TryPrefetch(string ja, out string en)
        {
            lock (_prefetchLock) { return _prefetch.TryGetValue(ja, out en!) && !string.IsNullOrEmpty(en); }
        }
        private async Task<string> PrefetchOrTranslateAsync(string ja, System.Func<System.Threading.Tasks.Task<string>> tx)
        {
            if (TryPrefetch(ja, out var pre)) return pre;
            return await tx();
        }
        private async Task<List<string>> TranslateBattleRowsAsync(List<string> ja, List<string> lastJa, List<string> lastEn, System.Func<string, System.Threading.Tasks.Task<string>> translateOne, bool simMemo = false)
        {
            var sticky = new Dictionary<string, string>();
            if (lastJa != null && lastEn != null && lastJa.Count == lastEn.Count && lastJa.Count > 0)
            {
                for (int i = 0; i < lastJa.Count; i++)
                    if (!sticky.ContainsKey(lastJa[i])) sticky[lastJa[i]] = lastEn[i];
            }
            // Fan-out, not sequence: every miss starts before the first await, so
            // N rows cost the slowest row, not the sum. Order preserved by index.
            var tasks = new Task<string>[ja.Count];
            // Intentional gaps (the blank magic row) bypass everything and stay
            // "" — the "------" mask is for failed translations, not for slots
            // that are empty on purpose.
            var emptyBypass = new bool[ja.Count];
            for (int i = 0; i < ja.Count; i++)
            {
                string l = ja[i];
                if (string.IsNullOrEmpty(l)) { tasks[i] = Task.FromResult(""); emptyBypass[i] = true; }
                else if (sticky.TryGetValue(l, out var e)) tasks[i] = Task.FromResult(e);
                else tasks[i] = TranslateRowSplitAsync(l, translateOne, simMemo);
            }
            var ens = await Task.WhenAll(tasks);
            var result = new List<string>(ens.Length);
            for (int i = 0; i < ens.Length; i++) result.Add(emptyBypass[i] ? "" : (IsUnpublishable(ens[i]) ? "------" : ens[i]));
            return result;
        }

        // Digit-split: HP/counts never burn translation. A 16s DeepL call for
        // "２" once gated a whole rewards fire (slowest row wins the WhenAll),
        // so every action/rewards publish died stale. Digit-only rows post
        // verbatim; mixed rows translate the name part only (prefetch-keyed on
        // the name, so HP flicker reuses it instantly) and reattach digits
        // verbatim ("Mash 231"). Title-agnostic: digits are digits everywhere.
        private static void SplitRowDigits(string row, out string name, out string digits)
        {
            name = row; digits = "";
            if (string.IsNullOrWhiteSpace(row)) return;
            var names = new List<string>();
            var digs = new List<string>();
            foreach (var k in row.Split(new[] { ' ', '　' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string s = k.Trim('・', '…', '．', '.', '「', '」', '"', '\'', '、', '。', '!', '！', '?', '？', ',', '，');
                bool isDigits = s.Length > 0;
                foreach (char c in s)
                    if (!((c >= '0' && c <= '9') || (c >= '０' && c <= '９'))) { isDigits = false; break; }
                if (isDigits) digs.Add(k); else names.Add(k);
            }
            if (digs.Count == 0) return;
            name = string.Join(" ", names); digits = string.Join(" ", digs);
        }
        private async Task<string> TranslateRowSplitAsync(string row, System.Func<string, System.Threading.Tasks.Task<string>> translateOne, bool simMemo = false)
        {
            SplitRowDigits(row, out string name, out string digits);
            if (string.IsNullOrWhiteSpace(name)) return row;
            if (string.IsNullOrEmpty(digits))
            {
                string whole = await PrefetchOrTranslateAsync(row, () => translateOne(row));
                if (simMemo) MemoizeBattleName(row, whole);
                return whole;
            }
            string en;
            if (simMemo && TrySimilarBattleEn(name, out var simEn))
            {
                en = simEn;
                FileLogger.Log($"[battle] memo-sim \"{name}\" -> \"{en}\"");
            }
            else
            {
                en = await PrefetchOrTranslateAsync(name, () => translateOne(name));
                if (simMemo) MemoizeBattleName(name, en);
            }
            if (string.IsNullOrWhiteSpace(en)) return row;
            return en + " " + digits;
        }
        // Battle-name memo: OCR flicker (シビーラビット/シピーラビット/…)
        // mints a new exact key every loop, so exact memoization never hits
        // and every variant burns a fresh 7s translation that dies stale.
        // Levenshtein ≥ 0.7 reuses EN across flicker (logged, auditable);
        // fallback serves ARE memoized here (battle-scoped — the dialogue
        // no-fallback-cache rule is untouched). Refusals/echoes never
        // memoize. Cleared on pack switch. Action rows excluded (reusing a
        // hallucinated ability EN faster is faster-wrong, not better).
        private readonly Dictionary<string, string> _battleNameMemo = new();
        private const double BattleSimThreshold = 0.7;
        private void MemoizeBattleName(string ja, string en)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ja) || string.IsNullOrWhiteSpace(en) || IsUnpublishable(en)) return;
                lock (_prefetchLock)
                {
                    if (_battleNameMemo.Count > 200) _battleNameMemo.Clear();
                    _battleNameMemo[ja] = en;
                }
            }
            catch { }
        }
        private bool TrySimilarBattleEn(string name, out string en)
        {
            en = "";
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return false;
                lock (_prefetchLock)
                {
                    double best = BattleSimThreshold;
                    string bestEn = "";
                    foreach (var kv in _battleNameMemo)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                        if (kv.Key == name) { en = kv.Value; return true; }
                        double s = ComputeSimilarity(name, kv.Key);
                        if (s > best) { best = s; bestEn = kv.Value; }
                    }
                    if (bestEn.Length > 0) { en = bestEn; return true; }
                }
            }
            catch { }
            return false;
        }
        // Sig tags: drops were anonymous — the next log convicts exactly
        // which fire died and what superseded it.
        // Command-vocabulary test for the tall-block gate: a line stays in the
        // action slot only if it carries a known command (or is short enough
        // to be a command fragment). Long garble-free non-command lines are
        // merged monster lists and spill back to the monster column.
        private static bool ContainsBattleCommand(string line, System.Collections.Generic.IReadOnlyList<string> commands)
        {
            if (string.IsNullOrEmpty(line) || commands == null) return false;
            foreach (var c in commands)
                if (!string.IsNullOrEmpty(c) && line.Contains(c, System.StringComparison.Ordinal)) return true;
            return false;
        }
        // Merged-monster split: two names in one OCR block ("エビオルニス
        // シビーラビット", one tall block) post as one long wrapping line.
        // Split on memoized single names, longest first — self-improving:
        // single-name fights memoize parts, later merges split around them.
        // Cold start (nothing known yet) keeps the merged line; nothing is
        // ever hidden or dropped, only divided.
        private List<string> SplitMergedMonsters(List<string> rows)
        {
            List<string> keys;
            lock (_prefetchLock)
                keys = _battleNameMemo.Keys.Where(k => k.Length >= 5 && k.Length <= 9).OrderByDescending(k => k.Length).ToList();
            if (keys.Count == 0) return rows;
            var out_ = new List<string>(rows.Count);
            foreach (var r in rows)
            {
                var parts = new List<string> { r };
                bool changed = true;
                int guard = 0;
                while (changed && guard++ < 4)
                {
                    changed = false;
                    var next = new List<string>();
                    foreach (var p in parts)
                    {
                        string? hit = null;
                        foreach (var k in keys)
                            if (p.Length > k.Length + 1 && p.Contains(k, System.StringComparison.Ordinal)) { hit = k; break; }
                        if (hit is null) { next.Add(p); continue; }
                        int ix = p.IndexOf(hit, System.StringComparison.Ordinal);
                        string a = p.Substring(0, ix), b = p.Substring(ix + hit.Length);
                        if (a.Length >= 2 || b.Length >= 2)
                        {
                            if (a.Length >= 2) next.Add(a);
                            next.Add(hit);
                            if (b.Length >= 2) next.Add(b);
                            changed = true;
                        }
                        else next.Add(p);
                    }
                    parts = next;
                }
                out_.AddRange(parts);
            }
            return out_;
        }
        // Count attach: a standalone count ("２") joins the preceding monster
        // ("シビーラビット　２", same line as in-game) instead of floating
        // alone. No preceding name: stays standalone (truthful over clever).
        // Deliberately no trailing gap on count-less names: trailing space is
        // invisible in a left-aligned box but churns exact cache keys (an extra
        // full retranslation per row) — cost without gain.
        private static List<string> AttachMonsterCounts(List<string> rows)
        {
            var out_ = new List<string>(rows.Count);
            foreach (var r in rows)
            {
                SplitRowDigits(r, out string name, out string digits);
                if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(digits) && out_.Count > 0)
                {
                    SplitRowDigits(out_[^1], out string prevName, out _);
                    if (!string.IsNullOrWhiteSpace(prevName)) { out_[^1] = out_[^1].TrimEnd() + "　" + digits; continue; }
                }
                out_.Add(r);
            }
            return out_;
        }
        private static string SigTag(string sig)
        {
            if (string.IsNullOrEmpty(sig)) return "-";
            string t = sig.Replace("\n", "|").Replace("\r", "");
            return t.Length <= 44 ? t : t.Substring(0, 44) + "…";
        }

        // Fast/refresh split: the frame is a live surface — it must post inside
        // the state's lifetime (~2-8s), but one miss costs 10-25s (CAT
        // refusal-gens + DeepL fallback) and the slowest row gated the whole
        // frame, so every action/rewards fire died stale. Now: a 1200ms
        // best-effort fast publish (slow rows post JA-verbatim — truthful
        // pending state, never "------"-masked), then the full translation
        // refreshes in place if still current. Newer fires always supersede;
        // currency checks prevent inversion.
        private const int BattleFastMs = 1200;
        private static async Task<T> WithTimeout<T>(Task<T> full, T fallback, int ms)
        {
            if (full.IsCompletedSuccessfully) return await full;
            if (ms <= 0) return await full;
            var winner = await Task.WhenAny(full, Task.Delay(ms));
            if (winner == full) return await full;
            return fallback;
        }
        private static async Task<List<string>> BestEffortRowsAsync(Task<List<string>> full, List<string> jaFallback)
        {
            // Blank-check only, with intentional gaps staying blank to match the
            // full leg: IsUnpublishable would nuke JA-verbatim into "------",
            // but the pending JA text IS the truthful fast state.
            var fb = new List<string>(jaFallback.Count);
            foreach (var j in jaFallback) fb.Add(string.IsNullOrEmpty(j) ? "" : j);
            return await WithTimeout(full, fb, BattleFastMs);
        }

        private async Task TranslateAndPublishBattleAsync(string sig, BattleFrame.BattleSlots slots)
        {
            try
            {
                // Whole-frame fan-out: top + all rows start together, wall time is
                // the slowest row, not the sum. Results land back in slot order.
                var topFull = (slots.Top.Length > 0 && slots.Top == _lastBattleTopJa) ? Task.FromResult(_lastBattleTopEn)
                    : (string.IsNullOrWhiteSpace(slots.Top) ? Task.FromResult("") : PrefetchOrTranslateAsync(slots.Top, () => _translationManager.TranslateAsync(slots.Top)));
                var monFull = TranslateBattleRowsAsync(slots.Monster, _lastBattleMonJa, _lastBattleMonEn, l => TranslateMenuJaRowAsync(l), true);
                var actFull = TranslateBattleRowsAsync(slots.Action, _lastBattleActJa, _lastBattleActEn, l => TranslateMenuJaRowAsync(l));
                var rightFull = TranslateBattleRowsAsync(slots.Right, _lastBattleRightJa, _lastBattleRightEn, n => _translationManager.TranslateAsync(n), true);
                // Fast leg: best-effort inside the state's lifetime. All four
                // start before the first await, so the frame costs ~1.2s max.
                var topFastTask = WithTimeout(topFull, slots.Top, BattleFastMs);
                var monFastTask = BestEffortRowsAsync(monFull, slots.Monster);
                var actFastTask = BestEffortRowsAsync(actFull, slots.Action);
                var rightFastTask = BestEffortRowsAsync(rightFull, slots.Right);
                await Task.WhenAll(topFastTask, monFastTask, actFastTask, rightFastTask);
                string topEn = await topFastTask;
                var monEn = await monFastTask;
                var actEn = await actFastTask;
                var rightEn = await rightFastTask;
                // Stale-drop: translation outlived the state it captured. The
                // newest state always has its own fast fire ~1s behind, so
                // dropping wrong beats posting wrong.
                if (_battleFiredSig != sig || _battlePrevSig != sig) { FileLogger.Log($"[battle] stale publish dropped fire={SigTag(sig)} cur={SigTag(_battleFiredSig)}"); return; }
                FileLogger.Log($"[battle] frame publish top={slots.Top.Length}ch mon={slots.Monster.Count} act={slots.Action.Count} right={slots.Right.Count} fast sig={SigTag(sig)}");
                BattleFramePublished?.Invoke(slots.Top, topEn, slots.Monster, monEn, slots.Action, actEn, slots.Right, rightEn);
                // Refresh leg: real EN when the slow rows land, if still current.
                // Corpus records the full pair only (never JA-verbatim fallbacks).
                _ = RefreshBattleWhenReadyAsync(sig, slots, topFull, monFull, actFull, rightFull);
            }
            catch (Exception ex) { FileLogger.Log($"[battle] ERROR: {ex.Message}"); }
        }
        private async Task RefreshBattleWhenReadyAsync(string sig, BattleFrame.BattleSlots slots, Task<string> topFull, Task<List<string>> monFull, Task<List<string>> actFull, Task<List<string>> rightFull)
        {
            try
            {
                await Task.WhenAll(topFull, monFull, actFull, rightFull);
                string topEn = await topFull;
                var monEn = await monFull;
                var actEn = await actFull;
                var rightEn = await rightFull;
                // Memos warm even when superseded (yesterday's behavior: keeps
                // sticky/prefetch hot for the next fire).
                _lastBattleTopJa = slots.Top;
                _lastBattleTopEn = topEn;
                _lastBattleMonJa = new List<string>(slots.Monster);
                _lastBattleMonEn = new List<string>(monEn);
                _lastBattleActJa = new List<string>(slots.Action);
                _lastBattleActEn = new List<string>(actEn);
                _lastBattleRightJa = new List<string>(slots.Right);
                _lastBattleRightEn = new List<string>(rightEn);
                // Silent when superseded: a newer fire owns the panel, and the
                // fast leg already posted something truthful. No drop-spam.
                if (_battleFiredSig != sig || _battlePrevSig != sig) return;
                FileLogger.Log($"[battle] frame publish top={slots.Top.Length}ch mon={slots.Monster.Count} act={slots.Action.Count} right={slots.Right.Count} full sig={SigTag(sig)}");
                try { CorpusRecordBubble("battle-frame:fire", slots.Top + "\n" + string.Join("\n", slots.Monster) + "\n" + string.Join("\n", slots.Action) + "\n" + string.Join("\n", slots.Right), topEn + "\n" + string.Join("\n", monEn) + "\n" + string.Join("\n", actEn) + "\n" + string.Join("\n", rightEn)); } catch { }
                BattleFramePublished?.Invoke(slots.Top, topEn, slots.Monster, monEn, slots.Action, actEn, slots.Right, rightEn);
            }
            catch { }
        }

        // Menu Latin-token split (universal machinery, N3): JA rows with embedded fullwidth-Latin
        // art (ＥｏｌｌＰｈｏＥＮＴ-class) translate the JA part only; art reattaches verbatim,
        // never burns CAT. Allowlist/coords/button shapes stay inline. JA bubble text untouched.
        private async Task<string> TranslateMenuJaRowAsync(string t)
        {
            var toks = t.Split(' ');
            var held = new List<string>();
            var kept = new List<string>();
            foreach (var k in toks)
            {
                string s = k.Trim('・', '…', '．', '.', '「', '」', '"', '\'', '、', '。', '!', '！', '?', '？', ',', '，', ' ', '　');
                bool latin = s.Length >= 2;
                foreach (char c in s)
                {
                    if ((c >= 'ぁ' && c <= 'ん') || (c >= 'ァ' && c <= 'ヶ') || (c >= '一' && c <= '鿿')) { latin = false; break; }
                }
                string u = s.ToUpperInvariant();
                if (latin && (u == "MAP" || u == "START" || u == "PUSH" || u == "SELECT" || u == "COPY" || u == "KILL"
                    || System.Text.RegularExpressions.Regex.IsMatch(u, @"^[0-9]+[A-Z]+$") || System.Text.RegularExpressions.Regex.IsMatch(u, @"^[A-Z]+[0-9]+$")))
                    latin = false;
                if (latin) held.Add(k); else kept.Add(k);
            }
            string jaPart = string.Join(" ", kept);
            if (string.IsNullOrWhiteSpace(jaPart)) return t;
            if (held.Count == 0) return await _translationManager.TranslateAsync(t);
            string e = await _translationManager.TranslateAsync(jaPart);
            return e + " " + string.Join(" ", held);
        }
        private async Task TranslateAndPublishMenuAsync(string kind, List<(int y, string t, bool ja)> rows)
        {
            try
            {
                // Fan-out like the battle path: every row starts together, wall time
                // is the slowest row. Order preserved by index; sync shortcuts stay.
                // Faults still fail the whole publish via WhenAll (as before).
                var tasks = new Task<string>[rows.Count];
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    if (kind == "TITLE") { string tt = string.IsNullOrWhiteSpace(r.t) ? Packs.PackRegistry.Current.FullTitle : r.t; tasks[i] = Task.FromResult(Packs.PackRegistry.Current.ApplyTitleFixup(tt) ?? tt); continue; }
                    if (string.IsNullOrWhiteSpace(r.t)) { tasks[i] = Task.FromResult(""); continue; }
                    if (!r.ja || IsSlotRow(r.t)) { tasks[i] = Task.FromResult(r.t); continue; }
                    tasks[i] = TranslateOneMenuRowAsync(r.t);
                }
                var got = await Task.WhenAll(tasks);
                var en = new List<string>(got);
                FileLogger.Log($"[menu] {kind} -> {en.Count} lines");
                try { CorpusRecordBubble("menu-fire:" + kind, string.Join("\n", rows.ConvertAll(r => r.t)), string.Join("\n", en)); } catch { }
                MenuTranslated?.Invoke(kind, string.Join("\n", rows.ConvertAll(r => r.t)), string.Join("\n", en));
            }
            catch (Exception ex) { FileLogger.Log($"[menu] ERROR: {ex.Message}"); }
        }


        private async Task<string> TranslateOneMenuRowAsync(string t)
        {
            string e = await TranslateMenuJaRowAsync(t);
            return IsUnpublishable(e) ? "------" : e;
        }
        private async Task TranslateAndUpdateMenuAsync(string oldSig, List<(int y, string t, bool ja)> rows)
        {
            try
            {
                var clean = rows.ToList();
                var en = new List<string>();
                foreach (var r in clean)
                {
                    if (string.IsNullOrWhiteSpace(r.t)) { en.Add(""); continue; }
                    if (!r.ja || IsSlotRow(r.t)) { en.Add(r.t); continue; }
                    string e = await TranslateMenuJaRowAsync(r.t);
                    if (IsUnpublishable(e)) e = "------";
                    en.Add(e);
                }
                string newJa = string.Join("\n", clean.ConvertAll(r => r.t));
                string newEn = string.Join("\n", en);
                FileLogger.Log($"[menu] update -> {en.Count} lines");
                if (_stopped) return;
                // Kind derived from rows: TITLE only for Triforce single, else MENU.
                string kind2 = oldSig == _lastDecisionSig ? "DECISION" : (newJa.Contains(Packs.PackRegistry.Current.TitleMarker) && newJa.Split('\n').Length <= 2 ? "TITLE" : "MENU");
                try { CorpusRecordBubble("menu-update:" + kind2, newJa, newEn, oldSig); } catch { }
                MenuUpdated?.Invoke(oldSig, kind2, newJa, newEn);
            }
            catch (Exception ex) { FileLogger.Log($"[menu] ERROR: {ex.Message}"); }
        }

        // Pixel-font header reads dirty (PLAVER/SELECPA when merged or small).
        // Shape: leader + PLA…SELEC in order. Dialogue never matches this.
        private static bool IsPlayerSelectHeader(string ascii)
        {
            string t = ascii.Replace(" ", "").Replace("　", "").TrimStart('◆', '<', '>', '＞');
            return t.StartsWith("PLA", StringComparison.OrdinalIgnoreCase)
                && t.Contains("SELEC", StringComparison.OrdinalIgnoreCase);
        }

        // Fullwidth Latin/digits to ASCII for pack matching only. Display keeps original.
        private static string ToHalfwidth(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= 'Ａ' && c <= 'Ｚ') sb.Append((char)(c - 'Ａ' + 'A'));
                else if (c >= 'ａ' && c <= 'ｚ') sb.Append((char)(c - 'ａ' + 'a'));
                else if (c >= '０' && c <= '９') sb.Append((char)(c - '０' + '0'));
                else sb.Append(c);
            }
            return sb.ToString();
        }


        private static readonly Regex JapaneseCharsProbe = new(@"[぀-ヿ一-鿿]", RegexOptions.Compiled);
        private static bool HasJapanese(string s) => !string.IsNullOrEmpty(s) && JapaneseCharsProbe.IsMatch(s);

                // Sequential repair, not a word list: 1、2、 present but 3、 missing
        // and a lone short row (は、/ほ、/…) in its slot = mis-OCRed 3.
        // Slot-row prefix (save slots etc.): starts with 1-9 + separator.
        // Names after the number are user content: copied, never translated.
        private static bool IsSlotRow(string t)
        {
            string h = ToHalfwidth(t).Trim();
            if (h.Length == 0 || h[0] < '1' || h[0] > '9') return false;
            if (h.Length == 1) return true;
            char s = h[1];
            return s == '、' || s == '.' || s == '．' || s == ' ' || s == ',' || s == '，';
        }

        // Stray misreads that survived repair are junk, not content.
        private static void DropStrayNumberCandidates(List<(int y, string t, bool ja)> rows)
        {
            rows.RemoveAll(r => r.t == "ま、" || r.t == "は、" || r.t == "ほ、" || r.t == "ぱ、" || r.t == "は,");
        }

        private static void RepairMenuNumberSequence(List<(int y, string t, bool ja)> rows)
        {
            bool has1 = false, has2 = false, has3 = false;
            foreach (var r in rows)
            {
                string h = ToHalfwidth(r.t).Trim();
                if (h.Length == 0) continue;
                if (h[0] == '1' && (h.Length == 1 || h[1] == '、' || h[1] == '.' || h[1] == '．' || h[1] == ' ')) has1 = true;
                else if (h[0] == '2' && (h.Length == 1 || h[1] == '、' || h[1] == '.' || h[1] == '．' || h[1] == ' ')) has2 = true;
                else if (h[0] == '3' && (h.Length == 1 || h[1] == '、' || h[1] == '.' || h[1] == '．' || h[1] == ' ')) has3 = true;
            }
            if (!(has1 && has2 && !has3)) return;
            bool repaired = false;
            for (int i = 0; i < rows.Count; i++)
            {
                string t = rows[i].t;
                                if (t == "ま、" || t == "は、" || t == "ほ、" || t == "ぱ、" || t == "は," || t == "…" || t == "...")
                {
                    rows[i] = (rows[i].y, "３、", false);
                    FileLogger.Log("[menu] number-repair ->３、");
                    repaired = true;
                    break;
                }
            }
            // Dots-head recovery: numeral lost ("3. JAC" -> ". . JACK").
            // Same missing-slot condition, dot-noise head with real tail.
            if (!repaired)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    string h = ToHalfwidth(rows[i].t).Trim();
                    int k = 0;
                    while (k < h.Length && (h[k] == '.' || h[k] == '．' || h[k] == '…' || h[k] == ' ' || h[k] == '　' || h[k] == ',' || h[k] == '，' || h[k] == '、')) k++;
                    string tail = h.Substring(k);
                    if (k > 0 && tail.Length > 0 && !IsEllipsisOnly(tail))
                    {
                        rows[i] = (rows[i].y, "３、 " + rows[i].t.TrimStart('.', '．', '…', ' ', '　'), false);
                        FileLogger.Log("[menu] number-repair dots-head ->３、");
                        break;
                    }
                }
            }
        }

                // Aesthetic section gaps: blank after header, blank between 1、2、3、
        // block and modes.
        private static void InsertMenuSectionBlank(List<(int y, string t, bool ja)> rows)
        {
            if (rows.Count >= 2 && rows[1].t != "")
            {
                string h0 = ToHalfwidth(rows[0].t);
                if ((h0.Contains("PLAYER") && h0.Contains("SELECT")) || rows[0].t.StartsWith("<") || rows[0].t.StartsWith("◆"))
                    rows.Insert(1, ((rows[0].y + rows[1].y) / 2, "", false));
            }
            int lastNumber = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                string t = rows[i].t;
                if (IsSlotRow(t))
                    lastNumber = i;
                else if (lastNumber >= 0)
                    break;
            }
            if (lastNumber >= 0 && lastNumber + 1 < rows.Count && rows[lastNumber + 1].t != "")
            {
                int y = (rows[lastNumber].y + rows[lastNumber + 1].y) / 2;
                rows.Insert(lastNumber + 1, (y, "", false));
            }
        }

                // Input-grid gate (name entry etc.): full kana/alphabet chart. Shape
        // signal, not a word list: 10+ tiny single-char blocks across 3+ rows.
        // Suppressed in both tiers (zero DeepL/CAT cost, zero feed noise).
        private static bool IsInputGrid(List<OcrBlock> blocks)
        {
            if (blocks.Count < 10) return false;
            // Shop/menu lists carry tall merged content blocks (h=614 shop list); name-entry
            // charts never do (all cells tiny). Veto keeps shop menus publishable. Omission-safe:
            // dialogue textboxes never reach this gate as grids (few tiny cells).
            foreach (var b in blocks)
            {
                string bt = (b.Text ?? "").Trim();
                if (b.Height > 150 && bt.Length >= 6 && HasAnchorSubstance(bt)) return false;
            }
            int tiny = 0;
            var rows = new HashSet<int>();
            foreach (var b in blocks)
            {
                string t = b.Text.Trim();
                if (t.Length > 2) continue;
                bool single = false;
                foreach (char c in t)
                {
                    if ((c >= 'ぁ' && c <= 'ん') || (c >= 'ァ' && c <= 'ヶ') || c == 'ー'
                        || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                    { single = true; break; }
                }
                if (!single) continue;
                tiny++;
                rows.Add(b.Y / 25);
            }
            return tiny >= 10 && rows.Count >= 3;
        }

                // Merged-blob chart signal: 20+ kana, zero kanji in one block.
        // Normal dialogue always mixes kanji; chart blobs never do.
        private static bool IsKanaChartScreen(List<OcrBlock> blocks)
        {
            foreach (var b in blocks)
            {
                string t = b.Text.Trim();
                if (t.Length < 20) continue;
                int kana = 0, kanji = 0;
                foreach (char c in t)
                {
                    if ((c >= 'ぁ' && c <= 'ん') || (c >= 'ァ' && c <= 'ヶ') || c == 'ー') kana++;
                    else if (c >= '一' && c <= '鿿') kanji++;
                }
                if (kana >= 20 && kanji == 0) return true;
            }
            return false;
        }

        private static double MenuRowOverlap(string a, string b)
        {
            var ra = new HashSet<string>(a.Split('\n'));
            var rb = new HashSet<string>(b.Split('\n'));
            if (ra.Count == 0 || rb.Count == 0) return 0.0;
            int hit = 0;
            foreach (var r in ra) if (rb.Contains(r)) hit++;
            return (double)hit / Math.Max(ra.Count, rb.Count);
        }

        private static string ComputeFrameHash(Bitmap bitmap)
        {
            using var small = new Bitmap(bitmap, new Size(32, 32));
            using var ms = new MemoryStream();
            small.Save(ms, ImageFormat.Bmp);
            using var md5 = MD5.Create();
            return Convert.ToBase64String(md5.ComputeHash(ms.ToArray()));
        }
    }
}