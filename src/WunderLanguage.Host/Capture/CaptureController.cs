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
        // Perf batch: per-loop cost accounting (distributions, not means).
        // Loop-thread rings need no lock; per-fire publish samples share
        // _costLock (push-only, contention ~nil). pub samples the fast leg
        // only (fire-to-first-paint, the user-facing number; silent refresh
        // legs excluded). One flush line per 60s — no per-loop logging
        // (observer effect). Revert: delete this block plus the Cost* sites.
        private const int CostRing = 64;
        private readonly double[] _costCap = new double[CostRing];
        private readonly double[] _costOcr = new double[CostRing];
        private readonly double[] _costDet = new double[CostRing];
        private readonly double[] _costRec = new double[CostRing];
        private readonly double[] _costPub = new double[CostRing];
        private int _costCapIx, _costCapN, _costOcrIx, _costOcrN, _costDetIx, _costDetN, _costRecIx, _costRecN, _costPubIx, _costPubN;
        private long _costLoops, _costOcrLoops, _costSkipped, _costHolds;
        // Session input fingerprint (ML book: shift detection without
        // labels — per-session totals enabling cross-session/game
        // comparison at onboarding). Accumulated alongside the flush
        // counters, reported once in Stop(). Revert with the Stop block.
        private long _sessLoops, _sessOcr, _sessSkipped, _sessHolds, _sessBlocks, _sessJaFrames;
        private double _sessDetSum, _sessRecSum;
        private long _costPrefHit, _costPrefMiss, _costStickyHit, _costStickyMiss, _costSimHit, _costSimMiss;
        // PNG-encode mean (stable cost — mean suffices, no ring). Loop
        // thread only; reset per flush with the counters.
        private double _costPngSum;
        private long _costPngN;
        private DateTime _costFlushAt = DateTime.UtcNow;
        private readonly object _costLock = new();
        private static void CostPush(double[] ring, ref int ix, ref int n, double v)
        {
            ring[ix] = v; ix = (ix + 1) % CostRing; if (n < CostRing) n++;
        }
        private void CostPubSample(double ms)
        {
            lock (_costLock) CostPush(_costPub, ref _costPubIx, ref _costPubN, ms);
        }
        private static double CostPct(double[] ring, int n, double p)
        {
            if (n <= 0) return 0;
            var s = new double[n]; Array.Copy(ring, s, n); Array.Sort(s);
            return s[Math.Min(n - 1, (int)(p * n))];
        }
        private void MaybeFlushLoopCost()
        {
            try
            {
                if ((DateTime.UtcNow - _costFlushAt).TotalSeconds < 60) return;
                _costFlushAt = DateTime.UtcNow;
                double[] pub; int pn;
                lock (_costLock) { pub = (double[])_costPub.Clone(); pn = _costPubN; }
                FileLogger.Log($"[loop-cost] game={_gameId} loops={_costLoops} ocr={_costOcrLoops} skipped={_costSkipped} holds={_costHolds} " +
                    $"capN={_costCapN} p50/p99={CostPct(_costCap, _costCapN, 0.5):F0}/{CostPct(_costCap, _costCapN, 0.99):F0}ms " +
                    $"ocrN={_costOcrN} p50/p99={CostPct(_costOcr, _costOcrN, 0.5):F0}/{CostPct(_costOcr, _costOcrN, 0.99):F0}ms " +
                    $"det p50/p99={CostPct(_costDet, _costDetN, 0.5):F0}/{CostPct(_costDet, _costDetN, 0.99):F0}ms " +
                    $"rec p50/p99={CostPct(_costRec, _costRecN, 0.5):F0}/{CostPct(_costRec, _costRecN, 0.99):F0}ms " +
                    $"pubN={pn} p50/p99={CostPct(pub, pn, 0.5):F0}/{CostPct(pub, pn, 0.99):F0}ms " +
                    $"pngMean={(_costPngN == 0 ? 0 : _costPngSum / _costPngN):F1}ms over {_costPngN}");
                _costLoops = 0; _costOcrLoops = 0; _costSkipped = 0; _costHolds = 0;
                _costPngSum = 0; _costPngN = 0;
                // Cache-hit visibility, same cadence (interval rates, not
                // cumulative): tcache = persistent translation cache,
                // sticky = last-published row reuse, prefetch = stability-wait
                // memo, sim = flicker-tolerant name memo.
                var (tcReq, tcHit) = _translationManager.CacheStats();
                long ph = Interlocked.Exchange(ref _costPrefHit, 0);
                long pm = Interlocked.Exchange(ref _costPrefMiss, 0);
                long sh = Interlocked.Exchange(ref _costStickyHit, 0);
                long sm = Interlocked.Exchange(ref _costStickyMiss, 0);
                long ih = Interlocked.Exchange(ref _costSimHit, 0);
                long im = Interlocked.Exchange(ref _costSimMiss, 0);
                static string Rate(long h, long m) => (h + m) == 0 ? "-/-" : $"{h}/{h + m} ({100.0 * h / (h + m):F0}%)";
                FileLogger.Log($"[cache-hit] game={_gameId} tcache={Rate(tcHit, tcReq - tcHit)} sticky={Rate(sh, sm)} prefetch={Rate(ph, pm)} sim={Rate(ih, im)}");
            }
            catch { }
        }
        private string _lastOcrText = string.Empty;
        private int _lastOcrContent;
        private int _lastOcrTotal;
        private bool _focusHoldActive;

        public event Action<string, string>? TextTranslated;
        public event Action<string, string, string>? MenuTranslated; // (kind TITLE/MENU, jaJoined, enJoined)
        public event Action<string, string, List<string>, List<string>, List<string>, List<string>, List<string>, List<string>, List<string>, List<string>, List<string>, List<string>>? BattleFramePublished; // (topJa, topEn, monJa, monEn, actJa, actEn, rightJa, rightEn, itemJa, itemEn, weaponJa, weaponEn)
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
        // HDIP eval rig: last loop text + settle-crop bookkeeping.
        private string _settleLastText = "";
        private int _settleLastSavedLoop;
        private int _settleSaved;
        private string _settleSavedText = "";
        private int _settleTextCount;
        // Zelda logo hold release: set once TITLE publishes this session.
        private bool _titlePosted;
        // Last seen monster rows within the current fight. The action menu covers
        // the monster box when open; stickiness keeps the frame steady instead of
        // blanking every time the player picks a command.
        private List<string> _battleMonsterSticky = new();
        // Last seen item pair rows ("left\tRight") within the current open
        // box. Grey-row OCR flicker (dots↔empty per loop) would otherwise
        // collapse and re-post positions every few loops; memory holds slots
        // steady. Cleared with everything else when the box closes.
        private List<string> _battleItemSticky = new();
        // Item-box-open latch: set when an item fire carries content, cleared
        // when the box demonstrably closes (action/monster rows visible, full
        // battle wipe). While set, the bottom band belongs to the item box —
        // scrolled short rows (bottom-of-list views too sparse for tall
        // columns) assemble as item rows instead of scattering to
        // monster/right slots. Cited 2026-10-01: bottom 〈ダウン〉 soup fired
        // as a monster while めぐすり leaked to party.
        private bool _itemBoxOpen;
        // Carry slot: cls==0 rescue promotion computes synthetic columns
        // before the early-return below; the main path consumes them after.
        // Always cleared on consume so one frame can never poison the next.
        private List<string>? _rescueCarry;
        // Item settle gate (cited 2026-10-01, user-endorsed "works it out
        // and sticks to it"): item-carrying fires post only on two
        // consecutive identical assemblies, so grey flicker (clean↔garble
        // alternation) holds last-stable instead of flipping the panel.
        // Cost: ~1 loop of latency on real changes; monster/action-only
        // frames keep instant fire. Separate prev-sig per fire site so
        // blank-box and content assemblies don't cross-hold each other.
        private string _battleItemFirePrev = "";
        private string _battleBlankFirePrev = "";
        // Hysteresis counters (cited 2026-10-01: grey flicker dwells 2+
        // loops per state, so plain 2-repeat still flips the panel —
        // consistency needs content-aware patience).
        private int _battleItemFireRuns;
        private int _battleItemFiredContent;
        // Weapon settle (cited 2026-10-03 log: cls=4 weapon=2 extracted for
        // 4+ loops but never published — the item peeking strip flickers
        // every loop, resetting the whole-sig item runs counter, so the
        // combined gate never settles). Weapon rows get their own
        // stability counter on the weapon sub-signature; a stable NEW
        // weapon layer publishes even while the item tail flickers.
        // Item-only changes still wait on the item gate above.
        private string _battleWeaponFirePrev = "";
        private int _battleWeaponFireRuns;
        private string _battleWeaponFired = "";
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
        private List<string> _lastBattleItemJa = new();
        private List<string> _lastBattleItemEn = new();
        private List<string> _lastBattleWeaponJa = new();
        private List<string> _lastBattleWeaponEn = new();
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
        // Frame-hash novelty history + current-frame flag (see loop).
        private readonly System.Collections.Generic.Queue<string> _recentFrameHashes = new();
        private bool _lastFrameNovel = true;
        private int _pendingFrameStableCount;
        private const int RequiredPreOcrStablePolls = 1;
        // A1 averaging trial (HDIP Ch 25, pilot 2026-10-07: 1 real fix, 0
        // regressions on 4 pairs): mean of the current + previous captured
        // frame, single OCR pass on the mean. No extra waits, no extra OCR
        // calls — the pass it replaces. Gate: diff frac < 0.002 (static),
        // so drifted frames never average (pilot: 1px drift breaks the
        // mean). Logged as [avg]. Revert: delete these two fields + the
        // update block + the OCR-site block + AverageBitmaps.
        private Bitmap? _prevFullFrame;
        private double _lastDiffFrac = 1.0;
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
                    CorpusRotate();
                    // New game, new timeline: stale quiet must not mint a TITLE.
                    _quietCount = 0; _quietSeen = "";
                    return;
                }
                if (fresh == _gameId) return;
                FileLogger.Log($"[pack] game switch {_gameId} -> {fresh}");
                _gameId = fresh;
                try { Packs.PackRegistry.SetSession(_gameId); } catch { }
                try { LoadMinedCorrections(); } catch { }
                CorpusRotate();
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
                    if (Settings.Diag.Enabled) FileLogger.Log($"[mine] \"{raw}\" -> \"{fixed_}\"");
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
            {
                if (Settings.Diag.Enabled) FileLogger.Log($"[ocr-fixup] \"{text}\" -> \"{fixed_}\"");
            }
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
            // Sentence-double collapse (cited 2026-10-07 Zelda live: 4/54
            // fires carried an internal double from overlapping box reads —
            // "X。 X。" posted twice; seen-but-never-fired candidates show
            // the same shape blocking stability). Pack-blind, replay-covered
            // (this function). Revert: delete the call + the helper.
            s = CollapseRepeatedSentences(s);
            return s;
        }
        private static string CollapseRepeatedSentences(string s)
        {
            // Split into (body, terminator); compare bodies punctuation-
            // blind (… vs ！ variants are the same sentence re-read).
            // Collapse adjacent identicals, then an exact repeated half
            // (XYXY → XY). Bodies < 2 chars never collapse (choice menus).
            // Keeps the LAST occurrence (later reads are the more complete
            // ones: … → ！ as the line finishes typing).
            var parts = Regex.Split(s, @"([。！？…]+)");
            var bodies = new System.Collections.Generic.List<string>();
            var terms = new System.Collections.Generic.List<string>();
            for (int i = 0; i < parts.Length; i += 2)
            {
                string body = parts[i].Trim(' ', '　', '、', '，', ',', '・', '\t', '\n', '\r');
                string term = (i + 1 < parts.Length) ? parts[i + 1] : "";
                if (body.Length > 0) { bodies.Add(body); terms.Add(term); }
            }
            if (bodies.Count < 2) return s;
            var keptB = new System.Collections.Generic.List<string>();
            var keptT = new System.Collections.Generic.List<string>();
            bool cut = false;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (keptB.Count > 0 && bodies[i].Length >= 2 && bodies[i] == keptB[keptB.Count - 1])
                {
                    keptT[keptT.Count - 1] = terms[i];
                    cut = true;
                    continue;
                }
                keptB.Add(bodies[i]);
                keptT.Add(terms[i]);
            }
            if (keptB.Count >= 2 && keptB.Count % 2 == 0)
            {
                int h = keptB.Count / 2;
                bool halves = true;
                for (int i = 0; i < h; i++)
                    if (keptB[i].Length < 2 || keptB[i] != keptB[h + i]) { halves = false; break; }
                if (halves)
                {
                    for (int i = 0; i < h; i++) keptT[i] = terms[h + i];
                    keptB.RemoveRange(h, h);
                    keptT.RemoveRange(h, h);
                    cut = true;
                }
            }
            if (!cut) return s;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < keptB.Count; i++) { sb.Append(keptB[i]); sb.Append(keptT[i]); }
            try { FileLogger.Log($"[sentdedup] \"{s}\" -> \"{sb}\""); } catch { }
            return sb.ToString();
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
            // Build tag: bump the tail token EVERY behavior batch so a log
            // instantly convicts which binary produced it (cited 2026-10-01:
            // a stale binary's panel was debugged as current — wasted a full
            // round). Tokens are chronological batch markers.
            try { FileLogger.Log($"[build] battle25 split+guard+grace+sticky+titleguard+overlap+rows+fanout+prefetch+romaji+rewardclean+hintgate+1read+adopt+digitsplit+fastrefresh+namesonly+topgate+gapblank+onerow+simmemo+siglog+fixedframe+fireorder+actionslot+wideact+splitmon+autofit+countattach+fitlog+itembox+itemcols+itemvocab+itemcover+mockuplayout+weaponbox+wlabels+reltail+behindstay+glossary+trueoverlay+2block+remnant+scrollflicker+bottomrows+gapbar10+dusttoken+stretch+ghostdrop+flickfix+soupbar9+memocap+blankkeep+chartveto+doubleF+voicefix+anchor+latch+blanksfire+canonblank+settle2+newitems+dustcontent+anchorcanon+latchfreeze+blankfire+canonmemokey+midorder+shortrescue+nocatfb+holdzip+hysteresis+pairdedup+tailanchor+novelty+2rows+openbypass+padappend+rescue0+rewardsexp+wnew+wtop+wicon+wtail+sentdedup+titlegeo active (pack={Packs.PackRegistry.Current.GameId})"); } catch { }
            try { Packs.PackRegistry.SetSession(_gameId); } catch { }
            try { LoadMinedCorrections(); } catch { }
            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_cts.Token);
            // Survival batch 4: block idle-sleep for the live session only
            // (pause/Stop releases). Revert: delete this line.
            try { Windows.PowerAvailability.Hold(); } catch { }
        }

        public void Stop()
        {
            FileLogger.Log($"CaptureController.Stop called id={_id}");
            _stopped = true;
            // Session input fingerprint (one line per session — the
            // cross-session/game comparison input for shift detection).
            try
            {
                double jaRate = _sessOcr == 0 ? 0 : 100.0 * _sessJaFrames / _sessOcr;
                double bpf = _sessOcr == 0 ? 0 : (double)_sessBlocks / _sessOcr;
                double detM = _sessOcr == 0 ? 0 : _sessDetSum / _sessOcr;
                double recM = _sessOcr == 0 ? 0 : _sessRecSum / _sessOcr;
                FileLogger.Log($"[session] loops={_sessLoops} ocr={_sessOcr} skipped={_sessSkipped} holds={_sessHolds} blocks={_sessBlocks} jaframes={_sessJaFrames} jarate={jaRate:F0}% boxesperframe={bpf:F1} detmean={detM:F0}ms recmean={recM:F0}ms");
            }
            catch { }
            try { CorpusRotate(); } catch { }
            _cts?.Cancel();
            // Survival batch 4: session over, yield sleep back. Revert:
            // delete this line.
            try { Windows.PowerAvailability.Release(); } catch { }
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            FileLogger.Log("CaptureController loop starting (cc-converge-overlap)");
            int iteration = 0;

            while (!token.IsCancellationRequested)
            {
                iteration++;
                _costLoops++;
                _sessLoops++;
                MaybeFlushLoopCost();
                try
                {
                    // Focus gate: minimized or behind another window -> hold.
                    // No OCR, no publish, until the emulator is foreground.
                    if (WindowCapture.IsMinimized(_hWnd))
                    {
                        _costHolds++;
                        _sessHolds++;
                        if (iteration % 20 == 0)
                            FileLogger.Log($"[loop {iteration}] emulator minimized, holding");
                        await Task.Delay(IdleDelayMs, token);
                        continue;
                    }
                    IntPtr fgFocus = WindowCapture.GetForegroundWindowHandle();
                    if (fgFocus != _hWnd && !(_isCompanionWindow?.Invoke(fgFocus) ?? false))
                    {
                        _focusHoldActive = true;
                        _costHolds++;
                        _sessHolds++;
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
                        _recentFrameHashes.Clear();
                        _lastFrameNovel = true;
                        _prevSmall = null;
                        FileLogger.Log($"[loop {iteration}] refocus-catchup (stale frame dropped, OCR now)");
                    }
                    if (iteration % 20 == 0) RefreshGameSession();
                    var capSw = System.Diagnostics.Stopwatch.StartNew();
                    using var bitmap = WindowCapture.CaptureWindowRegion(_hWnd, _gameRegion);
                    _consecutiveCaptureFailures = 0;

                    string frameHash = ComputeFrameHash(bitmap);
                    capSw.Stop();
                    CostPush(_costCap, ref _costCapIx, ref _costCapN, capSw.Elapsed.TotalMilliseconds);
                    // Vision item 4 (temporal derivative v0): downscaled
                    // diff energy + change band, logged on settled frames.
                    // Classify-only — the settle machinery below is untouched;
                    // acting on the signal (blink-hold, scroll-fastpath) waits
                    // for cited distributions. Revert: delete fields/method +
                    // the two call sites.
                    string frameDiff = ComputeFrameDiff(bitmap);
                    if (frameHash == _lastFrameHash)
                    {
                        _costSkipped++;
                        _sessSkipped++;
                        if (iteration % 20 == 0)
                            FileLogger.Log($"[loop {iteration}] frame unchanged, skipping OCR");
                        string? quietTitleStatic = CheckQuietTitle(iteration, null);
                        // Same battle guard as the settled path: static cooldown screens
                        // must never mint a TITLE mid-fight (forces a dupe frame).
                        if (quietTitleStatic != null && string.IsNullOrEmpty(_battleFiredSig) && _battleCooldown <= 0) { _titlePosted = true; _ = TranslateAndPublishMenuAsync("TITLE", new List<(int y, string t, bool ja)> { (0, "", false) }); }
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
                        try { _prevFullFrame?.Dispose(); _prevFullFrame = null; _prevFullFrame = (Bitmap)bitmap.Clone(); } catch { _prevFullFrame = null; }
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
                    // Frame-hash novelty history (cited 2026-10-01: cursor
                    // blink cycles pixels A,B,A,B with identical content —
                    // each novel-vs-last frame re-OCRs and grey rows re-read
                    // differently, flip-flopping the panel with zero game
                    // motion). Repeats within recent history mark oscillation,
                    // not change. UpdateBattleFrame holds content on those.
                    _lastFrameNovel = !_recentFrameHashes.Contains(frameHash);
                    _recentFrameHashes.Enqueue(frameHash);
                    while (_recentFrameHashes.Count > 8) _recentFrameHashes.Dequeue();
                    FileLogger.Log($"[loop {iteration}] frame settled - running OCR (bitmap {bitmap.Width}x{bitmap.Height} {frameDiff})");

                    var ocrSw = System.Diagnostics.Stopwatch.StartNew();
                    // Learning mode (empty item vocab) unions the classical
                    // box proposer; vocab games keep the tuned detector-only
                    // path (vision item 2 — byte-identical for FFVI).
                    bool learningMode = (Packs.PackRegistry.Current.ItemVocabulary?.Count ?? 0) == 0;
                    // A1 trial: mean of previous + current capture, single OCR
                    // pass on the mean. Previous frame is last loop's capture;
                    // the static gate (frac<0.002) rejects drifted pairs, so
                    // the mean is aligned by construction. Miss = plain frame.
                    Bitmap ocrBitmap = bitmap;
                    Bitmap? avgBitmap = null;
                    try
                    {
                        if (_prevFullFrame is not null && _prevFullFrame.Size == bitmap.Size && _lastDiffFrac < 0.002)
                        {
                            avgBitmap = AverageBitmaps(_prevFullFrame, bitmap);
                            if (avgBitmap is not null)
                            {
                                ocrBitmap = avgBitmap;
                                FileLogger.Log($"[loop {iteration}] [avg] mean-of-2 ({frameDiff})");
                            }
                        }
                    }
                    catch { avgBitmap = null; ocrBitmap = bitmap; }
                    var (rawText, ocrBlocks, contentWords, totalWords) = await _ocrClient.ExtractTextWithBlocksAsync(ocrBitmap, learningMode, Packs.PackRegistry.Current.BoxScoreThreshold);
                    try { avgBitmap?.Dispose(); } catch { }
                    // Stash this capture for next loop's mean (all paths).
                    try
                    {
                        _prevFullFrame?.Dispose();
                        _prevFullFrame = null;
                        _prevFullFrame = (Bitmap)bitmap.Clone();
                    }
                    catch { _prevFullFrame = null; }
                    ocrSw.Stop();
                    CostPush(_costOcr, ref _costOcrIx, ref _costOcrN, ocrSw.Elapsed.TotalMilliseconds);
                    CostPush(_costDet, ref _costDetIx, ref _costDetN, _ocrClient.LastDetMs);
                    CostPush(_costRec, ref _costRecIx, ref _costRecN, _ocrClient.LastRecMs);
                    _costPngSum += _ocrClient.LastPngMs;
                    _costPngN++;
                    _costOcrLoops++;
                    _sessOcr++;
                    _sessDetSum += _ocrClient.LastDetMs;
                    _sessRecSum += _ocrClient.LastRecMs;
                    _sessBlocks += ocrBlocks.Count;
                    foreach (var _sb in ocrBlocks)
                        if (HasJapanese(_sb.Text)) { _sessJaFrames++; break; }
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
                            preview.Append($" [b{shown} x={b.X} y={b.Y} w={b.Width} h={b.Height} nx={nx:0.00} ny={ny:0.00} ja={HasJapanese(b.Text)} c={b.Conf:0.00} n={b.NMerge}{(b.Src == "classical" ? " cls" : "")} \"{t}\"]");
                            shown++;
                        }
                        if (Settings.Diag.Enabled) FileLogger.Log($"[blocks] total={ocrBlocks.Count} jaBlocks={jaBlocks} primaryJa={HasJapanese(extractedText)} frame={fw}x{fh}{preview}");
                        string? quietTitleSettled = CheckQuietTitle(iteration, ocrBlocks);
                        // A live battle owns the screen: the logo/title detector must
                        // never fire mid-fight (it forces a bogus TITLE plus a dupe
                        // frame). Battle-grace covers transitions too.
                        if (quietTitleSettled != null && string.IsNullOrEmpty(_battleFiredSig) && _battleCooldown <= 0) { _titlePosted = true; _ = TranslateAndPublishMenuAsync("TITLE", new List<(int y, string t, bool ja)> { (0, "", false) }); }
                    }
                    catch { }
                    // Battle-frame dialogue guard: bottom-band battle blocks never become
                    // dialogue; top-band rewards isolate and flow on. Wiring only — all
                    // geometry lives in BattleFrame.cs. Non-battle frames fall through.
                    try
                    {
                        // Zelda repair (cited 2026-10-07: Zelda dialogue boxes
                        // classify as battle cls=2, latch battle context, and
                        // suppress all dialogue; FFVI continuity machinery is
                        // pack-scoped — off-pack it only ever made garbage
                        // because Zelda/universal battle vocabs are empty).
                        // Non-FFVI packs clear latched battle state once and
                        // never suppress dialogue. FFVI path byte-identical.
                        // Revert: delete ffviBattle + the wipe below.
                        bool ffviBattle = Packs.PackRegistry.Current.GameId == Packs.PackRegistry.FfviGameId;
                        if (!ffviBattle && (_battleFiredSig != "" || _battleCooldown > 0 || _itemBoxOpen || _battleMonsterSticky.Count > 0 || _battleItemSticky.Count > 0))
                        {
                            _battlePrevSig = ""; _battleFiredSig = ""; _battleMonsterSticky = new List<string>(); _battleItemSticky = new List<string>(); _itemBoxOpen = false; _battleItemFirePrev = ""; _battleBlankFirePrev = ""; _battleItemFireRuns = 0; _battleItemFiredContent = 0; _battleWeaponFirePrev = ""; _battleWeaponFireRuns = 0; _battleWeaponFired = "";
                            FileLogger.Log("[battle] non-FFVI pack: latched battle state cleared");
                        }
                        string battleTop = "";
                        int battleClass = BattleFrame.Classify(ocrBlocks, _battleFrameW, _battleFrameH, HasJapanese, Packs.PackRegistry.Current.WeaponLabels, out battleTop, _itemBoxOpen);
                        if (ffviBattle && battleClass == 2)
                        {
                            // Rewards live in the battle frame now (UpdateBattleFrame
                            // carries the top bar); they no longer dialogue-fire.
                            FileLogger.Log($"[battle] rewards frame owns top-bar (dialogue suppressed)");
                            extractedText = string.Empty;
                        }
                        else if (ffviBattle && battleClass == 1)
                        {
                            if (!string.IsNullOrEmpty(extractedText))
                                FileLogger.Log($"[battle] bottom-frame suppressed from dialogue ({ocrBlocks.Count} blocks)");
                            extractedText = string.Empty;
                        }
                        else if (ffviBattle && battleClass == 3 && InBattleContext())
                        {
                            // Item box owns the bottom grid now; like rewards,
                            // it never dialogue-fires.
                            FileLogger.Log($"[battle] item box owns bottom (dialogue suppressed)");
                            extractedText = string.Empty;
                        }
                        else if (ffviBattle && battleClass == 4 && InBattleContext())
                        {
                            // Weapon box owns the bottom grid now; like the
                            // item box, it never dialogue-fires.
                            FileLogger.Log($"[battle] weapon box owns bottom (dialogue suppressed)");
                            extractedText = string.Empty;
                        }
                        else if (ffviBattle && battleClass == 0 && InBattleContext() && BattleFrame.IsBattleRemnant(ocrBlocks, _battleFrameW, _battleFrameH, HasJapanese))
                        {
                            // Scroll-remnant hold: fragmented item frames
                            // (1-col, short rows) are battle-owned but
                            // unclassifiable — they hold the panel, never
                            // dialogue-fire (2026-09-30 scroll hallucinations).
                            FileLogger.Log($"[battle] remnant holds bottom (dialogue suppressed)");
                            extractedText = string.Empty;
                        }
                    }
                    catch { }
                    // Zelda logo hold (cited 2026-10-07: the ゼルダの伝説 logo
                    // OCRs as fluent garbage — "ここでは１０００円…" fired as
                    // dialogue, and the fire itself reset the quiet-title
                    // streak so TITLE never published). Logo art reads as
                    // full-bleed short bands; real scroll/dialogue text is
                    // inset and tall (scroll x=116 w=388 h=134, dialogue
                    // x=117 w=331 h=126 vs logo x=-2 w=668 h=76 on 668-wide
                    // frames). Runs on Zelda AND universal: at boot the pack
                    // is still universal (pre-ROM-load title), which is
                    // exactly the logo window (cited 2026-10-07: Zelda-gated
                    // hold never engaged). Never on FFVI. Pre-title only;
                    // releases the moment frames stop matching, so nothing
                    // can deadlock. Revert: delete this block.
                    try
                    {
                        if (Packs.PackRegistry.Current.GameId != Packs.PackRegistry.FfviGameId && !_titlePosted && ocrBlocks.Count > 0)
                        {
                            bool allLogoBands = true;
                            foreach (var b in ocrBlocks)
                            {
                                if (!HasJapanese(b.Text)) continue;
                                if (!(b.X <= 6 && b.X + b.Width >= _battleFrameW - 6 && b.Height < 100)) { allLogoBands = false; break; }
                            }
                            if (allLogoBands && ocrBlocks.Exists(b => HasJapanese(b.Text)))
                            {
                                FileLogger.Log($"[zelda] logo-frame holds dialogue ({ocrBlocks.Count} blocks)");
                                extractedText = string.Empty;
                            }
                        }
                    }
                    catch { }
                    // HDIP batch 1 eval rig (Ch 25 settled-frame averaging):
                    // dev-only image evidence. Saves crops of the largest JA
                    // block on consecutive same-text loops (settled sequences
                    // for the offline averaging experiment). Diagnostics-gated,
                    // capped, observe-only — never on the fire path.
                    // Revert: delete this block + the 3 fields.
                    try
                    {
                        if (Settings.Diag.Enabled && !string.IsNullOrEmpty(extractedText) && extractedText == _settleLastText && iteration - _settleLastSavedLoop >= 5 && _settleSaved < 60 && _settleTextCount < 6)
                        {
                            OcrBlock? biggest = null;
                            foreach (var b in ocrBlocks)
                                if (HasJapanese(b.Text) && (biggest == null || b.Width * b.Height > biggest.Width * biggest.Height)) biggest = b;
                            if (biggest != null)
                            {
                                int cx = System.Math.Max(0, biggest.X), cy = System.Math.Max(0, biggest.Y);
                                int cw = System.Math.Min(biggest.Width, bitmap.Width - cx), ch = System.Math.Min(biggest.Height, bitmap.Height - cy);
                                if (cw > 8 && ch > 8)
                                {
                                    // Persist OUTSIDE troubleshoot/ (fresh bundle per
                                    // launch wipes it — cited 2026-10-07: a full
                                    // ring died that way before harvest).
                                    string sdir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "WunderLanguage", "settle-eval");
                                    System.IO.Directory.CreateDirectory(sdir);
                                    string fn = $"settle_{_settleSaved:00}_{iteration}.png";
                                    using (var crop = bitmap.Clone(new System.Drawing.Rectangle(cx, cy, cw, ch), bitmap.PixelFormat))
                                        crop.Save(System.IO.Path.Combine(sdir, fn), System.Drawing.Imaging.ImageFormat.Png);
                                    try { System.IO.File.AppendAllText(System.IO.Path.Combine(sdir, "index.txt"), $"{fn}|{extractedText.Replace("\n", " ")}{System.Environment.NewLine}"); } catch { }
                                    _settleLastSavedLoop = iteration; _settleSaved++;
                                    if (extractedText == _settleSavedText) _settleTextCount++; else { _settleSavedText = extractedText; _settleTextCount = 1; }
                                }
                            }
                        }
                        if (extractedText != _settleLastText) _settleLastText = extractedText ?? "";
                    }
                    catch { }
                    // Kana-chart gate (name entry etc.): merged-blob shape.
                    // Suppresses whole screen in both tiers: header, arrows,
                    // END and blobs cost nothing and post nothing.
                    // Battle veto (cited 2026-10-01: open item box reads
                    // 20+ kana with zero kanji per column, tripping this
                    // gate for 8s until scroll breaks the blob shape).
                    // Name entry never happens mid-battle; battle-shaped
                    // frames route to the battle handlers below instead.
                    if (IsKanaChartScreen(ocrBlocks)
                        && !BattleFrame.IsBattle(ocrBlocks, _battleFrameW, _battleFrameH, HasJapanese, Packs.PackRegistry.Current.WeaponLabels, _itemBoxOpen)
                        && !(_itemBoxOpen && BattleFrame.IsBattleRemnant(ocrBlocks, _battleFrameW, _battleFrameH, HasJapanese)))
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
                    // Zelda repair: the battle tracker (publishes BATTLE frames,
                    // owns _battleFiredSig/cooldown/stickies) runs on FFVI packs
                    // only — same gate as the suppression above. Revert: drop the if.
                    try { if (Packs.PackRegistry.Current.GameId == Packs.PackRegistry.FfviGameId) UpdateBattleFrame(ocrBlocks); } catch (Exception bex) { FileLogger.Log($"[battle] tracker error: {bex.Message}"); }
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
                        // Turned-page rescue (cited 2026-10-07, measured: 109 long
                        // unterminated Zelda texts never fired — both flush paths
                        // demand completeness, so a scroll page that turns
                        // mid-sentence dies silently). Same turn evidence as
                        // flush-on-scroll, but for long substantial pages that
                        // never terminated: publish or lose forever. Headless
                        // fragments excluded (M5 recall shape), shorts excluded
                        // (fragment-hold owns them), junk/blend/sacrifice as
                        // usual. Shared: FFVI item rows are short (<15 strip)
                        // and can't reach this branch; replay parity decides.
                        // Revert: delete this branch.
                        else if (_candidateStableCount >= 2
                            && !string.IsNullOrEmpty(_candidateText)
                            && StartsWithSuffixOfStrip(extractedText, _candidateText, 12)
                            && ComputeSimilarity(_candidateText, _lastTranslatedText) < NearDuplicateSimilarityThreshold
                            && !IsEllipsisOnly(_candidateText) && !IsJunkOcr(_candidateText)
                            && !IsScrollBlend(_candidateText) && !IsZeldaTransitionBlend(_candidateText)
                            && !IsIncompleteSacrifice(_candidateText) && !StartsHeadlessFragment(_candidateText)
                            && HasJapanese(_candidateText) && HasAnchorSubstance(_candidateText)
                            && StripForOverlap(_candidateText).Length >= 15)
                        {
                            FileLogger.Log($"[loop {iteration}] STABLE-DECISION=flush-on-turned-page (publishing unterminated scrolled page)");
                            try { CorpusRecordBubble("dialogue-decision:flush-on-turned-page", _candidateText, ""); } catch { }
                            _prevTranslatedText = _lastTranslatedText;
                            _lastTranslatedText = _candidateText;
                            _lastFireLoop = iteration;
                            string turnedText = _candidateText;
                            _ = TranslateAndPublishAsync(turnedText);
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
            var pubSw = System.Diagnostics.Stopwatch.StartNew();
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
                // Fragment hold (cited 2026-10-07, measured on 487 Zelda
                // fire-frames: junk median len 7, true-junk max 9; gate
                // len<=9 + unterminated catches 46/53 junk-frames at 9/434
                // real cost, all cost being typewriter prefixes the full line
                // later subsumes). Zelda-gated: FFVI item/menu rows are short
                // + unterminated by nature. Pack-vocab rows exempt (future
                // item names). Omission-only if ever wrong.
                // Revert: delete this block.
                if (Packs.PackRegistry.Current.GameId == Packs.PackRegistry.ZeldaGameId
                    && (text ?? "").Length <= 9 && !EndsTerminated(text))
                {
                    bool known = false;
                    try
                    {
                        var p = Packs.PackRegistry.Current;
                        known = (p.ItemVocabulary ?? new System.Collections.Generic.List<string>()).Contains(text)
                            || (p.MonsterVocabulary ?? new System.Collections.Generic.List<string>()).Contains(text)
                            || (p.BattleCommands ?? new System.Collections.Generic.List<string>()).Contains(text);
                    }
                    catch { }
                    if (!known)
                    {
                        FileLogger.Log($"[translate] suppressed fragment-hold for \"{Truncate(text)}\" (short unterminated off-vocab)");
                        return;
                    }
                }
                FileLogger.Log($"[translate-fire] \"{Truncate(text)}\"");
                string translated = await _translationManager.TranslateAsync(text);
                FileLogger.Log($"[translate-provider] {_translationManager.ActiveProviderName} for \"{Truncate(text)}\"");
                if (IsUnpublishable(translated))
                {
                    FileLogger.Log($"[translate] untranslatable -> ------ for \"{text}\" (DeepL fallback exhausted/no key)");
                    if (_stopped) { FileLogger.Log($"[translate] id={_id} dropped (stopped)"); return; }
                    CostPubSample(pubSw.Elapsed.TotalMilliseconds);
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
                CostPubSample(pubSw.Elapsed.TotalMilliseconds);
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

        // Fragment-hold terminator set (mirrors IsUnterminatedComposite's
        // complete-endings; hiragana-cut carve-out NOT applied here — a bare
        // verb tail is still a fragment until it terminates or grows).
        private static bool EndsTerminated(string? s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char last = s.TrimEnd()[^1];
            return last == '。' || last == '！' || last == '？' || last == '…' || last == '!' || last == '?';
        }

        // M7a corpus recorder (automatic, text-only, ~2KB/loop). One JSONL per session under
        // %AppData%/WunderLanguage/corpus/. M7b replays raw+blocks through the real fixup/
        // stability/publish path with translation stubbed; exit = identical Japanese bubbles.
        // Off-switch: RecordCorpus=false. Never throws; never blocks OCR.
        // Diagnostics-gated (file setting / --diagnostics): customers never
        // accumulate session files. Translation cache + mined corrections
        // are separate and stay always-on.
        private static bool RecordCorpus => Settings.Diag.Enabled;
        private string _corpusPath = "";
        private readonly object _corpusLock = new();
        // Compressed corpus (perf batch: 302 files / 13.6MB and growing —
        // one held gzip member per session instead of plaintext). Single
        // open stream per session (best ratio); every line sync-flushed so
        // a dirty kill keeps a decompressible prefix. Rotation (game
        // switch) and Stop() close first — never orphan a stream.
        // Reader (ReplayChecker) transparently handles .gz and legacy
        // plaintext side by side. Revert: path back to .jsonl + plain
        // appends + delete fields/methods.
        private System.IO.FileStream? _corpusFs;
        private System.IO.Compression.GZipStream? _corpusGz;
        private System.IO.StreamWriter? _corpusWr;
        private void CorpusEnsure()
        {
            if (_corpusPath != "") return;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage", "corpus");
            Directory.CreateDirectory(dir);
            _corpusPath = Path.Combine(dir, _gameId + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jsonl.gz");
            try
            {
                _corpusFs = new System.IO.FileStream(_corpusPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read);
                _corpusGz = new System.IO.Compression.GZipStream(_corpusFs, System.IO.Compression.CompressionLevel.Fastest);
                _corpusWr = new System.IO.StreamWriter(_corpusGz, System.Text.Encoding.UTF8);
            }
            catch { CorpusClose(); }
        }
        private void CorpusClose()
        {
            try { _corpusWr?.Dispose(); } catch { } finally { _corpusWr = null; }
            try { _corpusGz?.Dispose(); } catch { } finally { _corpusGz = null; }
            try { _corpusFs?.Dispose(); } catch { } finally { _corpusFs = null; }
        }
        private void CorpusRotate()
        {
            try
            {
                lock (_corpusLock) { CorpusClose(); _corpusPath = ""; }
            }
            catch { _corpusPath = ""; }
        }
        private void CorpusLine(string line)
        {
            try
            {
                if (!RecordCorpus) return;
                CorpusEnsure();
                lock (_corpusLock)
                {
                    if (_corpusWr is null || _corpusGz is null) return;
                    _corpusWr.WriteLine(line);
                    _corpusWr.Flush();
                    _corpusGz.Flush();
                }
            }
            catch { }
        }
        private void CorpusRecordOcr(int loop, string raw, List<OcrBlock> blocks, string extracted, bool mined = false)
        {
            try
            {
                if (!RecordCorpus) return;
                var bs = new List<object>(blocks.Count);
                // HDIP B3 calibration feed (forward-only additive fields;
                // replay reads lenient properties and ignores them — parity
                // proven per batch. Old files simply lack c/n/s.).
                // Revert: drop c/n/s from the anonymous type.
                foreach (var b in blocks) bs.Add(new { t = b.Text, x = b.X, y = b.Y, w = b.Width, h = b.Height, c = b.Conf, n = b.NMerge, s = b.Src });
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
                // Logo-window repair (cited 2026-10-07: the Zelda logo dwell is
                // shorter than 30 loops, so the title starved even with the
                // dialogue hold working). Non-FFVI hinted bar is 10; FFVI keeps
                // 30/120 byte-identical, and universal still can't publish
                // (empty FullTitle blocks it below). Revert: delete ffvi line.
                bool ffviTitle = Packs.PackRegistry.Current.GameId == Packs.PackRegistry.FfviGameId;
                int titleBar = _quietHint ? (ffviTitle ? QuietTitleLoops : 10) : QuietTitleLoops * 4;
                // Mistitle repair (cited 2026-10-08: TITLE published at loop 25
                // mid-intro-scroll — copyright-year hint + 10 quiet loops while
                // the blocks were tall inset scroll text, not logo bands).
                // Hinted non-FFVI path additionally requires logo geometry
                // (all JA blocks full-bleed short bands — the same test as the
                // logo hold). FFVI byte-identical; bare-quiet (bar 120) and
                // static (blocks==null) paths unchanged. Revert: delete the
                // logoGeometry conjunct.
                bool logoGeometry = true;
                if (blocks != null && _quietHint && !ffviTitle)
                {
                    // All JA blocks must be logo bands; non-JA blocks
                    // (sprites, HUD) are invisible to this test. No JA at
                    // all passes through (bare-quiet path decides those).
                    logoGeometry = true;
                    foreach (var b in blocks)
                    {
                        if (!HasJapanese(b.Text)) continue;
                        if (!(b.X <= 6 && b.X + b.Width >= _battleFrameW - 6 && b.Height < 100)) { logoGeometry = false; break; }
                    }
                }
                if (_quietCount == titleBar && !string.IsNullOrEmpty(packTitle)
                    && string.IsNullOrEmpty(_menuSignature) && string.IsNullOrEmpty(_candidateText)
                    && logoGeometry)
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
            bool battleFrame = BattleFrame.IsBattle(blocks, _battleFrameW, _battleFrameH, HasJapanese, Packs.PackRegistry.Current.WeaponLabels, _itemBoxOpen);
            // Latch freeze (cited 2026-10-01 bottom spiral: empty-slot
            // frames classify 0 for many loops, draining grace to a wipe
            // that kills context+latch — then every bottom fragment
            // dialogue-fires). An open box holds grace until it closes.
            if (_battleCooldown > 0 && !_itemBoxOpen) _battleCooldown--;
            // Battle owns its rows while a frame is live (plus grace): menu stays
            // silent so the feed shows one frame, never a frame plus a MENU dupe.
            // Scroll remnants stay silent too (same hold as the dialogue guard).
            // Non-battle and pre-frame menus flow exactly as before.
            if (battleFrame && (_battleFiredSig != "" || _battleCooldown > 0)) return;
            if (!battleFrame && (_battleFiredSig != "" || _battleCooldown > 0)
                && BattleFrame.IsBattleRemnant(blocks, _battleFrameW, _battleFrameH, HasJapanese)) return;
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
            // Title purity (cited 2026-10-07: Zelda scroll fragment
            // "これからの" rode a marker row into a minted TITLE).
            // Zelda-gated: TITLE bubbles carry only the TitleMarker row;
            // frame junk is omission-only. FFVI untouched (neither FFVI
            // corpus contains a TITLE, so harmlessness is unprovable via
            // replay — pack-scope instead). Revert: delete this block.
            if (kind == "TITLE" && Packs.PackRegistry.Current.GameId == Packs.PackRegistry.ZeldaGameId)
            {
                var markerRows = jaRows.Where(a => (a.t ?? "").Contains(Packs.PackRegistry.Current.TitleMarker)).ToList();
                if (markerRows.Count > 0) jaRows = markerRows;
            }
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
            // Latch expiry: mid-band JA means non-item UI (field, mid
            // dialogue, name entry) — the box is no longer what's on
            // screen. Top-band text coexists with an open box (mid-battle
            // messages) and bottom-only frames never expire it (empty-slot
            // study keeps the latch; the blank-fire covers those).
            if (_itemBoxOpen && HasMidJa(blocks)) _itemBoxOpen = false;
            _lastAnchorS = 0;
            _rescueCarry = null;
            // Box-close detector (load-bearing for the latch): action commands
            // visible in the bottom band mean the action box is back, so the
            // item box is closed. Runs on raw blocks BEFORE extraction —
            // post-slots the action text would already be claimed away by
            // latched gates and the latch could never clear (every action
            // frame would route to item forever).
            if (BottomHasCommands(blocks)) _itemBoxOpen = false;
            string topRaw;
            int cls = BattleFrame.Classify(blocks, _battleFrameW, _battleFrameH, HasJapanese, Packs.PackRegistry.Current.WeaponLabels, out topRaw, _itemBoxOpen);
            // Item box and weapon box are trusted only mid-battle (battle context); elsewhere
            // they fall through to menu/dialogue handling below.
            if ((cls == 3 || cls == 4) && !InBattleContext()) cls = 0;
            // Leaving battle clears the fired mark: an identical fight later refires
            // (panel spawns new only if other text came between), menus flow again.
            // Grace hold (cited 2026-09-30): a single unclassifiable frame —
            // OCR dropout (0 blocks) or scroll fragment — must not wipe
            // context while cooldown is alive, or the item box falls to
            // dialogue mid-scroll. The wipe lands once cooldown truly expires.
            if (cls == 0)
            {
                // Latched short-row rescue runs BEFORE blank-fire (cited
                // 2026-10-02: bottom views classify 0 (wide-ish shorts fail
                // both arms) and blank-fired over real [Y|Kd] content).
                // Success promotes to class 3 and falls through to the full
                // pipeline below (slots, anchor, memory, settle, fire).
                List<string>? rescueCols0 = null;
                if (_itemBoxOpen)
                {
                    rescueCols0 = TryAssembleShortRows(blocks, Packs.PackRegistry.Current.BattleCommands ?? new System.Collections.Generic.List<string>(), 0);
                    if (rescueCols0 != null) { cls = 3; _rescueCarry = rescueCols0; }
                }
                if (cls == 0)
                {
                // Latched empty bottom (cited 2026-10-01: scrolled past the
                // last item, both columns blank): fire the empty box once so
                // the panel renders blanks, never hallucinations. Gated to
                // bottom-only narrow frames (wide = dialogue box, which must
                // still fire normally); zero-block dropouts hold silently.
                // Same-sig repeats hold via the fire gate below.
                if (_itemBoxOpen && HasBottomContent(blocks) && !HasTopOrMidJa(blocks) && !HasWideBlock(blocks))
                {
                    var blanks = new System.Collections.Generic.List<string> { "　\t　", "　\t　", "　\t　", "　\t　" };
                    var bShown = new BattleFrame.BattleSlots("", new System.Collections.Generic.List<string>(_battleMonsterSticky), new System.Collections.Generic.List<string>(), new System.Collections.Generic.List<string>(), blanks, new System.Collections.Generic.List<string>());
                    string bsig = bShown.Top + "\n" + string.Join("\n", bShown.Monster) + "\n" + string.Join("\n", bShown.Action) + "\n" + string.Join("\n", bShown.Right) + "\n" + string.Join("\n", bShown.Item) + "\n" + string.Join("\n", bShown.Weapon);
                    _battlePrevSig = bsig;
                    bool blankSettled = bsig == _battleBlankFirePrev;
                    _battleBlankFirePrev = bsig;
                    if (bsig != _battleFiredSig && blankSettled)
                    {
                        _battleFiredSig = bsig;
                        _battleCooldown = 6;
                        FileLogger.Log($"[battle] fire blanks top=0ch mon={bShown.Monster.Count} item=4 (empty box)");
                        _ = TranslateAndPublishBattleAsync(bsig, bShown);
                    }
                    return;
                }
                if (_battleCooldown > 0 || _itemBoxOpen) return;
                _battlePrevSig = ""; _battleFiredSig = ""; _battleMonsterSticky = new List<string>(); _battleItemSticky = new List<string>(); _itemBoxOpen = false; _battleItemFirePrev = ""; _battleBlankFirePrev = ""; _battleItemFireRuns = 0; _battleItemFiredContent = 0; _battleWeaponFirePrev = ""; _battleWeaponFireRuns = 0; _battleWeaponFired = ""; return;
                }
            }
            // Any battle-classified loop refreshes grace, so the countdown only
            // starts once battle truly ends (fires set it too, same value).
            // Oscillation hold: repeat pixels (cursor-blink cycles) carry no
            // new information even when OCR re-reads grey rows differently —
            // hold content, keep grace alive, touch nothing.
            if (!_lastFrameNovel) { _battleCooldown = 6; return; }
            _battleCooldown = 6;
            var slots = BattleFrame.ExtractSlots(blocks, _battleFrameW, _battleFrameH, HasJapanese, Packs.PackRegistry.Current.WeaponLabels, t => IsEllipsisOnly(t), _itemBoxOpen);
            // Party rows keep HP digits (name + HP post together, HP ticks
            // refire live like monster counts); names translate once via the
            // digit-split path, digits reattach verbatim. Reward tops need
            // substance:
            // まえば-class flicker stays dialogue-suppressed but never posts,
            // so the top bar sits empty until real post-fight rewards arrive.
            slots = slots with { Top = slots.Top.Length < 4 ? "" : slots.Top };
            // Party-slot dust guard (mirrors the monster one): standalone
            // single-kana rows (lone だ/ェ) never post; bare counts stay.
            // Joined dust too (cited 2026-10-01: "マッシュ だ" fired — the
            // だ timebar read joins the name's line, so row-length misses
            // it): lone-kana tokens drop, names survive. Multi-char tokens
            // (all real names) pass untouched.
            slots = slots with
            {
                Right = slots.Right.Select(r =>
                {
                    var kept = new System.Collections.Generic.List<string>();
                    foreach (var tok in r.Split(new[] { ' ', '　' }, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = tok.Trim();
                        if (t.Length == 1 && IsKanaChar(t[0])) continue;
                        kept.Add(tok);
                    }
                    return string.Join(" ", kept);
                }).Where(r =>
                {
                    string t = r.Trim();
                    if (t.Length == 0) return false;
                    if (t.Length != 1) return true;
                    SplitRowDigits(r, out string nm, out _);
                    return string.IsNullOrWhiteSpace(nm);
                }).ToList()
            };
            var commands = Packs.PackRegistry.Current.BattleCommands ?? new System.Collections.Generic.List<string>();
            // Latched short-row rescue (cited 2026-10-01: scrolled to the
            // last 2 rows, blocks go short (h~48), miss even lowered tall
            // gates, scatter to monster/right — the box "closed itself").
            // Content-gated (vocab/fixup shapes only; monster/party/dust can
            // never match their disjoint shapes), so stale-latch monster
            // phases assemble nothing and route normally. Circuit-breaker:
            // any action-command row aborts (action box content must never
            // become items). Synthetic \n-hard columns flow through the
            // normal split/zip/anchor/memory/settle/fire below; rows pad to
            // 4 with blanks (empties render, positions hold).
            var rescueCols = _rescueCarry;
            _rescueCarry = null;
            rescueCols ??= TryAssembleShortRows(blocks, commands, cls);
            if (rescueCols != null)
                slots = slots with { Monster = new List<string>(), Action = new List<string>(), Right = new List<string>() };
            // Overlap bit: action rows normalize through pack fixups, then split
            // on pack command vocabulary into positional rows (blank magic gap).
            var normAction = slots.Action.ConvertAll(a => ApplyZeldaOcrFixups(a));
            // Tall-block disambiguation: geometry alone files a tall monster
            // list (two names, one OCR block — same height as the command box)
            // into the action slot. Only lines carrying command vocabulary (or
            // short enough to be a command fragment) stay; the rest spill back
            // to the monster column with their count. Unknown game (no vocab):
            // geometry as before.
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
            // Single-char letter dust (ェ-class, 28 cited fires) never posts —
            // it translated into stray EN ("I") every flicker loop. Bare
            // counts stay: counts always sit with names in-game, a lone digit
            // is truthful where a lone letter is noise.
            monsterLines.RemoveAll(r =>
            {
                string t = r.Trim();
                if (t.Length == 0 || t.Length >= 2) return false;
                SplitRowDigits(r, out string nm, out _);
                return !string.IsNullOrWhiteSpace(nm);
            });
            var itemJa = BuildItemPairs(rescueCols ?? slots.Item, _battleItemSticky);
            // Item memory: fill blank sides from last-seen rows so grey-row
            // flicker holds positions instead of collapsing. Strict guards so
            // memory can never invent or misplace: same pair count, blank
            // sides only, and a blank side fills only when the pair's other
            // side matches memory (same row). Whole-row restore is BANNED
            // (cited 2026-10-01: scrolled layout kept pair count 4 while
            // moving フェニックスのお to row 1, and the blind restore
            // duplicated it into row 3 — pair count is not layout identity).
            // A fully-blank flicker row simply stays blank until re-read.
            // Anything else posts fresh and rewarms memory. Counts ride
            // inside remembered rows verbatim; new sightings overwrite.
            {
                var mem = _battleItemSticky;
                if (mem.Count > 0 && itemJa.Count == mem.Count)
                {
                    // Fill-guard (cited 2026-10-01: mem-Y filled a blank side
                    // while current also held Y elsewhere — duplicate smoke
                    // bombs). Fresh reads win: skip any fill whose normalized
                    // name already occurs in the current assembly, and feed
                    // each fill back into the set so fossil mem can't spread
                    // one name across two rows.
                    var curNorms = new System.Collections.Generic.HashSet<string>();
                    foreach (var cp in itemJa)
                    {
                        SplitItemPair(cp, out string ccl, out string ccr);
                        if (!string.IsNullOrWhiteSpace(ccl)) curNorms.Add(NormItemKey(ccl));
                        if (!string.IsNullOrWhiteSpace(ccr)) curNorms.Add(NormItemKey(ccr));
                    }
                    var held = new List<string>(itemJa.Count);
                    for (int i = 0; i < itemJa.Count; i++)
                    {
                        SplitItemPair(itemJa[i], out string cl, out string cr);
                        SplitItemPair(mem[i], out string ml, out string mr);
                        string l = cl, r = cr;
                        if (!string.IsNullOrWhiteSpace(cl) || !string.IsNullOrWhiteSpace(cr))
                        {
                            if (string.IsNullOrWhiteSpace(cl) && cr == mr && !curNorms.Contains(NormItemKey(ml))) { l = ml; curNorms.Add(NormItemKey(ml)); }
                            if (string.IsNullOrWhiteSpace(cr) && cl == ml && !curNorms.Contains(NormItemKey(mr))) { r = mr; curNorms.Add(NormItemKey(mr)); }
                        }
                        held.Add(l + "\t" + r);
                    }
                    itemJa = held;
                }
                _battleItemSticky = new List<string>(itemJa);
            }
            // Pair duplicate blanking (cited 2026-10-01: same canonical name
            // twice — both sides checked, soup exempt — is always a ghost:
            // inventory names are unique per box. Keep first top-down, blank
            // later sides. Left column wins ties (cursor side reads cleaner).
            // Runs after fills so fill-induced dupes die here too.
            {
                var seenPair = new System.Collections.Generic.HashSet<string>();
                var canonPair = new System.Collections.Generic.HashSet<string>(
                    Packs.PackRegistry.Current.ItemVocabulary ?? new System.Collections.Generic.List<string>());
                for (int i = 0; i < itemJa.Count; i++)
                {
                    SplitItemPair(itemJa[i], out string pl, out string pr);
                    bool ch = false;
                    string nl = NormItemKey(pl), nr = NormItemKey(pr);
                    if (!string.IsNullOrWhiteSpace(pl) && canonPair.Contains(nl))
                    {
                        if (!seenPair.Add(nl)) { pl = "　"; ch = true; }
                    }
                    if (!string.IsNullOrWhiteSpace(pr) && canonPair.Contains(nr))
                    {
                        if (!seenPair.Add(nr)) { pr = "　"; ch = true; }
                    }
                    if (ch) itemJa[i] = pl + "\t" + pr;
                }
            }
            // Weapon rows post verbatim (slot labels + weapon names, mockup
            // equip box — no counts, no command gate).
            var weaponJa = new List<string>(slots.Weapon);
            if (monsterLines.Count > 0) _battleMonsterSticky = new List<string>(monsterLines);
            // Item content gate (moved here from the pair drop): blank pairs
            // are positional rows, not content — firing rides on any side
            // having text. All-blank assemblies (fully grey box) never fire,
            // and the monster sticky no longer leaks through them either
            // (that was the "refused" phantom shape).
            bool itemHasContent = false;
            foreach (var p in itemJa)
                if (!string.IsNullOrWhiteSpace((p ?? "").Replace("\t", ""))) { itemHasContent = true; break; }
            var shownMonster = (actionLines.Count > 0 || itemHasContent || weaponJa.Count > 0) && _battleMonsterSticky.Count > 0 ? _battleMonsterSticky : monsterLines;
            // Latch tracking: item content means the box is open. Runs every
            // loop (not per fire) so slow scroll dwells — which refire
            // nothing — keep the latch. Clearing runs on box-close signals
            // only (commands pre-extraction, full wipe, mid-band expiry):
            // monster rows must NEVER clear it — bottom-of-list views ARE
            // monster-shaped short rows (cited 2026-10-01: latch cleared at
            // the bottom, item box "closed" itself and leaked items into
            // the party box).
            // Open edge (cited 2026-10-01: 19s dead panel on open while
            // flicker never settled twice): a newly opened box posts its
            // first sight immediately; later updates settle normally.
            bool justOpened = itemHasContent && !_itemBoxOpen;
            // Post-battle rewards expiry: a top bar with no enemies and no
            // action box means the fight is over (mid-battle messages always
            // have monsters or action rows present, so they never trip this).
            // Placed before the set below so a visibly open box re-latches
            // in the same pass.
            if (_itemBoxOpen && cls == 2 && monsterLines.Count == 0 && actionLines.Count == 0) _itemBoxOpen = false;
            if (itemHasContent) _itemBoxOpen = true;
            var actionRows = BattleFrame.SplitActionRows(actionLines, commands);
            var shown = new BattleFrame.BattleSlots(slots.Top, shownMonster, actionRows, slots.Right, itemJa, weaponJa);
            string sig = shown.Top + "\n" + string.Join("\n", shown.Monster) + "\n" + string.Join("\n", shown.Action) + "\n" + string.Join("\n", shown.Right) + "\n" + string.Join("\n", shown.Item) + "\n" + string.Join("\n", shown.Weapon);
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
            // Slots telemetry (permanent, one line per battle loop): convicts
            // silent no-fires (suppressed-but-empty assemblies) with cls and
            // every slot count. Cited 2026-10-01: open-box frames suppressed
            // 5 loops with zero fires and zero errors.
            FileLogger.Log($"[battle] slots cls={cls} top={shown.Top.Length}ch mon={shown.Monster.Count} act={shown.Action.Count} right={shown.Right.Count} itemCols={slots.Item.Count} itemJa={shown.Item.Count} weapon={shown.Weapon.Count}");
            // Battle fires on first sight: the frame updates in place and stale
            // publishes drop, so a lone misread self-corrects next loop instead of
            // waiting out a stability bar while the game moves on. Dialogue and
            // menu keeps their frozen gates; battle is the live surface.
            // Action rides the sig, so open/close always counts as a change.
            // Item rides it too (open/scroll/close refire in place). Weapon
            // rides it as well (mockup layering: each box opens on top of
            // the previous, and all refresh live as boxes open and close).
            // Item-carrying sigs wait for the settle gate above, now with
            // hysteresis: gaining (or keeping) content fires at 2 repeats
            // (fast scrolls and recoveries stay snappy), losing content
            // needs 4 (a grey flicker-out must prove itself — the panel
            // holds last-stable instead of blinking). Confident scrolls
            // (anchor shift nonzero) bypass: real movement posts instantly,
            // flicker never estimates a shift.
            int itemContent = 0;
            foreach (var p in itemJa)
                if (!string.IsNullOrWhiteSpace((p ?? "").Replace("\t", ""))) itemContent++;
            if (sig == _battleItemFirePrev) _battleItemFireRuns++;
            else { _battleItemFirePrev = sig; _battleItemFireRuns = 1; }
            bool itemSettled = !itemHasContent || _lastAnchorS != 0 || justOpened
                || _battleItemFireRuns >= (itemContent < _battleItemFiredContent ? 4 : 2);
            // Weapon-new bypass: stable weapon rows the last publish hasn't
            // seen post at 2 repeats, even mid item-tail flicker. Closing
            // (empty weapon) never bypasses — the item gate owns the close.
            string wsig = string.Join("\n", shown.Weapon);
            if (wsig == _battleWeaponFirePrev) _battleWeaponFireRuns++;
            else { _battleWeaponFirePrev = wsig; _battleWeaponFireRuns = 1; }
            bool weaponNew = shown.Weapon.Count > 0 && wsig != _battleWeaponFired && _battleWeaponFireRuns >= 2;
            if (sig != _battleFiredSig && (shown.Top.Length > 0 || shown.Monster.Count > 0 || shown.Action.Count > 0 || itemHasContent || shown.Weapon.Count > 0) && (itemSettled || weaponNew))
            {
                _battleFiredSig = sig;
                _battleWeaponFired = wsig;
                _battleCooldown = 6;
                _battleItemFiredContent = itemContent;
                FileLogger.Log($"[battle] fire top={shown.Top.Length}ch mon={shown.Monster.Count} act={shown.Action.Count} right={shown.Right.Count} item={shown.Item.Count} weapon={shown.Weapon.Count} sig={SigTag(sig)}");
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
                    foreach (var l in slots.Monster)
                    {
                        // Name-part key when digits ride along (perf batch:
                        // the publish path strips digits before lookup, so a
                        // full-row key never hit). Mirrors TranslateRowSplit.
                        SplitRowDigits(l, out string mn, out string md);
                        string mkey = string.IsNullOrWhiteSpace(md) ? l : mn;
                        string mval = mkey;
                        PrefetchNeed(mkey, () => TranslateMenuJaRowAsync(mval), fresh);
                    }
                    foreach (var l in slots.Action) PrefetchNeed(l, () => TranslateMenuJaRowAsync(l), fresh);
                    // Name-part key, glossary-aware translate: the prefetch must
                    // resolve names exactly like the publish path, or a raw
                    // provider serve ("Massh") would shadow the pinned EN.
                    foreach (var n in slots.Right) { SplitRowDigits(n, out string rn, out _); string key = string.IsNullOrWhiteSpace(rn) ? n : rn; PrefetchNeed(key, () => TranslateMenuJaRowAsync(key), fresh); }
                    foreach (var l in slots.Item) PrefetchNeed(l, () => TranslateItemRowAsync(l), fresh);
                    // Weapon pairs prefetch per side (the publish path looks
                    // up sides, never whole pairs — same glossary pins).
                    foreach (var w in slots.Weapon)
                    {
                        // Whole-pair entry for the outer row lookup (perf
                        // batch: TranslateRowSplitAsync looks up the pair
                        // before fanning to sides — without this it always
                        // missed and counted). Same function either way.
                        PrefetchNeed(w, () => TranslateWeaponRowAsync(w), fresh);
                        SplitItemPair(w, out string wl, out string wr);
                        if (!string.IsNullOrWhiteSpace(wl)) PrefetchNeed(wl, () => TranslateMenuJaRowAsync(wl), fresh);
                        if (!string.IsNullOrWhiteSpace(wr)) PrefetchNeed(wr, () => TranslateMenuJaRowAsync(wr), fresh);
                    }
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
            if (TryPrefetch(ja, out var pre)) { Interlocked.Increment(ref _costPrefHit); return pre; }
            Interlocked.Increment(ref _costPrefMiss);
            return await tx();
        }
        private async Task<List<string>> TranslateBattleRowsAsync(List<string> ja, List<string> lastJa, List<string> lastEn, System.Func<string, System.Threading.Tasks.Task<string>> translateOne, bool simMemo = false, bool rowsFinal = false)
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
                else if (sticky.TryGetValue(l, out var e)) { Interlocked.Increment(ref _costStickyHit); tasks[i] = Task.FromResult(e); }
                else { Interlocked.Increment(ref _costStickyMiss); tasks[i] = rowsFinal ? translateOne(l) : TranslateRowSplitAsync(l, translateOne, simMemo); }
            }
            var ens = await Task.WhenAll(tasks);
            var result = new List<string>(ens.Length);
            // Final rows (item pairs) skip the unpublishable mask: sides are
            // already individually sensible (JA-verbatim pending or real EN),
            // and masking a half-failed pair would hide its good side too.
            for (int i = 0; i < ens.Length; i++) result.Add(emptyBypass[i] ? "" : (rowsFinal ? ens[i] : (IsUnpublishable(ens[i]) ? "------" : ens[i])));
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
                if (isDigits) { digs.Add(k); continue; }
                // Glued suffix split: OCR glues Latin/digit/punct tails onto
                // names ("マッシュFACHTASSORYURUBOOK.", "マッシュ231").
                // Leading-JA head stays a name (pins keep hitting); tail
                // digits join counts; tail junk drops (never name content,
                // never counts). No JA lead (pure "MAP") preserves as-is.
                int p = 0;
                while (p < s.Length && IsJaNameChar(s[p])) p++;
                if (p == 0) { names.Add(k); continue; }
                if (p < s.Length)
                {
                    names.Add(s.Substring(0, p));
                    foreach (char c in s.Substring(p))
                        if ((c >= '0' && c <= '9') || (c >= '０' && c <= '９')) digs.Add(c.ToString());
                    continue;
                }
                names.Add(k);
            }
            // Trailing single-kana dust (だ/は/が particles, ェ fragments
            // glued by same-Y joining) is never name content: strip it so
            // pins like マッシュ→Matthew keep hitting. Digits untouched;
            // single-token names (フェニックスのお) never match this shape.
            while (names.Count > 0 && names[names.Count - 1].Length == 1 && IsKanaChar(names[names.Count - 1][0]))
                names.RemoveAt(names.Count - 1);
            if (digs.Count == 0) return;
            name = string.Join(" ", names); digits = string.Join(" ", digs);
        }
        private static void SplitItemPair(string row, out string left, out string right)
        {
            string src = row ?? "";
            int ti = src.IndexOf('\t');
            left = ti < 0 ? src : src.Substring(0, ti);
            right = ti < 0 ? "" : src.Substring(ti + 1);
        }
        private static bool IsKanaChar(char c)
        {
            return (c >= 'ぁ' && c <= 'ん') || (c >= 'ァ' && c <= 'ヶ') || c == 'ー' || (c >= 'ｦ' && c <= 'ﾟ');
        }
        // JA name characters for glued-suffix splitting: hiragana, katakana
        // (full + half width), kanji, prolonged marks. Latin/digits/punct
        // never start name content in battle rows.
        private static bool IsJaNameChar(char c)
        {
            return (c >= 'ぁ' && c <= 'ん') || (c >= 'ァ' && c <= 'ヶ') || (c >= '一' && c <= '鿿') ||
                   c == 'ー' || c == '々' || (c >= 'ｦ' && c <= 'ﾟ');
        }
        private async Task<string> TranslateRowSplitAsync(string row, System.Func<string, System.Threading.Tasks.Task<string>> translateOne, bool simMemo = false)
        {
            SplitRowDigits(row, out string name, out string digits);
            // Dust-only rows (a lone だ/ェ with no name and no digits) post
            // nothing; digit-only rows still post verbatim below.
            if (string.IsNullOrWhiteSpace(name)) return string.IsNullOrWhiteSpace(digits) ? "" : row;
            if (string.IsNullOrEmpty(digits))
            {
                string whole = await PrefetchOrTranslateAsync(row, () => translateOne(row));
                if (simMemo) MemoizeBattleName(row, whole);
                return whole;
            }
            string en;
            if (simMemo && TrySimilarBattleEn(name, out var simEn))
            {
                Interlocked.Increment(ref _costSimHit);
                en = simEn;
                FileLogger.Log($"[battle] memo-sim \"{name}\" -> \"{en}\"");
            }
            else
            {
                if (simMemo) Interlocked.Increment(ref _costSimMiss);
                en = await PrefetchOrTranslateAsync(name, () => translateOne(name));
                if (simMemo) MemoizeBattleName(name, en);
            }
            if (string.IsNullOrWhiteSpace(en)) return row;
            return en + " " + digits;
        }
        // Item-pair translation: "left\tRight" sides translate independently
        // (name→EN via the menu path, counts reattach verbatim), concurrently.
        // Missing sides stay blank (the panel pads for alignment).
        // Weapon-pair translation (cited 2026-10-03: whole-row CAT turned
        // "みぎて ひだりで" into sentence salad and typo'd the name).
        // Sides translate independently through the menu path (glossary
        // pins: みぎて→Right, ひだりで→Left, メタルナックル→Metal Knuckle),
        // simMemo OFF (weapon names must never join the battle splitter
        // keys or sim pool — equip states, not names to memorize).
        private async Task<string> TranslateWeaponRowAsync(string row)
        {
            string src = row ?? "";
            int ti = src.IndexOf('\t');
            string l = ti < 0 ? src : src.Substring(0, ti);
            string r = ti < 0 ? "" : src.Substring(ti + 1);
            var lt = string.IsNullOrWhiteSpace(l) ? Task.FromResult("") : TranslateRowSplitAsync(l, t => TranslateMenuJaRowAsync(t), false);
            var rt = string.IsNullOrWhiteSpace(r) ? Task.FromResult("") : TranslateRowSplitAsync(r, t => TranslateMenuJaRowAsync(t), false);
            await Task.WhenAll(lt, rt);
            string le = await lt, re = await rt;
            return ti < 0 ? le : le + "\t" + re;
        }
        private async Task<string> TranslateItemRowAsync(string row)
        {
            // Prefetch alignment (perf batch: prefetch writes whole pairs
            // but the final-row path never looked them up — orphaned
            // entries that only warmed tcache by side effect). Same key,
            // same function as the prefetch closure; a hit skips both
            // sides, misses flow through unchanged.
            string key = row ?? "";
            return await PrefetchOrTranslateAsync(key, () => TranslateItemRowInnerAsync(key));
        }
        private async Task<string> TranslateItemRowInnerAsync(string row)
        {
            string src = row ?? "";
            int ti = src.IndexOf('\t');
            string l = ti < 0 ? src : src.Substring(0, ti);
            string r = ti < 0 ? "" : src.Substring(ti + 1);
            var lt = TranslateRowSplitAsync(l, t => TranslateMenuJaRowAsync(t), true);
            var rt = string.IsNullOrWhiteSpace(r) ? Task.FromResult("") : TranslateRowSplitAsync(r, t => TranslateMenuJaRowAsync(t), true);
            await Task.WhenAll(lt, rt);
            string le = await lt, re = await rt;
            return ti < 0 ? le : le + "\t" + re;
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
                // Soup self-lock guard (cited 2026-10-01: 10-char
                // こういうのはあぐすり posted once, memoized, then kept
                // matching as a splitter key forever, bypassing the gap
                // bar). Longest cited legit names run 8 (ナイトのこころえ,
                // フェニックスのお; monsters 7), so 9+ never memoizes —
                // translation still serves them via cache, only sim-reuse
                // and splitter keys are withheld.
                if ((ja ?? "").Trim().Length > 8) return;
                string jkey = (ja ?? "").Trim();
                if (jkey.Length == 0) return;
                lock (_prefetchLock)
                {
                    if (_battleNameMemo.Count > 200) _battleNameMemo.Clear();
                    _battleNameMemo[jkey] = en;
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
        // Box-close detector helper: action commands visible in the bottom
        // band mean the action box is back (item box closed). Item names
        // never contain commands and monster names neither, so this only
        // ever matches the action box. Unknown games (<5 commands) never
        // clear: the latch never sets there either.
        // Latch helpers: band/shape evidence for the open-box latch.
        // Mid-band JA expires it (field/mid-dialogue on screen); bottom
        // content with no wide block marks empty-box frames eligible for
        // blank-fire.
        private bool HasMidJa(List<OcrBlock> blocks)
        {
            try
            {
                if (blocks == null) return false;
                foreach (var b in blocks)
                {
                    string t = (b.Text ?? "").Trim();
                    if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                    if (!HasJapanese(t)) continue;
                    double ny = (double)b.Y / System.Math.Max(1, _battleFrameH);
                    if (ny >= 0.25 && ny < 0.65) return true;
                }
            }
            catch { }
            return false;
        }
        // Blank-fire gate keeps the wider exclusion: only pure-bottom
        // frames may post the empty box (a top message + empty bottom
        // routes as today, unchanged).
        private bool HasTopOrMidJa(List<OcrBlock> blocks)
        {
            try
            {
                if (blocks == null) return false;
                foreach (var b in blocks)
                {
                    string t = (b.Text ?? "").Trim();
                    if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                    if (!HasJapanese(t)) continue;
                    if ((double)b.Y / System.Math.Max(1, _battleFrameH) < 0.65) return true;
                }
            }
            catch { }
            return false;
        }
        private bool HasBottomContent(List<OcrBlock> blocks)
        {
            try
            {
                if (blocks == null) return false;
                foreach (var b in blocks)
                {
                    string t = (b.Text ?? "").Trim();
                    if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                    if ((double)b.Y / System.Math.Max(1, _battleFrameH) >= 0.5) return true;
                }
            }
            catch { }
            return false;
        }
        private bool HasWideBlock(List<OcrBlock> blocks)
        {
            try
            {
                if (blocks == null) return false;
                foreach (var b in blocks)
                {
                    string t = (b.Text ?? "").Trim();
                    if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                    if ((double)b.Width / System.Math.Max(1, _battleFrameW) >= 0.60) return true;
                }
            }
            catch { }
            return false;
        }
        private bool BottomHasCommands(List<OcrBlock> blocks)
        {
            try
            {
                var cmds = Packs.PackRegistry.Current.BattleCommands;
                if (cmds == null || cmds.Count < 5 || blocks == null) return false;
                foreach (var b in blocks)
                {
                    if ((double)b.Y / System.Math.Max(1, _battleFrameH) < 0.6) continue;
                    string t = b.Text ?? "";
                    if (t.Length < 1) continue;
                    foreach (var c in cmds)
                        if (!string.IsNullOrEmpty(c) && t.Contains(c, System.StringComparison.Ordinal)) return true;
                }
            }
            catch { }
            return false;
        }
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
            // Pack vocab pre-warms the memo: cold multi-type fights split
            // one-line-each on first sight (cited names only). Longest first,
            // same as before — vocab just joins the key pool.
            var keys = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var k in Packs.PackRegistry.Current.MonsterVocabulary ?? new System.Collections.Generic.List<string>())
                if (!string.IsNullOrEmpty(k) && seen.Add(k)) keys.Add(k);
            lock (_prefetchLock)
                foreach (var k in _battleNameMemo.Keys)
                    if (!string.IsNullOrEmpty(k) && k.Length >= 5 && k.Length <= 9 && seen.Add(k)) keys.Add(k);
            keys.Sort((a, b) => b.Length.CompareTo(a.Length));
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
        // Latched short-row rescue: bottom-of-list views (1-2 content rows,
        // blocks too short for tall columns) assemble here instead of
        // scattering to monster/right. Returns synthetic \n-hard columns
        // (or null when inapplicable) for the normal pipeline below.
        private List<string>? TryAssembleShortRows(List<OcrBlock> blocks, System.Collections.Generic.IReadOnlyList<string> commands, int cls)
        {
            try
            {
                if (!_itemBoxOpen || (cls != 1 && cls != 0) || blocks == null || blocks.Count == 0) return null;
                var vocab = new System.Collections.Generic.HashSet<string>(
                    Packs.PackRegistry.Current.ItemVocabulary ?? new System.Collections.Generic.List<string>());
                if (vocab.Count == 0) return null;
                var fixupFroms = new System.Collections.Generic.HashSet<string>();
                try
                {
                    foreach (var (from, to) in Packs.PackRegistry.Current.OcrFixups)
                        if (!string.IsNullOrEmpty(from)) fixupFroms.Add(from);
                }
                catch { }
                bool CitedShape(string raw)
                {
                    string t = (raw ?? "").Trim();
                    if (t.Length < 1) return false;
                    string nk = NormItemKey(t);
                    if (vocab.Contains(nk)) return true;
                    string key = t.Trim('、', '。', '，', ',', '.', '．', '…', '・');
                    return vocab.Contains(key) || fixupFroms.Contains(key) || fixupFroms.Contains(t.Trim());
                }
                // Circuit-breaker: action content must never become items.
                foreach (var b in blocks)
                {
                    if ((double)b.Y / System.Math.Max(1, _battleFrameH) < 0.6) continue;
                    if (ContainsBattleCommand(b.Text ?? "", commands)) return null;
                }
                var rows = new System.Collections.Generic.List<(int y, System.Collections.Generic.List<OcrBlock> parts)>();
                foreach (var b in blocks.OrderBy(b => b.Y))
                {
                    string t = (b.Text ?? "").Trim();
                    if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                    if ((double)b.Y / System.Math.Max(1, _battleFrameH) < 0.6) continue;
                    if (IsEllipsisOnly(t)) continue;
                    SplitRowDigits(t, out string nm, out _);
                    if (string.IsNullOrWhiteSpace(nm) || !CitedShape(nm)) continue;
                    bool placed = false;
                    foreach (var ln in rows)
                    {
                        if (System.Math.Abs(b.Y - ln.y) < System.Math.Min(b.Height, 60) / 2 + 8)
                        {
                            ln.parts.Add(b);
                            placed = true;
                            break;
                        }
                    }
                    if (!placed) rows.Add((b.Y, new System.Collections.Generic.List<OcrBlock> { b }));
                }
                if (rows.Count == 0) return null;
                var left = new System.Collections.Generic.List<string>();
                var right = new System.Collections.Generic.List<string>();
                foreach (var ln in rows.OrderBy(l => l.y))
                {
                    string l = "", r = "";
                    foreach (var p in ln.parts.OrderBy(p => p.X))
                    {
                        SplitRowDigits((p.Text ?? "").Trim(), out string nm, out _);
                        if (string.IsNullOrWhiteSpace(nm) || !CitedShape(nm)) continue;
                        double cx = (double)(p.X + p.Width / 2) / System.Math.Max(1, _battleFrameW);
                        if (cx < 0.38) l = l.Length == 0 ? nm : l + " " + nm;
                        else r = r.Length == 0 ? nm : r + " " + nm;
                    }
                    if (l.Length == 0 && r.Length == 0) continue;
                    left.Add(l);
                    right.Add(r);
                }
                if (left.Count == 0 && right.Count == 0) return null;
                FileLogger.Log($"[battle] item-rescue rows={left.Count}");
                return new System.Collections.Generic.List<string>
                {
                    string.Join("\n", left) + "\t",
                    string.Join("\n", right) + "\t"
                };
            }
            catch { return null; }
        }
        // Item-pair assembly: raw "names\tcounts" columns become visual
        // "left\tRight" rows. Names split on pack vocabulary + memoized names
        // (longest first; unknown gaps stay as truthful remainder items);
        // counts zip positionally when the digit count matches, else names
        // post count-less (wrong counts are worse than none). Blank holds
        // never take counts. Each column normalizes to exactly 4 rows: the
        // window shows 4, missing slots distribute over the blank holds first
        // (positions hold 1:1), tail-pad only when nothing unreadable marks a
        // spot, truncate past 4 (extras are flicker). Empty inventory rows
        // have no text, so scrolling and the endless blanks post nothing.
        private List<string> BuildItemPairs(List<string> cols, List<string>? memPairs = null)
        {
            var colItems = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
            foreach (var c in cols ?? new System.Collections.Generic.List<string>())
            {
                string src = c ?? "";
                int ti = src.IndexOf('\t');
                string namesRaw = ti < 0 ? src : src.Substring(0, ti);
                string countsRaw = ti < 0 ? "" : src.Substring(ti + 1);
                // Tail-pad lead (cited 2026-10-03 weapon-open fire: tail
                // cols carry Battleframe's leading blank hard-rows to sit
                // row 4, but the splitter below drops blank chunks — the
                // content top-filled row 1, hiding behind the weapon box
                // while the panel showed blanks underneath). A blob that
                // OPENS with blank lines is tail-pad only (scroll windows
                // are content-top-aligned, never leading-blank) — count it
                // so the assembled column re-anchors to the bottom below.
                int leadingPad = 0;
                foreach (var chunk in namesRaw.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(chunk)) leadingPad++;
                    else break;
                }
                var items = SplitItemNames(namesRaw);
                var digits = DigitChars(countsRaw);
                // Any blank hold vetoes the zip: with rows unreadable the
                // surviving digits can't be placed truthfully (wrong counts
                // are worse than none). Complete reads zip exactly as before.
                bool hasHold = items.Any(s => string.IsNullOrWhiteSpace(s));
                var column = new System.Collections.Generic.List<string>();
                // End-anchored zip (complete-window gate): bright reads whose
                // middle rows vanished without a dots trace still place
                // first↔first and last↔last. Only the full 4-digit window
                // carries complete information. Middles pack top-down right
                // after first (cited 2026-10-01: どくけし rode row 3 under a
                // blank; truth row 2 — content packs top-down everywhere,
                // blanks sink); middles stay bare (a middle name could sit on
                // either side of its digit). Logged: a grey first row would
                // misplace its count.
                if (!hasHold && digits.Count == 4 && items.Count >= 2 && items.Count < 4)
                {
                    FileLogger.Log($"[battle] item-endanchor names={items.Count}");
                    column.Add(items[0].TrimEnd() + " ： " + digits[0]);
                    for (int k = 1; k < items.Count - 1; k++) column.Add(items[k]);
                    for (int b = 0; b < digits.Count - items.Count; b++) column.Add("　");
                    column.Add(items[items.Count - 1].TrimEnd() + " ： " + digits[digits.Count - 1]);
                }
                else if (!hasHold && digits.Count > items.Count && items.Count >= 2 && CountSlots(countsRaw) >= 4)
                {
                    // Ghost-slot stretch (cited 2026-10-01 open: counts read
                    // "Ｂ２２１" — slot 1 ghosted to Ｂ, grey names left no
                    // dots trace, gluing ポーション to フェニックスのお in
                    // row 2). Full-width window with more digits than names:
                    // first name opens row 1 bare (its digit ghosted), last
                    // name closes row 4 with the last digit, middles ride
                    // bare, blanks fill. Short windows keep top-fill (their
                    // digits are leading-complete, no ghost).
                    FileLogger.Log($"[battle] item-stretch names={items.Count} digits={digits.Count}");
                    column.Add(items[0]);
                    for (int k = 1; k < items.Count - 1; k++) column.Add(items[k]);
                    for (int b = 0; b < 4 - items.Count; b++) column.Add("　");
                    column.Add(items[items.Count - 1].TrimEnd() + " ： " + digits[digits.Count - 1]);
                }
                else if (!hasHold && items.Count >= 2 && items.Count < 4 && digits.Count >= 2 && GhostSlots(countsRaw) is var gs && gs.Count == 4 && gs.Count(b => b) < 4)
                {
                    // Ghost-slot map (cited 2026-10-01: counts "６４－３"
                    // with slot 3 ghosted to －; right names [P,Dk,Kd] with
                    // ねぶくろ silent — index pairing starved the column and
                    // Kd top-filled row 3). Ghosts mark unreadable rows
                    // without digits: first↔row 1 and last↔row 4 (top-open
                    // window), middles ride the earliest surviving digit
                    // slots bare, blanks fill the ghost slots. Counts attach
                    // only on real end slots (wrong counts worse than none).
                    FileLogger.Log($"[battle] item-ghostslot names={items.Count} digits={digits.Count}");
                    var placed4 = new string[4] { "　", "　", "　", "　" };
                    placed4[0] = items[0] + (gs[0] ? " ： " + digits[0] : "");
                    placed4[3] = items[items.Count - 1].TrimEnd() + (gs[3] ? " ： " + digits[digits.Count - 1] : "");
                    var usedRows = new System.Collections.Generic.HashSet<int> { 0, 3 };
                    for (int k = 1; k < items.Count - 1; k++)
                    {
                        int at = -1;
                        for (int r = 1; r < 3; r++)
                            if (gs[r] && !usedRows.Contains(r)) { at = r; break; }
                        if (at < 0)
                            for (int r = 0; r < 4; r++)
                                if (!usedRows.Contains(r)) { at = r; break; }
                        if (at >= 0) { placed4[at] = items[k]; usedRows.Add(at); }
                    }
                    foreach (var cell in placed4) column.Add(cell);
                }
                else if (hasHold && digits.Count == 4 && items.Count <= 4
                    && (Packs.PackRegistry.Current.ItemVocabulary?.Count ?? 0) > 0
                    && items.Count(s => !string.IsNullOrWhiteSpace(s)) >= 2 && !string.IsNullOrWhiteSpace(items[items.Count - 1]))
                {
                    // Full-window digit zip with holds (cited 2026-10-01:
                    // right [_,Dk,Kd] + full digits sat Kd row 3 — bare+pad
                    // can't place it, and the veto blocks every zip above).
                    // 4 real digits = every row carries a count. Minimal
                    // movement by design: leading blanks pin grey top rows
                    // (their digits orphan correctly); first content holds
                    // its slot + its positional digit; middles hold slots
                    // bare (endanchor convention — ambiguous middles never
                    // take counts); only the last content anchors to row 4
                    // + last digit (nothing trails it, so nothing below it).
                    // Anything else stays exactly where bare+pad puts it.
                    FileLogger.Log($"[battle] item-holdzip names={items.Count} digits={digits.Count}");
                    var placedH = new string[4] { "　", "　", "　", "　" };
                    int firstAt = -1, lastAt = -1;
                    for (int i = 0; i < items.Count; i++)
                        if (!string.IsNullOrWhiteSpace(items[i])) { if (firstAt < 0) firstAt = i; lastAt = i; }
                    placedH[firstAt] = items[firstAt].TrimEnd() + " ： " + digits[firstAt];
                    if (lastAt != firstAt)
                        placedH[3] = items[lastAt].TrimEnd() + " ： " + digits[3];
                    else
                        placedH[firstAt] = items[firstAt];
                    for (int i = 0; i < items.Count && i < 4; i++)
                    {
                        if (string.IsNullOrWhiteSpace(items[i])) continue;
                        if (i == firstAt || i == lastAt) continue;
                        placedH[i] = items[i];
                    }
                    foreach (var cell in placedH) column.Add(cell);
                }
                else if (hasHold && items.Count <= 4
                    && (Packs.PackRegistry.Current.ItemVocabulary?.Count ?? 0) > 0
                    && items.Count(s => !string.IsNullOrWhiteSpace(s)) >= 2
                    && string.IsNullOrWhiteSpace(items[0])
                    && !string.IsNullOrWhiteSpace(items[items.Count - 1]))
                {
                    // Leading-hold tail anchor (cited 2026-10-01: right
                    // [_,Dk,Kd] with N fully silent (no dots, partial digits
                    // or none) sat Kd row 3 — pad-insert/append can't place
                    // it, and every zip above needs fuller evidence).
                    // Minimal movement: leading blanks pin grey top rows;
                    // everything holds slots; only the last content anchors
                    // to row 4 (nothing trails it). No counts attach here
                    // (positional evidence only) — full windows use holdzip
                    // above. Bottom views never qualify (content-top-aligned,
                    // never leading-blank).
                    FileLogger.Log($"[battle] item-tailanchor names={items.Count}");
                    var placedT = new string[4] { "　", "　", "　", "　" };
                    int lastA = -1;
                    for (int i = 0; i < items.Count && i < 4; i++)
                    {
                        if (string.IsNullOrWhiteSpace(items[i])) continue;
                        lastA = i;
                    }
                    for (int i = 0; i < items.Count && i < 4; i++)
                    {
                        if (string.IsNullOrWhiteSpace(items[i])) continue;
                        if (i == lastA) placedT[3] = items[i];
                        else placedT[i] = items[i];
                    }
                    foreach (var cell in placedT) column.Add(cell);
                }
                else for (int i = 0; i < items.Count; i++)
                {
                    string it = items[i];
                    if (string.IsNullOrWhiteSpace(it)) { column.Add("　"); continue; }
                    column.Add(!hasHold && digits.Count == items.Count ? it.TrimEnd() + " ： " + digits[i] : it);
                }
                while (column.Count < 4)
                {
                    // Tail-pad appends (cited 2026-10-02: insert-after-blank
                    // pushed content down a row — [F,_,Y] became [F,_,_,Y]
                    // stranding Y row 4 instead of 3. Blanks mark real grey
                    // rows in place; missing slots are trailing empties or
                    // scroll-off content, both of which live at the END.
                    // Tailanchor/endanchor place content explicitly when
                    // evidence exists; this never invents rows).
                    column.Add("　");
                }
                // Overflow: drop blank holds before content (cited 2026-10-01:
                // [H,_,F,_,Y] truncated Y, the only real loss, to keep two
                // holds — holds are re-mintable noise, content is not).
                // Leading holds stay (row-1 grey is real): drop the first
                // blank past index 0, else the last element (flicker extra,
                // as before).
                while (column.Count > 4)
                {
                    int bi = -1;
                    for (int k = 1; k < column.Count; k++)
                        if (string.IsNullOrWhiteSpace(column[k])) { bi = k; break; }
                    if (bi >= 0) column.RemoveAt(bi);
                    else column.RemoveAt(column.Count - 1);
                }
                // Tail-pad re-anchor (cited 2026-10-03): the leading blank
                // hard-rows above pinned real top rows — content keeps its
                // order and counts, riding the window bottom instead of
                // row 1. No-op for full-content columns and for branches
                // that already placed bottom-up (order preserved either
                // way); scroll windows never carry a lead pad, so their
                // top-fill is untouched.
                if (leadingPad > 0)
                {
                    var tailContent = new System.Collections.Generic.List<string>();
                    foreach (var cell in column)
                        if (!string.IsNullOrWhiteSpace(cell)) tailContent.Add(cell);
                    if (tailContent.Count > 0 && tailContent.Count < column.Count)
                    {
                        FileLogger.Log($"[battle] item-tailpad rows={column.Count} content={tailContent.Count}");
                        column.Clear();
                        for (int b = 0; b < 4 - tailContent.Count; b++) column.Add("　");
                        column.AddRange(tailContent);
                        while (column.Count > 4) column.RemoveAt(0);
                    }
                }
                if (column.Count > 0) colItems.Add(column);
                // TEMP-DIAG (2026-10-01): per-column assembly trace. The 17:57
                // run fired [H,_,_,_] from blobs visibly containing F+Y — no
                // static path explains it. Remove once convicted.
                // Diagnostics-gated (perf batch: panel confirmed assembly long
                // ago; customers never pay for this window, devs keep it).
                if (Settings.Diag.Enabled)
                try
                {
                    var dit = new System.Collections.Generic.List<string>();
                    foreach (var it in items) dit.Add(string.IsNullOrWhiteSpace(it) ? "_" : it.Trim());
                    FileLogger.Log($"[battle] item-col rawLen={namesRaw.Length} items=[{string.Join("|", dit)}] digits={digits.Count}");
                }
                catch { }
            }
            // Scroll anchor (cited 2026-10-01: after 1-down, silent-swallowed
            // leading greys top-filled フェニックスのお into row 1 instead of
            // row 3). Columns scroll together, so estimate the window shift s
            // from BOTH columns' agreement with memory, then pin each matched
            // item to (memoryIndex - s). Unmatched items hold their slots.
            // Strict gates (unique best, score ≥ 2, contradictions penalized
            // only when both sides are canonical vocab) make no-agreement a
            // no-op: the assembly above stands.
            AnchorItemColumns(colItems, memPairs);
            var rows = new System.Collections.Generic.List<string>();
            int n = 0;
            foreach (var col in colItems) n = System.Math.Max(n, col.Count);
            for (int r = 0; r < n; r++)
            {
                string l = colItems.Count > 0 && r < colItems[0].Count ? colItems[0][r] : "";
                string rr = colItems.Count > 1 && r < colItems[1].Count ? colItems[1][r] : "";
                // Positional blanks stay: a both-blank row is a real empty
                // game row (grey unreadable both sides), and dropping it
                // collapses the rows below upward (cited 2026-10-01: two
                // holds rendered as one gap, フェニックスのお riding row 3).
                // The old drop caused that; phantom-fire safety moved to the
                // fire gate (content check), which is where firing belongs.
                rows.Add(l + "\t" + rr);
            }
            // Pack OCR fixups over assembled pairs: grey-item garbles cited per
            // game normalize here, before panel/translation/glossary.
            return rows.ConvertAll(r => ApplyZeldaOcrFixups(r));
        }
        // Item key for scroll anchoring: name part only (counts change as
        // items are used, rows do not), fixup-normalized so flicker variants
        // match their canonical memory rows.
        private static string NormItemKey(string s)
        {
            string t = s ?? "";
            int ci = t.IndexOf('：');
            if (ci >= 0) t = t.Substring(0, ci);
            ci = t.IndexOf(':');
            if (ci >= 0) t = t.Substring(0, ci);
            string normed = ApplyZeldaOcrFixups(t.Trim()).Trim();
            // Fuzzy canonical (cited paper #7 JaPOC rule: edit<2, ratio<0.30,
            // else pass through; measured on 1031 FFVI fired lines: 30 distinct
            // near-miss pairs, all correct mappings, zero wrong). Closed-domain
            // only: this helper serves item/monster assembly, never dialogue.
            // Unique-best required; empty-vocab packs no-op. Revert: delete
            // from the fuzzy line to the return.
            try
            {
                var p = Packs.PackRegistry.Current;
                var vocab = new System.Collections.Generic.HashSet<string>();
                foreach (var v in p.ItemVocabulary ?? new System.Collections.Generic.List<string>()) vocab.Add(v);
                foreach (var v in p.MonsterVocabulary ?? new System.Collections.Generic.List<string>()) vocab.Add(v);
                foreach (var v in p.BattleCommands ?? new System.Collections.Generic.List<string>()) vocab.Add(v);
                if (vocab.Count > 0 && !vocab.Contains(normed) && normed.Length >= 2)
                {
                    string? best = null;
                    int bestD = 99;
                    bool tied = false;
                    foreach (var v in vocab)
                    {
                        if (System.Math.Abs(v.Length - normed.Length) > 2) continue;
                        int d = LevenshteinDistance(normed, v);
                        if (d < bestD) { bestD = d; best = v; tied = false; }
                        else if (d == bestD) tied = true;
                    }
                    if (!tied && best != null && bestD >= 1 && bestD <= 2
                        && (double)bestD / System.Math.Max(1, normed.Length) < 0.30)
                    {
                        FileLogger.Log($"[fuzzy] \"{normed}\" -> \"{best}\" (d={bestD})");
                        return best;
                    }
                }
            }
            catch { }
            return normed;
        }
        // Returns the estimated window shift (0 when skipped): the fire gate
        // bypasses settle on confident shifts (real scrolls post instantly;
        // flicker never estimates nonzero). Field reset per frame by the
        // caller path (BuildItemPairs runs every item assembly).
        private int _lastAnchorS;
        private int AnchorItemColumns(System.Collections.Generic.List<System.Collections.Generic.List<string>> colItems, List<string>? memPairs)
        {
            try
            {
                _lastAnchorS = 0;
                if (colItems == null || colItems.Count != 2 || memPairs == null || memPairs.Count == 0) return 0;
                var memCols = new System.Collections.Generic.List<System.Collections.Generic.List<string>> { new(), new() };
                foreach (var mp in memPairs)
                {
                    SplitItemPair(mp, out string ml, out string mr);
                    memCols[0].Add(NormItemKey(ml));
                    memCols[1].Add(NormItemKey(mr));
                }
                if (memCols[0].Count != colItems[0].Count || memCols[1].Count != colItems[1].Count) return 0;
                var canon = new System.Collections.Generic.HashSet<string>(
                    Packs.PackRegistry.Current.ItemVocabulary ?? new System.Collections.Generic.List<string>());
                var curKeys = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
                foreach (var col in colItems)
                {
                    var ks = new System.Collections.Generic.List<string>();
                    foreach (var cell in col) ks.Add(NormItemKey(cell));
                    curKeys.Add(ks);
                }
                // Rigid-body gate (cited 2026-10-01: a flickered-off hold
                // scored s=+1 and dragged やまびこえんまく 4→3 with no
                // scroll — flicker mimics shifting). Both columns scroll
                // together, so a real shift ALWAYS turns over both tops;
                // if either side's first content matches memory's, s=0
                // (unique names can't re-enter). Blank tops compare by
                // first content, not first row.
                for (int side = 0; side < 2; side++)
                {
                    string? cur0 = null, mem0 = null;
                    foreach (var k in curKeys[side]) if (!string.IsNullOrWhiteSpace(k)) { cur0 = k; break; }
                    foreach (var k in memCols[side]) if (!string.IsNullOrWhiteSpace(k)) { mem0 = k; break; }
                    if (cur0 != null && mem0 != null && cur0 == mem0) return 0;
                }
                int bestS = 0, bestScore = int.MinValue, bestCount = 0;
                for (int s = -3; s <= 3; s++)
                {
                    int sc = 0;
                    for (int side = 0; side < 2; side++)
                    {
                        for (int i = 0; i < curKeys[side].Count; i++)
                        {
                            string ck = curKeys[side][i];
                            if (string.IsNullOrWhiteSpace(ck)) continue;
                            int mi = i + s;
                            if (mi < 0 || mi >= memCols[side].Count) continue;
                            string mk = memCols[side][mi];
                            if (string.IsNullOrWhiteSpace(mk)) continue;
                            // Agreements score only on canonical shapes (cited
                            // 2026-10-01: soup==soup self-agreement must never
                            // anchor positions). Single-item agreement is
                            // vacuous by construction (trueIdx falls back to
                            // obsIdx), so the ≥2 bar below stands.
                            if (ck == mk && canon.Contains(ck)) sc++;
                            else if (ck != mk && canon.Contains(ck) && canon.Contains(mk)) sc--;
                        }
                    }
                    if (sc > bestScore) { bestScore = sc; bestS = s; bestCount = 1; }
                    else if (sc == bestScore) bestCount++;
                }
                if (bestS == 0 || bestCount > 1 || bestScore < 2) return 0;
                _lastAnchorS = bestS;
                FileLogger.Log($"[battle] item-anchor s={bestS:+0;-0;+0}");
                for (int side = 0; side < 2; side++)
                {
                    var placed = new string[4];
                    var usedMem = new System.Collections.Generic.HashSet<int>();
                    var fallback = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < curKeys[side].Count && i < 4; i++)
                    {
                        string ck = curKeys[side][i];
                        if (string.IsNullOrWhiteSpace(ck)) continue;
                        int found = -1;
                        for (int mi = 0; mi < memCols[side].Count; mi++)
                            if (!usedMem.Contains(mi) && memCols[side][mi] == ck) { found = mi; break; }
                        if (found < 0) { fallback.Add(colItems[side][i]); continue; }
                        usedMem.Add(found);
                        int t = found - bestS;
                        if (t >= 0 && t < 4 && placed[t] == null) placed[t] = colItems[side][i];
                        else fallback.Add(colItems[side][i]);
                    }
                    foreach (var f in fallback)
                    {
                        int at = -1;
                        for (int k = 0; k < 4; k++) if (placed[k] == null) { at = k; break; }
                        // Fallbacks keep observed order in remaining slots;
                        // (obs slot may be taken — first-free preserves order
                        // without stacking.)
                        if (at >= 0) placed[at] = f;
                    }
                    for (int k = 0; k < 4; k++)
                        colItems[side][k] = placed[k] ?? "　";
                }
                return bestS;
            }
            catch { return 0; }
        }
        // Reduplicated grunt dust (cited 2026-10-01: ああ、ああ fired as an
        // item row). Item-path only — dialogue agreement ("yeah, yeah")
        // must never be touched, so this lives here, never in IsJunkOcr.
        private static bool IsGruntGap(string g)
        {
            string bare = "";
            foreach (char c in g ?? "")
            {
                if (c == '、' || c == '，' || c == ',' || c == '.' || c == '．' || c == '…' || c == '・' || c == ' ' || c == '　') continue;
                bare += c;
            }
            if (bare.Length < 4 || bare.Length > 8 || bare.Length % 2 != 0) return false;
            string unit = bare.Substring(0, 2);
            if (unit != "ああ" && unit != "うう" && unit != "おお" && unit != "ええ" && unit != "はあ" && unit != "はぁ" && unit != "あぁ" && unit != "うぅ" && unit != "ふう" && unit != "ふぅ") return false;
            for (int i = 2; i < bare.Length; i += 2)
                if (bare.Substring(i, 2) != unit) return false;
            return true;
        }
        // Tail-bleed set for the lastContent gate above: row separators that
        // bound content but are never content in item chunks.
        private static bool IsTailBleed(char ch)
        {
            return ch == ' ' || ch == '　' || ch == '：' || ch == ':' || ch == '．' || ch == '.' || ch == '…' || ch == '」' || ch == '』' || ch == '】' || ch == '〉' || ch == '》' || (ch >= '0' && ch <= '9') || (ch >= '０' && ch <= '９');
        }
        private static List<string> DigitChars(string s)
        {
            var out_ = new System.Collections.Generic.List<string>();
            foreach (char c in s ?? "")
                if ((c >= '0' && c <= '9') || (c >= '０' && c <= '９')) out_.Add(c.ToString());
            return out_;
        }
        // Ghost-slot count: character slots in a counts column after
        // stripping row separators (dots-runs, colons, brackets, spaces).
        // A ghosted digit (cited: Ｂ for ７ in "Ｂ２２１") still occupies
        // its slot, so slots > digits marks a full-width window with a
        // ghost — the stretch gate. Short windows have no ghost.
        private static int CountSlots(string s) => GhostSlots(s).Count;
        // Ordered slot map: true where the slot holds a digit, false for
        // ghosts (Ｂ/－-class). Parallel to DigitChars (digits[k] is the
        // k-th true slot's value).
        private static System.Collections.Generic.List<bool> GhostSlots(string s)
        {
            var out_ = new System.Collections.Generic.List<bool>();
            foreach (char c in s ?? "")
            {
                if (char.IsWhiteSpace(c)) continue;
                if (c == '．' || c == '.' || c == '…' || c == '：' || c == ':' || c == '、' || c == '。' ||
                    c == '「' || c == '」' || c == '『' || c == '』' || c == '【' || c == '】' || c == '〈' || c == '〉' || c == '《' || c == '》') continue;
                out_.Add((c >= '0' && c <= '9') || (c >= '０' && c <= '９'));
            }
            return out_;
        }
        // Item-name tokenizer: hard row breaks first (free perfect rows when
        // OCR keeps them), then longest vocab/memo match with unknown gaps as
        // remainder items. Trailing count-ish tails are stripped (counts zip
        // positionally from the counts column instead). Single-char dust drops.
        private List<string> SplitItemNames(string blob)
        {
            var out_ = new System.Collections.Generic.List<string>();
            string t = (blob ?? "").Replace("\r", "").Trim();
            if (t.Length == 0) return out_;
            var keys = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>();
            var vocabOnly = new System.Collections.Generic.HashSet<string>();
            foreach (var k in Packs.PackRegistry.Current.ItemVocabulary ?? new System.Collections.Generic.List<string>())
                if (!string.IsNullOrEmpty(k) && seen.Add(k)) { keys.Add(k); vocabOnly.Add(k); }
            // Memo keys are OUT of the splitter pool (cited 2026-10-01 soup
            // self-lock: a posted row rejoined as a key and matched itself
            // forever). Translation sim-memo is untouched — splitting is
            // pack-truth only now. Unknown games (empty vocab) keep raw
            // passthrough below via the empty-keepSet rule.
            keys.Sort((a, b) => b.Length.CompareTo(a.Length));
            // Keep-set for the canonical-or-blank policy: vocab names plus
            // cited fixup sources (which the pair stage maps to canonical).
            // Unknown games pass everything through (learning mode).
            var fixupFroms = new System.Collections.Generic.HashSet<string>();
            bool learnMode = vocabOnly.Count == 0;
            try
            {
                foreach (var (from, to) in Packs.PackRegistry.Current.OcrFixups)
                    if (!string.IsNullOrEmpty(from)) fixupFroms.Add(from);
            }
            catch { }
            // Canonical dedup (cited 2026-10-01: やまびこえんまく posted
            // twice in one column — scroll ghosts duplicate reads, but
            // inventory names are unique per column). Second+ occurrences
            // blank (slots preserved) instead of duplicating. Fixup-
            // normalized, so variants dedup against their canonical form.
            // Soup is exempt (non-canonical shapes may genuinely repeat).
            var seenCanon = new System.Collections.Generic.HashSet<string>();
            foreach (var chunk in t.Split('\n'))
            {
                // Recognition-bleed (digits/dots/colons) bounds content but is
                // never content: item names hold none of them (ー/ッ/々
                // survive; spaces join as OCR spacing noise, so エア ナイフ
                // still matches エアナイフ). Each dots-run marks unreadable
                // grey rows and holds ONE blank slot ("　") in place, so the
                // 4-row window keeps its shape; real counts zip from the
                // counts column onto their own rows. Unknown text between
                // bounds posts as remainder, as before.
                string c = chunk.Trim();
                if (c.Length == 0) continue;
                // Last real content in the chunk: dots-runs hold blanks only
                // when content follows them (leading/middle grey rows).
                // Trailing runs hold nothing — tail-pad covers the same slot,
                // and chunk tails collect box-corner artifacts ("・」") that
                // must never mint positioned rows. Trailing single-kana dust
                // (cited 2026-10-01: "．．．と" anchored a phantom 4th slot,
                // stranding やまびこえんまく in row 3) never counts as the
                // content either: lone kana with bleed neighbors is dust.
                // Single kanji still counts (月-class counters are content).
                int lastContent = -1;
                for (int k = c.Length - 1; k >= 0; k--)
                {
                    char ch = c[k];
                    bool isBleed = ch == ' ' || ch == '　' || ch == '：' || ch == ':' || ch == '．' || ch == '.' || ch == '…' || ch == '」' || ch == '』' || ch == '】' || ch == '〉' || ch == '》' || (ch >= '0' && ch <= '9') || (ch >= '０' && ch <= '９');
                    if (isBleed) continue;
                    bool isKana = (ch >= 'ぁ' && ch <= 'ん') || (ch >= 'ァ' && ch <= 'ヶ') || ch == 'ー';
                    if (isKana)
                    {
                        bool leftContent = k > 0 && !IsTailBleed(c[k - 1]);
                        bool rightContent = k < c.Length - 1 && !IsTailBleed(c[k + 1]);
                        if (!leftContent && !rightContent) continue;
                    }
                    lastContent = k; break;
                }
                int pos = 0;
                var gap = new System.Text.StringBuilder();
                bool inDots = false;
                // Gap hold bar is 9: longest cited item names run 8
                // (ナイトのこころえ, フェニックスのお), so 9+ gaps are
                // sentence soup, never items (cited 2026-10-01: 10-char
                // こういうのはあぐすり fired as an item row; 11-char
                // こういうことはあくすり before it). Shop milestone
                // revisits if longer names ever cite in.
                // ダウン dust (cited 2026-10-01: bracketed 〈ダウン〉 scroll
                // chrome read posted as an item; the brackets already split
                // as boundaries, the word drops here — item-path only).
                void FlushGap() { string g = gap.ToString().Trim(); if (g.Length < 2) { gap.Clear(); return; } if (g.Length > 9) g = "　"; else if (IsGruntGap(g) || g == "ダウン") { gap.Clear(); return; } out_.Add(g); gap.Clear(); }
                void MatchSeg(string seg)
                {
                    int p = 0;
                    while (p < seg.Length)
                    {
                        string? hit = null;
                        foreach (var k in keys)
                        {
                            if (p + k.Length <= seg.Length && string.Compare(seg, p, k, 0, k.Length, System.StringComparison.Ordinal) == 0) { hit = k; break; }
                        }
                        if (hit != null)
                        {
                            // NOTE: no seenCanon touch here — the blank pass
                            // below owns dedup for ALL entries uniformly.
                            // (Cited 2026-10-01: registering here made every
                            // direct hit re-match itself below and blank —
                            // only fixup-mediated rows survived.)
                            FlushGap(); out_.Add(hit); p += hit.Length;
                        }
                        else { gap.Append(seg[p]); p++; }
                    }
                }
                var seg = new System.Text.StringBuilder();
                void FlushSeg() { if (seg.Length > 0) { MatchSeg(seg.ToString()); seg.Clear(); } }
                while (pos < c.Length)
                {
                    char ch = c[pos];
                    // Spaces join: OCR spacing noise inside names (エア ナイフ
                    // must still match エアナイフ). Digits/colons/dots bound.
                    if (ch == ' ' || ch == '　') { pos++; continue; }
                    // Nakaguro word separators (MangaOCR emits テント・X):
                    // boundary like colons, but never a grey-row hold
                    // (unlike dots-runs).
                    if (ch == '・') { FlushSeg(); FlushGap(); inDots = false; pos++; continue; }
                    // Geometric shapes, corner brackets and fullwidth quotes
                    // (● cursor/bullet artifacts, 〈〉《》 quotes, ＂＂『』 —
                    // cited 2026-10-01: ＂』 posted as an item): boundaries
                    // like ・ above — split around them, never hold, never
                    // content.
                    if (ch == '●' || ch == '○' || ch == '◆' || ch == '◇' || ch == '■' || ch == '□' || ch == '▲' || ch == '△' || ch == '▼' || ch == '▽' || ch == '★' || ch == '☆' || ch == '〈' || ch == '〉' || ch == '《' || ch == '》' || ch == '＂' || ch == '"' || ch == '『' || ch == '』') { FlushSeg(); FlushGap(); inDots = false; pos++; continue; }
                    bool isDots = ch == '．' || ch == '.' || ch == '…';
                    bool isBleed = isDots || ch == '：' || ch == ':' || (ch >= '0' && ch <= '9') || (ch >= '０' && ch <= '９');
                    if (!isBleed) { inDots = false; seg.Append(ch); pos++; continue; }
                    FlushSeg(); FlushGap();
                    if (isDots && !inDots && pos < lastContent) out_.Add("　");
                    inDots = isDots;
                    pos++;
                }
                FlushSeg(); FlushGap();
            }
            // Fragment completion: OCR line-breaks can split one name across
            // rows ("ハイ" + "ポーション"). If a non-key gap plus its neighbor
            // form an EXACT vocab key, merge (the gap was a fragment, never an
            // item). Real items (keys themselves) never merge; blanks never
            // merge (whitespace breaks exactness). Terminates: every merge
            // shortens the list.
            for (int m = 0; m < out_.Count - 1; m++)
            {
                if (vocabOnly.Contains(out_[m])) continue;
                if (vocabOnly.Contains(out_[m] + out_[m + 1])) { out_[m] = out_[m] + out_[m + 1]; out_.RemoveAt(m + 1); m--; }
            }
            // Canonical-or-blank (cited 2026-10-01, user-endorsed): the full
            // inventory is known, so item rows post only cited shapes — vocab
            // hits and fixup sources (mapped canonical at pair stage); every
            // other remainder blanks instead of posting soup (コナイフ,
            // プラン, クリフ-class flicker all auto-resolve). Runs AFTER
            // fragment completion so split names still merge. Unknown games
            // (learnMode) keep raw passthrough for discovery; shop milestone
            // revisits if shop stock needs different rules.
            if (!learnMode)
            {
                for (int m = 0; m < out_.Count; m++)
                {
                    string o = out_[m];
                    if (string.IsNullOrWhiteSpace(o)) continue;
                    if (vocabOnly.Contains(o))
                    {
                        if (!seenCanon.Add(NormItemKey(o))) out_[m] = "　";
                        continue;
                    }
                    string key = o.Trim();
                    key = key.Trim('、', '。', '，', ',', '.', '．', '…', '・', '「', '」', '『', '』', '【', '】', '〈', '〉', '《', '》', '」');
                    if (vocabOnly.Contains(key) || fixupFroms.Contains(key) || fixupFroms.Contains(o))
                    {
                        string nk = NormItemKey(o);
                        if (vocabOnly.Contains(nk) && !seenCanon.Add(nk)) out_[m] = "　";
                        continue;
                    }
                    out_[m] = "　";
                }
            }
            return out_;
        }
        private static List<string> AttachMonsterCounts(List<string> rows)
        {
            // Leading digits-only rows merge into the FOLLOWING name row
            // (cited 2026-10-01: bare "１" posted as a monster; OCR order
            // flips put the count first). Same info, one row — the trailing
            // behavior below is unchanged.
            var out_ = new List<string>(rows.Count);
            string pending = "";
            foreach (var r in rows)
            {
                SplitRowDigits(r, out string name, out string digits);
                if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(digits) && out_.Count == 0)
                {
                    pending = pending.Length == 0 ? digits : pending + " " + digits;
                    continue;
                }
                if (pending.Length > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    SplitRowDigits(r, out _, out string ownDigits);
                    if (string.IsNullOrWhiteSpace(ownDigits)) { out_.Add(r.TrimEnd() + "　" + pending); pending = ""; continue; }
                }
                if (pending.Length > 0) { out_.Add(pending); pending = ""; }
                if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(digits) && out_.Count > 0)
                {
                    SplitRowDigits(out_[^1], out string prevName, out _);
                    if (!string.IsNullOrWhiteSpace(prevName)) { out_[^1] = out_[^1].TrimEnd() + "　" + digits; continue; }
                }
                out_.Add(r);
            }
            if (pending.Length > 0) out_.Add(pending);
            return out_;
        }
        // Battle context: the item-box class is trusted only mid-battle, so
        // field or shop colon lists never route into the battle frame.
        private bool InBattleContext()
        {
            return _battleFiredSig != "" || _battleCooldown > 0 || _battleMonsterSticky.Count > 0;
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
            var pubSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Whole-frame fan-out: top + all rows start together, wall time is
                // the slowest row, not the sum. Results land back in slot order.
                var topFull = (slots.Top.Length > 0 && slots.Top == _lastBattleTopJa) ? Task.FromResult(_lastBattleTopEn)
                    : (string.IsNullOrWhiteSpace(slots.Top) ? Task.FromResult("") : PrefetchOrTranslateAsync(slots.Top, () => _translationManager.TranslateAsync(slots.Top)));
                var monFull = TranslateBattleRowsAsync(slots.Monster, _lastBattleMonJa, _lastBattleMonEn, l => TranslateMenuJaRowAsync(l), true);
                var actFull = TranslateBattleRowsAsync(slots.Action, _lastBattleActJa, _lastBattleActEn, l => TranslateMenuJaRowAsync(l));
                var rightFull = TranslateBattleRowsAsync(slots.Right, _lastBattleRightJa, _lastBattleRightEn, n => _translationManager.TranslateAsync(n), true);
                var itemFull = TranslateBattleRowsAsync(slots.Item, _lastBattleItemJa, _lastBattleItemEn, TranslateItemRowAsync, true, true);
                var weaponFull = TranslateBattleRowsAsync(slots.Weapon, _lastBattleWeaponJa, _lastBattleWeaponEn, TranslateWeaponRowAsync, false);
                // Fast leg: best-effort inside the state's lifetime. All six
                // start before the first await, so the frame costs ~1.2s max.
                var topFastTask = WithTimeout(topFull, slots.Top, BattleFastMs);
                var monFastTask = BestEffortRowsAsync(monFull, slots.Monster);
                var actFastTask = BestEffortRowsAsync(actFull, slots.Action);
                var rightFastTask = BestEffortRowsAsync(rightFull, slots.Right);
                var itemFastTask = BestEffortRowsAsync(itemFull, slots.Item);
                var weaponFastTask = BestEffortRowsAsync(weaponFull, slots.Weapon);
                await Task.WhenAll(topFastTask, monFastTask, actFastTask, rightFastTask, itemFastTask, weaponFastTask);
                string topEn = await topFastTask;
                var monEn = await monFastTask;
                var actEn = await actFastTask;
                var rightEn = await rightFastTask;
                var itemEn = await itemFastTask;
                var weaponEn = await weaponFastTask;
                // Stale-drop: translation outlived the state it captured. The
                // newest state always has its own fast fire ~1s behind, so
                // dropping wrong beats posting wrong.
                if (_battleFiredSig != sig || _battlePrevSig != sig) { FileLogger.Log($"[battle] stale publish dropped fire={SigTag(sig)} cur={SigTag(_battleFiredSig)}"); return; }
                FileLogger.Log($"[battle] frame publish top={slots.Top.Length}ch mon={slots.Monster.Count} act={slots.Action.Count} right={slots.Right.Count} item={slots.Item.Count} weapon={slots.Weapon.Count} fast sig={SigTag(sig)}");
                CostPubSample(pubSw.Elapsed.TotalMilliseconds);
                BattleFramePublished?.Invoke(slots.Top, topEn, slots.Monster, monEn, slots.Action, actEn, slots.Right, rightEn, slots.Item, itemEn, slots.Weapon, weaponEn);
                // Refresh leg: real EN when the slow rows land, if still current.
                // Corpus records the full pair only (never JA-verbatim fallbacks).
                _ = RefreshBattleWhenReadyAsync(sig, slots, topFull, monFull, actFull, rightFull, itemFull, weaponFull);
            }
            catch (Exception ex) { FileLogger.Log($"[battle] ERROR: {ex.Message}"); }
        }
        private async Task RefreshBattleWhenReadyAsync(string sig, BattleFrame.BattleSlots slots, Task<string> topFull, Task<List<string>> monFull, Task<List<string>> actFull, Task<List<string>> rightFull, Task<List<string>> itemFull, Task<List<string>> weaponFull)
        {
            try
            {
                await Task.WhenAll(topFull, monFull, actFull, rightFull, itemFull, weaponFull);
                string topEn = await topFull;
                var monEn = await monFull;
                var actEn = await actFull;
                var rightEn = await rightFull;
                var itemEn = await itemFull;
                var weaponEn = await weaponFull;
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
                _lastBattleItemJa = new List<string>(slots.Item);
                _lastBattleItemEn = new List<string>(itemEn);
                _lastBattleWeaponJa = new List<string>(slots.Weapon);
                _lastBattleWeaponEn = new List<string>(weaponEn);
                // Silent when superseded: a newer fire owns the panel, and the
                // fast leg already posted something truthful. No drop-spam.
                if (_battleFiredSig != sig || _battlePrevSig != sig) return;
                FileLogger.Log($"[battle] frame publish top={slots.Top.Length}ch mon={slots.Monster.Count} act={slots.Action.Count} right={slots.Right.Count} item={slots.Item.Count} weapon={slots.Weapon.Count} full sig={SigTag(sig)}");
                try { CorpusRecordBubble("battle-frame:fire", slots.Top + "\n" + string.Join("\n", slots.Monster) + "\n" + string.Join("\n", slots.Action) + "\n" + string.Join("\n", slots.Right) + "\n" + string.Join("\n", slots.Item) + "\n" + string.Join("\n", slots.Weapon), topEn + "\n" + string.Join("\n", monEn) + "\n" + string.Join("\n", actEn) + "\n" + string.Join("\n", rightEn) + "\n" + string.Join("\n", itemEn) + "\n" + string.Join("\n", weaponEn)); } catch { }
                BattleFramePublished?.Invoke(slots.Top, topEn, slots.Monster, monEn, slots.Action, actEn, slots.Right, rightEn, slots.Item, itemEn, slots.Weapon, weaponEn);
            }
            catch { }
        }

        // Menu Latin-token split (universal machinery, N3): JA rows with embedded fullwidth-Latin
        // art (ＥｏｌｌＰｈｏＥＮＴ-class) translate the JA part only; art reattaches verbatim,
        // never burns CAT. Allowlist/coords/button shapes stay inline. JA bubble text untouched.
        // Pack battle glossary: known UI chrome (commands, items, equip
        // labels) translates instantly exact-match — no CAT/DeepL burn, no
        // JA-verbatim flicker while the refresh leg crawls. Trailing colons
        // strip (pair sides arrive "name ："). Unknown strings fall through
        // to the live path untouched.
        private static bool TryBattleGlossary(string t, out string en)
        {
            en = "";
            try
            {
                string key = (t ?? "").Trim().TrimEnd(' ', '　', '：', ':').Trim();
                if (key.Length == 0) return false;
                var glo = Packs.PackRegistry.Current.BattleGlossary;
                if (glo == null) return false;
                foreach (var (ja, e) in glo)
                    if (!string.IsNullOrEmpty(ja) && key.Equals(ja, System.StringComparison.Ordinal)) { en = e; return true; }
            }
            catch { }
            return false;
        }
        private async Task<string> TranslateMenuJaRowAsync(string t)
        {
            if (TryBattleGlossary(t, out var g)) return g;
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
            var pubSw = System.Diagnostics.Stopwatch.StartNew();
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
                CostPubSample(pubSw.Elapsed.TotalMilliseconds);
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
                // Title purity, update path (same cited flaw as the publish
                // path above): keep only the marker row in Zelda TITLEs.
                if (kind2 == "TITLE" && Packs.PackRegistry.Current.GameId == Packs.PackRegistry.ZeldaGameId)
                {
                    var keep = clean.Select((r, i) => (r, i)).Where(x => (x.r.t ?? "").Contains(Packs.PackRegistry.Current.TitleMarker)).Select(x => x.i).ToList();
                    if (keep.Count > 0)
                    {
                        clean = keep.Select(i => clean[i]).ToList();
                        en = keep.Select(i => en[i]).ToList();
                        newJa = string.Join("\n", clean.ConvertAll(r => r.t));
                        newEn = string.Join("\n", en);
                    }
                }
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
        // Temporal derivative v0 (vision item 4): downscaled grayscale
        // diff vs the previous captured frame. Returns "diff=F band=T-B
        // low=S kind=K": F = changed fraction (|d|>12), T-B = changed
        // row band, S = share of change in the text zone (ny>0.65),
        // K = static (F<0.002) | lowband (S>0.7, text-zone change:
        // typewriter/scroll/dialogue) | spread (UI-wide change:
        // transitions, flashes). Reference updates every captured frame
        // (even skips) so the derivative is always one frame apart.
        // Classify-only: no firing behavior reads this yet.
        private byte[]? _prevSmall;
        private int _prevSmallW, _prevSmallH;
        private string ComputeFrameDiff(Bitmap bitmap)
        {
            try
            {
                int tw = 160;
                int th = Math.Max(1, bitmap.Height * tw / Math.Max(1, bitmap.Width));
                byte[] cur;
                using (var thumb = new Bitmap(bitmap, new Size(tw, th)))
                {
                    var rect = new Rectangle(0, 0, tw, th);
                    var data = thumb.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    try
                    {
                        cur = new byte[tw * th];
                        int stride = Math.Abs(data.Stride);
                        var row = new byte[stride];
                        for (int y = 0; y < th; y++)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, stride);
                            for (int x = 0; x < tw; x++)
                                cur[y * tw + x] = (byte)((row[x * 3 + 2] * 77 + row[x * 3 + 1] * 150 + row[x * 3] * 29) >> 8);
                        }
                    }
                    finally { thumb.UnlockBits(data); }
                }
                string res = "diff=noref";
                if (_prevSmall is not null && _prevSmall.Length == cur.Length && _prevSmallW == tw && _prevSmallH == th)
                {
                    int changed = 0, low = 0, top = th, bot = -1;
                    for (int y = 0; y < th; y++)
                    {
                        bool rowHit = false;
                        for (int x = 0; x < tw; x++)
                        {
                            int d = cur[y * tw + x] - _prevSmall[y * tw + x];
                            if (d < -12 || d > 12)
                            {
                                changed++;
                                rowHit = true;
                                if ((double)y / th > 0.65) low++;
                            }
                        }
                        if (rowHit) { if (y < top) top = y; bot = y; }
                    }
                    double frac = (double)changed / cur.Length;
                    _lastDiffFrac = frac;
                    string kind = frac < 0.002 ? "static" : ((double)low / Math.Max(1, changed) > 0.7 ? "lowband" : "spread");
                    res = bot < 0 ? "diff=0.000 band=-- low=0.00 kind=static"
                        : $"diff={frac:0.000} band={(double)top / th:0.00}-{(double)bot / th:0.00} low={(double)low / Math.Max(1, changed):0.00} kind={kind}";
                }
                _prevSmall = cur; _prevSmallW = tw; _prevSmallH = th;
                return res;
            }
            catch { _prevSmall = null; _lastDiffFrac = 1.0; return "diff=err"; }
        }
        // Pixel mean of two same-size captures (A1 trial). 32bpp scratch,
        // alpha forced opaque — game captures carry no alpha. Null on any
        // mismatch/failure: caller falls back to the plain frame.
        private static Bitmap? AverageBitmaps(Bitmap a, Bitmap b)
        {
            if (a.Size != b.Size || a.Width <= 0 || a.Height <= 0) return null;
            var out_ = new Bitmap(a.Width, a.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            {
                var rect = new Rectangle(0, 0, a.Width, a.Height);
                // ReadOnly locks with an explicit format convert on lock —
                // no scratch copies needed.
                var da = a.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var db = b.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var dd = out_.LockBits(rect, ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = Math.Abs(da.Stride);
                    var ra = new byte[stride];
                    var rb = new byte[stride];
                    var rd = new byte[stride];
                    for (int y = 0; y < a.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(da.Scan0 + y * da.Stride, ra, 0, stride);
                        System.Runtime.InteropServices.Marshal.Copy(db.Scan0 + y * db.Stride, rb, 0, stride);
                        for (int i = 0; i < stride; i += 4)
                        {
                            rd[i] = (byte)((ra[i] + rb[i]) >> 1);
                            rd[i + 1] = (byte)((ra[i + 1] + rb[i + 1]) >> 1);
                            rd[i + 2] = (byte)((ra[i + 2] + rb[i + 2]) >> 1);
                            rd[i + 3] = 255;
                        }
                        System.Runtime.InteropServices.Marshal.Copy(rd, 0, dd.Scan0 + y * dd.Stride, stride);
                    }
                }
                finally { a.UnlockBits(da); b.UnlockBits(db); out_.UnlockBits(dd); }
            }
            return out_;
        }
    }
}