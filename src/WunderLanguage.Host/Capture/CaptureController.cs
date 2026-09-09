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
        private readonly string _gameId;
        private readonly object _mineLock = new();
        private string _successorText = string.Empty;
        private int _successorCount;
        private string _lastOcrText = string.Empty;
        private int _lastOcrContent;
        private int _lastOcrTotal;

        public event Action<string, string>? TextTranslated;
        public event Action<string, string, string>? MenuTranslated; // (kind TITLE/MENU, jaJoined, enJoined)
        public event Action<string, string, string, string>? MenuUpdated; // (expectedOldJa, kind, jaJoined, enJoined) update-only, never appends
        private string _menuSignature = "";
        private int _menuStableCount;
        private bool _frameIsTitle;
        private string _lastPublishedMenu = "";
        public event Action<string, string, string>? TextUpdated; // (expectedOldJa, newJa, en)
        public event Action? WindowLost;

        private int _consecutiveCaptureFailures;
        private const int MaxConsecutiveCaptureFailures = 5;

        private string? _lastFrameHash;
        private string? _pendingFrameHash;
        private int _pendingFrameStableCount;
        private const int RequiredPreOcrStablePolls = 1;

        private string _candidateText = string.Empty;
        private int _candidateStableCount;
        private bool _candidateGrewThisRead;
        private string _lastTranslatedText = string.Empty;
        private const int RequiredStableReadings = 3;
        private const int GrowthBreakthroughPolls = 10;
        private const int MinCandidateContentLength = 2;

        private const int IdleDelayMs = 400;
        private const int SettlingDelayMs = 150;

        private static readonly (string from, string to)[] ZeldaOcrFixups =
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
            ("もうそこまで止まっていました", "もうそこまでせまっていました"),
            ("剣と首", "剣と盾"),
            ("よくきげ", "よくきけ"),
            ("品はポタシ", "Ｂボタン"),
            ("ポタン", "ボタン"),
            ("ポタシ", "ボタン"),
            ("昨日Ｐを手に入れた", "マップを手に入れた"),
            ("ルビー", "ルピー"),
            ("とりせる", "とらせる"),
            ("ほっび", "ほうび"),
        };

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

        private Dictionary<string, string> _minedCorrections = new();

        private void LoadMinedCorrections()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage", "corrections", _gameId + ".json");
                if (File.Exists(path))
                    _minedCorrections = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>();
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
                string stripRaw = StripForOverlap(raw).Replace("・", "").Replace("！", "").Replace("!", "").Replace("？", "").Replace("?", "").Replace("＂", "").Replace("\"", "").Replace("「", "").Replace("」", "").Replace("〈", "").Replace("〉", "").Replace("『", "").Replace("』", "").Replace("【", "").Replace("】", "");
                string stripFixed = StripForOverlap(fixed_).Replace("・", "").Replace("！", "").Replace("!", "").Replace("？", "").Replace("?", "").Replace("＂", "").Replace("\"", "").Replace("「", "").Replace("」", "").Replace("〈", "").Replace("〉", "").Replace("『", "").Replace("』", "").Replace("【", "").Replace("】", "");
                if (stripRaw == stripFixed)
                    return;
                FileLogger.Log($"[mine] \"{raw}\" -> \"{fixed_}\"");
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
                    map[raw] = fixed_;
                    File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch { }
        }

        private static string ApplyZeldaOcrFixups(string text)
        {
            string fixed_ = text;
            foreach (var (from, to) in ZeldaOcrFixups)
            {
                if (fixed_.Contains(from, StringComparison.Ordinal))
                    fixed_ = fixed_.Replace(from, to, StringComparison.Ordinal);
            }
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
            fixed_ = Regex.Replace(fixed_, @"ささ[けばが]られ", "ささげられ");
            fixed_ = Regex.Replace(fixed_, @"イケ[三二]工", "イケニエ");
            fixed_ = Regex.Replace(fixed_, @"[狼塊]達", "娘達");
            fixed_ = Regex.Replace(fixed_, @"カンラ[ラテ]ラ", "カンテラ");
            fixed_ = Regex.Replace(fixed_, @"[訪初射]印", "封印");
            fixed_ = Regex.Replace(fixed_, @"司[^祭]{1,2}アグニム", "司祭アグニム");
            fixed_ = Regex.Replace(fixed_, @"[カガ]ンテラ", "カンテラ");
            if (!ReferenceEquals(fixed_, text) && fixed_ != text)
                FileLogger.Log($"[ocr-fixup] \"{text}\" -> \"{fixed_}\"");
            return fixed_;
        }

        private static string NormalizePunctForStability(string text)
        {
            string s = text.Replace('・', '…').Replace('．', '…').Replace('＂', '…').Replace('"', '…')
                .Replace('！', '…').Replace('!', '…').Replace('？', '…').Replace('?', '…');
                        s = s.Replace("「", "").Replace("」", "").Replace("〈", "").Replace("〉", "").Replace("『", "").Replace("』", "").Replace("【", "").Replace("】", "");
            return Regex.Replace(s, @"[…．\.]{2,}", "…");
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
                        if (iteration % 20 == 0)
                            FileLogger.Log($"[loop {iteration}] emulator not focused (fg=0x{fgFocus:X}), holding");
                        await Task.Delay(IdleDelayMs, token);
                        continue;
                    }
                    using var bitmap = WindowCapture.CaptureWindowRegion(_hWnd, _gameRegion);
                    _consecutiveCaptureFailures = 0;

                    string frameHash = ComputeFrameHash(bitmap);
                    if (frameHash == _lastFrameHash)
                    {
                        if (iteration % 20 == 0)
                            FileLogger.Log($"[loop {iteration}] frame unchanged, skipping OCR");
                        // Static dwell confirms a clean held candidate. Menu screens
                        // (sig set) and titles stay out; dupes stay out via guards.
                        if (!string.IsNullOrEmpty(_candidateText)
                            && _candidateStableCount >= 1
                            && _candidateStableCount < RequiredStableReadings
                            && string.IsNullOrEmpty(_menuSignature)
                            && !_frameIsTitle
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
                    _lastOcrText = rawText.Trim();
                    _lastOcrContent = contentWords;
                    _lastOcrTotal = totalWords;
                    string rawTrim = rawText.Trim();
                    string? remembered = null;
                    try { remembered = LookupMined(rawTrim); } catch { }
                    string extractedText = remembered ?? NormalizePunctForStability(ApplyZeldaOcrFixups(rawTrim));
                    try { MineCorrection(rawTrim, extractedText); } catch { }
                    // Phase 1a probe only: shape signal for menus/titles, dialogue path below unchanged.
                    try
                    {
                        int jaBlocks = 0;
                        foreach (var b in ocrBlocks)
                            if (HasJapanese(b.Text)) jaBlocks++;
                        var preview = new System.Text.StringBuilder();
                        int shown = 0;
                        foreach (var b in ocrBlocks)
                        {
                            if (shown >= 12) break;
                            string t = b.Text.Replace("\n", " ");
                            if (t.Length > 28) t = t.Substring(0, 28) + "…";
                            preview.Append($" [b{shown} h={b.Height} ja={HasJapanese(b.Text)} \"{t}\"]");
                            shown++;
                        }
                        FileLogger.Log($"[blocks] total={ocrBlocks.Count} jaBlocks={jaBlocks} primaryJa={HasJapanese(extractedText)}{preview}");
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
                        await Task.Delay(IdleDelayMs, token);
                        continue;
                    }
                    // Phase 1b: menu/title tracker. Dialogue path below frozen.
                    try { UpdateMenuTracker(ocrBlocks); } catch (Exception mex) { FileLogger.Log($"[menu] tracker error: {mex.Message}"); }
                    token.ThrowIfCancellationRequested();
                    FileLogger.Log($"[loop {iteration}] OCR result: \"{extractedText}\"");

                    if (string.IsNullOrWhiteSpace(extractedText))
                    {
                        if (_candidateStableCount >= RequiredStableReadings && !string.IsNullOrEmpty(_candidateText)
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
                            _candidateText = extractedText;
                            _candidateGrewThisRead = false;
                            _successorText = string.Empty;
                            _successorCount = 0;
                        }
                        else if (extractedText.Length > _candidateText.Length)
                        {
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
                        // Scroll-flush: the held line just scrolled (its successor
                        // starts with its tail). Publish the full held text now --
                        // it will never stabilize again.
                        if (_candidateStableCount >= 2
                            && !string.IsNullOrEmpty(_candidateText)
                            && StartsWithSuffixOfStrip(extractedText, _candidateText, 12)
                            && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                            && !IsEllipsisOnly(_candidateText) && !IsJunkOcr(_candidateText)
                            && !IsScrollBlend(_candidateText) && !IsZeldaTransitionBlend(_candidateText)
                            && !EndsWithContinuation(_candidateText) && !IsIncompleteSacrifice(_candidateText)
                            && HasJapanese(_candidateText))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=flush-on-scroll (publishing scrolled line)");
                            _lastTranslatedText = _candidateText;
                            string flushText = _candidateText;
                            _ = TranslateAndPublishAsync(flushText);
                            _candidateText = extractedText;
                            _candidateStableCount = 1;
                            _candidateGrewThisRead = false;
                        }
                        // Single garbled scroll-out must not wipe a held line.
                        else if (_candidateStableCount >= RequiredStableReadings
                            && (IsScrollBlend(extractedText) || IsJunkOcr(extractedText) || extractedText.Length < 6))
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=hold-new-guard (keeping held line)");
                        }
                        else
                        {
                            // Genuinely new line: track the LATEST read fresh.
                            _candidateText = extractedText;
                            _candidateStableCount = 1;
                            _candidateGrewThisRead = false;
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
                        double similarity = ComputeSimilarity(_candidateText, _lastTranslatedText);
                        bool isNearDuplicate = similarity >= NearDuplicateSimilarityThreshold;

                        bool isGrowth = !string.IsNullOrEmpty(_lastTranslatedText)
                            && _candidateText.Length > _lastTranslatedText.Length
                            && (_candidateText.StartsWith(_lastTranslatedText, StringComparison.Ordinal)
                                || _candidateText.Contains(_lastTranslatedText, StringComparison.Ordinal));

                        bool isOverlapContinuation = !isNearDuplicate && _candidateText != _lastTranslatedText
                            && StartsWithSuffixOfStrip(_candidateText, _lastTranslatedText, 12);

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
                            else if (_frameIsTitle)
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=title-guard (title-screen misread, holding)");
                            }
                            else
                            {
                                FileLogger.Log($"[loop {iteration}] STABLE-DECISION=publish-new (similarity to last shown: {similarity:F2})");
                                _lastTranslatedText = _candidateText;

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
                FileLogger.Log($"[translate-fire] \"{Truncate(text)}\"");
                string translated = await _translationManager.TranslateAsync(text);
                FileLogger.Log($"[translate-provider] {_translationManager.ActiveProviderName} for \"{Truncate(text)}\"");
                if (IsUnpublishable(translated))
                {
                    FileLogger.Log($"[translate] suppressed failure output for \"{text}\" -> \"{Truncate(translated)}\"");
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
                FileLogger.Log($"[translate-fire] \"{Truncate(text)}\" (update)");
                string translated = await _translationManager.TranslateAsync(text);
                if (IsUnpublishable(translated))
                {
                    FileLogger.Log($"[translate] suppressed failure output for \"{text}\"");
                    return;
                }
                FileLogger.Log($"[translate-update] \"{text}\" -> \"{translated}\"");
                TextUpdated?.Invoke(expectedOldJa, text, translated);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[translate] ERROR translating \"{text}\": {ex}");
            }
        }

        private static string Truncate(string s, int max = 80)
            => s.Length <= max ? s : s.Substring(0, max) + "...";

        private static readonly Regex SingleCharRun = new(@"(?:\b\w\b\s*){8,}", RegexOptions.Compiled);
        private static readonly Regex AlphaRun = new(@"([A-Za-z0-9])\1{6,}", RegexOptions.Compiled);
        private static readonly Regex FullWidthLatin = new(@"[Ａ-Ｚａ-ｚ]", RegexOptions.Compiled);
        private static readonly Regex PromptEcho = new(@"(Translate|Transform) the following Japanese text into English|ready to translate|provide the Japanese (passage|text)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JapaneseChars = new(@"[぀-ヿ一-鿿]", RegexOptions.Compiled);

        private static bool IsJunkOcr(string text)
        {
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
            if (!hasHiragana && (fullWidthLatin >= 3 || (content > 0 && (double)fullWidthLatin / content > 0.3)))
                return true;
            return false;
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
            int lastPeriod = text.LastIndexOf('。');
            if (lastPeriod < 0)
                return false;
            return text.Substring(0, lastPeriod).Trim().Length >= 6
                && text.Substring(lastPeriod + 1).Trim().Length >= 6;
        }

        private static bool IsZeldaTransitionBlend(string text)
        {
            return text.Contains("みつかり", StringComparison.Ordinal) && text.Contains("多くの", StringComparison.Ordinal);
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

        // Generic menu/title: small JA blocks, art filtered. No per-game strings.
        private void UpdateMenuTracker(List<OcrBlock> blocks)
        {
            _frameIsTitle = blocks.Count <= 3 && blocks.Any(b => b.Text.Contains("トライフォ"));
            if (IsInputGrid(blocks)) { _menuStableCount = 0; _menuSignature = ""; return; }
            // Group same-line blocks by Y overlap, top-to-bottom lines.
            var lines = new List<(int y, List<OcrBlock> parts)>();
            foreach (var b in blocks.OrderBy(b => b.Y))
            {
                if (b.Height > 150) continue;
                if (b.Width < 10 || b.Height < 12) continue;
                string t = b.Text.Trim();
                if (t.Length < 1) continue;
                bool placed = false;
                foreach (var ln in lines)
                {
                    int ly = ln.y;
                    if (Math.Abs(b.Y - ly) < Math.Min(b.Height, 60) / 2 + 8)
                    {
                        ln.parts.Add(b);
                        placed = true;
                        break;
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
                // with a name block present = filled but unreadable -> "----".
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
                if (ascii.Contains("PLAYER") && ascii.Contains("SELECT"))
                    joined = "< PLAYER SELECT >";
                if (joined == "◆ＰＬＡＹＥＲ" || joined == "ＳＥＬＥＣＴＡ" || joined == "ＳＥＬＥＣＴ＞") { /* merged below */ }
                if (joined.Contains("神々のトライフォース") && !joined.Contains("ゼルダ"))
                {
                    // Title pack filled at bubble level below; keep row as-is here.
                }
                if (string.IsNullOrWhiteSpace(joined)) continue;
                if (IsEllipsisOnly(joined)) continue;
                bool ja = HasJapanese(joined);
                string asciiJa = ToHalfwidth(joined);
                bool isNumberCandidate = joined == "ま、" || joined == "は、" || joined == "ほ、" || joined == "ぱ、" || joined == "…" || joined == "...";
                if (ja && IsJunkOcr(joined) && !isNumberCandidate && !asciiJa.Contains("KILL", StringComparison.OrdinalIgnoreCase)) continue;
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
                if (jaRows[i].t.Contains("トライフォ") && !jaRows[i].t.Contains("ゼルダ"))
                    jaRows[i] = (jaRows[i].y, "ゼルダの伝説 神々のトライフォース", true);
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
            bool isTitleFrame = (blocks.Count <= 3 && jaRows.Count(a => a.ja && !string.IsNullOrWhiteSpace(a.t)) <= 2 && jaRows.Any(a => a.t.Contains("トライフォ")));
            // Fragments on dialogue screens are not menus: few rows, little JA,
            // little total content. (Title + kanji-dense single headers exempt.)
            if (!isTitleFrame)
            {
                var contentRows = jaRows.Where(a => !string.IsNullOrWhiteSpace(a.t)).ToList();
                bool headerOnly = contentRows.Count == 1 && contentRows[0].t.Count(c => c >= '一' && c <= '鿿') >= 2;
                int totalChars = 0;
                foreach (var r in contentRows) totalChars += r.t.Length;
                if (!headerOnly && (contentRows.Count < 2 || contentRows.Count(a => a.ja) < 2 || totalChars < 12)) { _menuStableCount = 0; _menuSignature = ""; return; }
            }
            string sig = string.Join("\n", jaRows.ConvertAll(o => o.t));
            // Dialogue owns shared rows: menu duplicating just-shown text stays held.
            if (!string.IsNullOrEmpty(_lastTranslatedText))
            {
                foreach (var r in jaRows)
                {
                    if (!string.IsNullOrWhiteSpace(r.t) && r.t.Length >= 10 && _lastTranslatedText.Contains(r.t, StringComparison.Ordinal))
                    { _menuStableCount = 0; _menuSignature = ""; return; }
                }
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
                    _ = TranslateAndUpdateMenuAsync(prev, jaRows);
                    return;
                }
                // Same kind, high row overlap = OCR jitter, not a new screen.
                // Update in place so stable save-select never duplicates.
                if (overlap >= 0.50)
                {
                    string prev = _lastPublishedMenu;
                    _lastPublishedMenu = sig;
                    _ = TranslateAndUpdateMenuAsync(prev, jaRows);
                    return;
                }
            }
            if (sig == _lastPublishedMenu) return;
            _lastPublishedMenu = sig;
                        bool isTitle = (blocks.Count <= 3 && jaRows.Count(a => a.ja && !string.IsNullOrWhiteSpace(a.t)) <= 2 && jaRows.Any(a => a.t.Contains("トライフォ")));
            string kind = isTitle ? "TITLE" : "MENU";
            FileLogger.Log($"[menu] publish {kind} lines={jaRows.Count}");
            _ = TranslateAndPublishMenuAsync(kind, jaRows);
        }

        private async Task TranslateAndPublishMenuAsync(string kind, List<(int y, string t, bool ja)> rows)
        {
            try
            {
                var en = new List<string>();
                foreach (var r in rows)
                {
                    if (string.IsNullOrWhiteSpace(r.t)) { en.Add(""); continue; }
                    if (!r.ja || IsSlotRow(r.t)) { en.Add(r.t); continue; }
                    string e = await _translationManager.TranslateAsync(r.t);
                    if (IsUnpublishable(e)) e = "(untranslatable)";
                    en.Add(e);
                }
                FileLogger.Log($"[menu] {kind} -> {en.Count} lines");
                MenuTranslated?.Invoke(kind, string.Join("\n", rows.ConvertAll(r => r.t)), string.Join("\n", en));
            }
            catch (Exception ex) { FileLogger.Log($"[menu] ERROR: {ex.Message}"); }
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
                    string e = await _translationManager.TranslateAsync(r.t);
                    if (IsUnpublishable(e)) e = "(untranslatable)";
                    en.Add(e);
                }
                string newJa = string.Join("\n", clean.ConvertAll(r => r.t));
                string newEn = string.Join("\n", en);
                FileLogger.Log($"[menu] update -> {en.Count} lines");
                if (_stopped) return;
                // Kind derived from rows: TITLE only for Triforce single, else MENU.
                string kind2 = newJa.Contains("トライフォ") && newJa.Split('\n').Length <= 2 ? "TITLE" : "MENU";
                MenuUpdated?.Invoke(oldSig, kind2, newJa, newEn);
            }
            catch (Exception ex) { FileLogger.Log($"[menu] ERROR: {ex.Message}"); }
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