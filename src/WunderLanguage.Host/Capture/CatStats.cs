using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WunderLanguage.Host.Ocr;

namespace WunderLanguage.Host.Capture
{
    // C1 category measurement pass (ROADMAP C1: offline, corpus-only, NO
    // behavior change). Answers "how many junk fires came from mis-
    // categorized screens" and produces the numbers that decide Option 1
    // (display tagging) vs Option 2 (routing-level).
    // Method: per corpus, feed the recorded lines through the REAL
    // pipeline via the proven SimReplay seam (ProcessOcrObservationAsync)
    // and sample REAL tracker state after every step:
    //   menu-stable     = _menuSignature non-empty AND _menuStableCount>=2
    //                     (mint-eligible: a real menu/title/decision screen)
    //   menu-transient  = signature but count<2 (moving multi-row JA text
    //                     like an intro crawl - menu-SHAPED, dialogue
    //                     publishes there are legitimate)
    //   battle          = BattleFrame.Classify(...) > 0, the SAME static
    //                     detector the live loop calls. Called here for
    //                     ALL packs because the in-engine UpdateBattleFrame
    //                     is FFVI-gated by design (Zelda repair): non-FFVI
    //                     battle-shaped frames fall through to the dialogue
    //                     path - the leak class C1 counts.
    // Two truth levels per publish:
    //   sim attempt     = dialogue fire/update captured in sim (PRE-guard:
    //                     live-funnel-only guards never run in the seam)
    //   publish truth   = RECORDED live dialogue bubbles joined to the
    //                     sampled frame category at their live loop stamp
    //                     = what the user actually saw on non-ingame
    //                     screens. Junk = publish on stable-menu or battle.
    // Exit 0 (observe-only). Usage: --catstats <corpus-file-or-dir>
    // (one process, all files; run with the app closed - FileLogger wipes
    // app.log per process).
    // Revert: delete this file + the --catstats exemption in
    // SingleInstanceGuard.
    internal static class CatStats
    {
        [ModuleInitializer]
        internal static void Init()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                bool has = false;
                string path = "";
                for (int i = 0; i < args.Length; i++)
                {
                    if (!string.Equals(args[i], "--catstats", StringComparison.OrdinalIgnoreCase)) continue;
                    has = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) path = args[i + 1];
                }
                if (!has) return;
                if (string.IsNullOrWhiteSpace(path))
                {
                    FileLogger.Log("[catstats] usage: --catstats <corpus-file-or-dir>");
                    Environment.Exit(2);
                }
                int rc = 1;
                try { rc = Run(path); }
                catch (Exception ex) { FileLogger.Log("[catstats] FATAL " + ex.Message); }
                Environment.Exit(rc);
            }
            catch { }
        }

        private sealed class Agg
        {
            public int corpora, fed, stableLoops, transientLoops, battleLoops, bothLoops, ingameLoops;
            public int fires, fStable, fTransient, fBattle, fBoth, fLeak, updates, uStable, uTransient, uBattle, uBoth;
            public int recPub, recPubStable, recPubTransient, recPubBattle, recPubBoth, recUpdPub, recUpdLeak;
            public int recMenuT, recMenuD, recMenuM, recMenuUpd, recBattle, recDlgDec, recDlgFire, recDlgUpd;
        }

        private static int Num(JsonElement e)
        {
            try
            {
                if (e.ValueKind == JsonValueKind.Number) { try { return e.GetInt32(); } catch { return (int)e.GetDouble(); } }
                if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out int v)) return v;
            }
            catch { }
            return 0;
        }

        private static double NumD(JsonElement e)
        {
            try
            {
                if (e.ValueKind == JsonValueKind.Number) return e.GetDouble();
                if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), out double v)) return v;
            }
            catch { }
            return 0.0;
        }

        // Verbatim parse shape from SimReplay (recorded block fields
        // t/x/y/w/h/c/n/s/d; engine sees the same OcrBlock list live).
        private static List<OcrBlock> ParseBlocks(JsonElement root)
        {
            var out_ = new List<OcrBlock>();
            try
            {
                if (!root.TryGetProperty("blocks", out var bs) || bs.ValueKind != JsonValueKind.Array) return out_;
                foreach (var b in bs.EnumerateArray())
                {
                    try
                    {
                        string t = b.TryGetProperty("t", out var te) ? (te.GetString() ?? "") : "";
                        int x = b.TryGetProperty("x", out var xe) ? Num(xe) : 0;
                        int y = b.TryGetProperty("y", out var ye) ? Num(ye) : 0;
                        int w = b.TryGetProperty("w", out var we) ? Num(we) : 0;
                        int h = b.TryGetProperty("h", out var he) ? Num(he) : 0;
                        double c = b.TryGetProperty("c", out var ce) ? NumD(ce) : 1.0;
                        int n = b.TryGetProperty("n", out var ne) ? Num(ne) : 1;
                        string s = b.TryGetProperty("s", out var se) ? (se.GetString() ?? "detector") : "detector";
                        double d = b.TryGetProperty("d", out var de) ? NumD(de) : 0.0;
                        out_.Add(new OcrBlock(t, x, y, w, h, (double)w * h, c, n, s, d));
                    }
                    catch { }
                }
            }
            catch { }
            return out_;
        }

        private static IEnumerable<string> ReadCorpusLines(string path)
        {
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var fs = File.OpenRead(path);
                using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
                using var sr = new StreamReader(gz);
                string? line;
                while ((line = sr.ReadLine()) != null)
                    yield return line;
            }
            else
            {
                foreach (var line in File.ReadLines(path))
                    yield return line;
            }
        }

        public static int Run(string path)
        {
            // Reflection cache: read-only samples of private tracker state
            // (zero engine edits; SimReplay-style internals are used where
            // they already exist - _simMode/_simLoop/_simPublished).
            var fMenuSig = typeof(CaptureController).GetField("_menuSignature", BindingFlags.NonPublic | BindingFlags.Instance);
            var fMenuStable = typeof(CaptureController).GetField("_menuStableCount", BindingFlags.NonPublic | BindingFlags.Instance);
            var fItemOpen = typeof(CaptureController).GetField("_itemBoxOpen", BindingFlags.NonPublic | BindingFlags.Instance);
            var fBattleCd = typeof(CaptureController).GetField("_battleCooldown", BindingFlags.NonPublic | BindingFlags.Instance);
            var mHasJa = typeof(CaptureController).GetMethod("HasJapanese", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            var hasJa = mHasJa != null ? (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), mHasJa) : (_ => false);

            var files = new List<string>();
            if (Directory.Exists(path))
            {
                foreach (var f in Directory.GetFiles(path, "*.jsonl*")) files.Add(f);
                files.Sort(StringComparer.Ordinal);
            }
            else if (File.Exists(path)) files.Add(path);
            else { FileLogger.Log("[catstats] path not found: " + path); return 2; }
            FileLogger.Log($"[catstats] start files={files.Count} path={path}");

            var prevPack = Packs.PackRegistry.Current;
            var byGame = new Dictionary<string, Agg>(StringComparer.Ordinal);
            var leakKinds = new Dictionary<string, int>(StringComparer.Ordinal);
            var topLeak = new List<(string name, int recJunk, int simLeak, int fires, int stable, int battle)>();
            long tFed = 0, tStable = 0, tTransient = 0, tBattle = 0, tBoth = 0, tIngame = 0, tBTrack = 0;
            long tFires = 0, tFIngame = 0, tFStable = 0, tFTransient = 0, tFBattle = 0, tFBoth = 0;
            long tUpd = 0, tUIngame = 0, tUStable = 0, tUTransient = 0, tUBattle = 0, tUBoth = 0;
            long rMenuT = 0, rMenuD = 0, rMenuM = 0, rMenuUpd = 0, rBattle = 0, rDd = 0, rDf = 0, rDu = 0;
            long rPub = 0, rPubStable = 0, rPubTransient = 0, rPubBattle = 0, rPubBoth = 0, rPubNoFrame = 0, rPubUnstamped = 0;
            long rUpdPub = 0, rUpdStable = 0, rUpdTransient = 0, rUpdBattle = 0, rUpdBoth = 0;
            long tBProbe = 0;
            int corporaDone = 0, corporaErr = 0;

            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                try
                {
                    // Parse pass (mirrors SimReplay v4): feed list + recorded
                    // live-truth bubble census. Frame dims banked per-line.
                    var fed = new List<(int loop, string raw, int cw, int tw, int fw, int fh, List<OcrBlock> blocks, string ja, long wt)>();
                    int recDd = 0, recDf = 0, recDu = 0, recMenuT = 0, recMenuD = 0, recMenuM = 0, recMenuUpd = 0, recBattle = 0;
                    // Live-truth join: recorded dialogue publishes with their
                    // live loop stamps, later matched against the sampled
                    // frame category - publish-level junk (what live actually
                    // posted on non-ingame frames), not sim attempts.
                    var recDlgPub = new List<(int loop, bool isUpd)>();
                    foreach (var line in ReadCorpusLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        JsonDocument doc;
                        try { doc = JsonDocument.Parse(line); }
                        catch { continue; }
                        using (doc)
                        {
                            var root = doc.RootElement;
                            if (!root.TryGetProperty("type", out var ty)) continue;
                            string t = ty.GetString() ?? "";
                            if (t == "ocr")
                            {
                                string ex = root.TryGetProperty("extracted", out var e) ? (e.GetString() ?? "") : "";
                                string raw = root.TryGetProperty("raw", out var r2) ? (r2.GetString() ?? "") : "";
                                int loop = root.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : fed.Count;
                                int cw = root.TryGetProperty("cw", out var cwe) ? Num(cwe) : 0;
                                int tw = root.TryGetProperty("tw", out var twe) ? Num(twe) : 0;
                                int fw = root.TryGetProperty("fw", out var fwe) ? Num(fwe) : 0;
                                int fh = root.TryGetProperty("fh", out var fhe) ? Num(fhe) : 0;
                                // Item 5: banked wall-clock (session ms) feeds the
                                // invention-zone window so replay windows mean the
                                // same seconds live does (tick-count would collapse
                                // the whole replayed session into <1s and zone
                                // everything).
                                long wt = root.TryGetProperty("wt", out var wte) && wte.ValueKind == JsonValueKind.Number ? wte.GetInt64() : 0;
                                fed.Add((loop, raw, cw, tw, fw, fh, ParseBlocks(root), ex, wt));
                            }
                            else if (t == "bubble")
                            {
                                string kind = root.TryGetProperty("kind", out var k) ? (k.GetString() ?? "") : "";
                                int blog = -1;
                                if (root.TryGetProperty("loop", out var bl) && bl.ValueKind == JsonValueKind.Number) blog = bl.GetInt32();
                                if (kind.StartsWith("dialogue-decision:", StringComparison.Ordinal)) { recDd++; recDlgPub.Add((blog, false)); }
                                else if (kind == "dialogue-fire") { recDf++; recDlgPub.Add((blog, false)); }
                                else if (kind == "dialogue-update") { recDu++; recDlgPub.Add((blog, true)); }
                                else if (kind.StartsWith("menu-fire:", StringComparison.Ordinal))
                                {
                                    string suf = kind.Substring("menu-fire:".Length);
                                    if (suf == "TITLE") recMenuT++;
                                    else if (suf == "DECISION") recMenuD++;
                                    else recMenuM++;
                                }
                                else if (kind.StartsWith("menu-update:", StringComparison.Ordinal)) recMenuUpd++;
                                else if (kind.StartsWith("battle-frame", StringComparison.Ordinal)) recBattle++;
                            }
                        }
                    }
                    int sesW = 0, sesH = 0;
                    foreach (var f in fed)
                    {
                        if (f.fw > sesW) sesW = f.fw;
                        if (f.fh > sesH) sesH = f.fh;
                    }

                    // Pack select: same filename resolve the session hook
                    // uses; unmatched -> universal (faithful unknown-game
                    // path - measured cold, per-corpus isolation).
                    string u = name.ToUpperInvariant();
                    string pack = "universal";
                    try
                    {
                        if (u.Contains("FFVI") || u.Contains("FANTASY_VI")) { Packs.PackRegistry.SetCurrent(Packs.FFVI.FfviPackInstance.Shared); pack = "ffvi"; }
                        else if (u.Contains("ZELDA") || u.Contains("DENSETSU")) { Packs.PackRegistry.SetCurrent(Packs.Zelda.ZeldaPackInstance.Shared); pack = "zelda"; }
                        else if (u.Contains("PAPERMARIO") || u.Contains("ペーパーマリオ") || u.Contains("G8MJ01")) { Packs.PackRegistry.SetCurrent(Packs.PaperMario.PaperMarioPackInstance.Shared); pack = "papermario"; }
                        else Packs.PackRegistry.SetCurrent(Packs.UniversalPackInstance.Shared);
                    }
                    catch { }
                    var weaponLabels = Packs.PackRegistry.Current.WeaponLabels;

                    // Feed pass: real pipeline, real tracker state sampled
                    // after every step. Fire/update loop -> frame category.
                    var ctl = new CaptureController { _simMode = true };
                    try { ctl.SetFrameDimsForSim(sesW, sesH); } catch { }
                    var token = new System.Threading.CancellationToken(false);
                    // v3 category: battle / stable-menu / transient-menu /
                    // ingame (priority order for the joint buckets).
                    var catAtLoop = new Dictionary<int, (bool battle, bool stable, bool transient)>();
                    int stableLoops = 0, transientLoops = 0, battleLoops = 0, bothLoops = 0, ingameLoops = 0, bTrackLoops = 0;
                    // C2 Part-2 probe: on non-FFVI packs the label-driven
                    // Classify is blind (universal battleLoops=0 across 244
                    // corpora - no weapon labels = no battle rows). Re-run
                    // the SAME detector with FFVI's labels to measure
                    // unknown-game battle-shaped frames at all - with C1's
                    // false-positive caveat on file (date-stamped text
                    // screens read as battle rows).
                    var ffviLabels = Packs.FFVI.FfviPack.WeaponLabels;
                    int bFfviProbe = 0;
                    foreach (var (loop, raw, cw, tw, fw, fh, blocks, ja, wt) in fed)
                    {
                        try
                        {
                            ctl.SetOcrContextForSim(raw, cw, tw);
                            ctl._simLoop = loop;
                            ctl._simZoneNowMs = wt > 0 ? wt : loop * 400L;   // item 5 replay clock (see ZoneNowMs)
                            ctl.ProcessOcrObservationAsync(loop, ja, blocks, token).GetAwaiter().GetResult();
                        }
                        catch (Exception fx) { FileLogger.Log($"[catstats] corpus={name} loop={loop} FEED-ERROR: {fx.Message}"); break; }
                        string menuSig = "";
                        try { menuSig = (fMenuSig?.GetValue(ctl) as string) ?? ""; } catch { }
                        int menuStable = 0;
                        try { menuStable = fMenuStable?.GetValue(ctl) is int ms ? ms : 0; } catch { }
                        bool itemOpen = false;
                        try { itemOpen = fItemOpen?.GetValue(ctl) is bool ob && ob; } catch { }
                        int bclass = 0;
                        if (blocks.Count > 0)
                        {
                            try { bclass = BattleFrame.Classify(blocks, sesW, sesH, hasJa, weaponLabels, out _, itemOpen); }
                            catch { bclass = 0; }
                        }
                        if (pack != "ffvi" && blocks.Count > 0)
                        {
                            try { if (BattleFrame.Classify(blocks, sesW, sesH, hasJa, ffviLabels, out _, itemOpen) > 0) bFfviProbe++; }
                            catch { }
                        }
                        bool menu = menuSig.Length > 0;
                        bool stable = menu && menuStable >= 2;
                        bool transient = menu && menuStable < 2;
                        bool battle = bclass > 0;
                        catAtLoop[loop] = (battle, stable, transient);
                        if (battle && (stable || transient)) bothLoops++;
                        else if (battle) battleLoops++;
                        else if (stable) stableLoops++;
                        else if (transient) transientLoops++;
                        else ingameLoops++;
                        try { if (fBattleCd?.GetValue(ctl) is int cd && cd > 0) bTrackLoops++; } catch { }
                    }
                    try { Packs.PackRegistry.SetCurrent(prevPack); } catch { }

                    // Attempt-level (pre-guard) cross-tab.
                    int fires = 0, fIngame = 0, fStable = 0, fTransient = 0, fBattle = 0, fBoth = 0;
                    int updates = 0, uIngame = 0, uStable = 0, uTransient = 0, uBattle = 0, uBoth = 0;
                    foreach (var s in ctl._simPublished)
                    {
                        string[] p = s.Split(new char[] { ':' }, 4);
                        if (p.Length < 4) continue;
                        if (!int.TryParse(p[1], out int lp)) lp = -1;
                        catAtLoop.TryGetValue(lp, out var cat);
                        bool isFire = p[0] == "fire";
                        if (isFire) fires++; else updates++;
                        if (cat.battle && (cat.stable || cat.transient)) { if (isFire) fBoth++; else uBoth++; }
                        else if (cat.battle) { if (isFire) fBattle++; else uBattle++; }
                        else if (cat.stable) { if (isFire) fStable++; else uStable++; }
                        else if (cat.transient) { if (isFire) fTransient++; else uTransient++; }
                        else { if (isFire) fIngame++; else uIngame++; }
                        if (cat.battle || cat.stable)
                        {
                            string lk = (isFire ? "" : "update:") + p[2];
                            leakKinds[lk] = leakKinds.TryGetValue(lk, out int lc) ? lc + 1 : 1;
                        }
                    }
                    int fLeak = fStable + fBattle + fBoth;   // junk-class attempts
                    int uLeak = uStable + uBattle + uBoth;

                    // Publish-level truth join: recorded (live) dialogue
                    // publishes mapped to the sampled frame category of
                    // their live loop. Junk = stable-menu or battle;
                    // transient-menu publishes are legitimate dialogue on
                    // menu-SHAPED text (crawls) and counted apart.
                    int recPub = 0, recPubStable = 0, recPubTransient = 0, recPubBattle = 0, recPubBoth = 0, recPubNoFrame = 0, recPubUnstamped = 0;
                    int recUpdPub = 0, recUpdStable = 0, recUpdTransient = 0, recUpdBattle = 0, recUpdBoth = 0;
                    foreach (var (lp, isUpd) in recDlgPub)
                    {
                        if (lp < 0) { recPubUnstamped++; continue; }
                        if (!catAtLoop.TryGetValue(lp, out var cat)) { recPubNoFrame++; continue; }
                        if (isUpd)
                        {
                            recUpdPub++;
                            if (cat.battle && (cat.stable || cat.transient)) recUpdBoth++;
                            else if (cat.battle) recUpdBattle++;
                            else if (cat.stable) recUpdStable++;
                            else if (cat.transient) recUpdTransient++;
                        }
                        else
                        {
                            recPub++;
                            if (cat.battle && (cat.stable || cat.transient)) recPubBoth++;
                            else if (cat.battle) recPubBattle++;
                            else if (cat.stable) recPubStable++;
                            else if (cat.transient) recPubTransient++;
                        }
                    }
                    int recJunk = recPubStable + recPubBattle + recPubBoth;

                    // Aggregations.
                    rMenuT += recMenuT; rMenuD += recMenuD; rMenuM += recMenuM; rMenuUpd += recMenuUpd;
                    rBattle += recBattle; rDd += recDd; rDf += recDf; rDu += recDu;
                    rPub += recPub; rPubStable += recPubStable; rPubTransient += recPubTransient;
                    rPubBattle += recPubBattle; rPubBoth += recPubBoth;
                    rPubNoFrame += recPubNoFrame; rPubUnstamped += recPubUnstamped;
                    rUpdPub += recUpdPub; rUpdStable += recUpdStable; rUpdTransient += recUpdTransient;
                    rUpdBattle += recUpdBattle; rUpdBoth += recUpdBoth;
                    tFed += fed.Count; tStable += stableLoops; tTransient += transientLoops;
                    tBattle += battleLoops; tBoth += bothLoops; tIngame += ingameLoops; tBTrack += bTrackLoops;
                    tFires += fires; tFIngame += fIngame; tFStable += fStable; tFTransient += fTransient;
                    tFBattle += fBattle; tFBoth += fBoth;
                    tUpd += updates; tUIngame += uIngame; tUStable += uStable; tUTransient += uTransient;
                    tUBattle += uBattle; tUBoth += uBoth;
                    tBProbe += bFfviProbe;
                    corporaDone++;

                    if (!byGame.TryGetValue(pack, out var ag)) { ag = new Agg(); byGame[pack] = ag; }
                    ag.corpora++; ag.fed += fed.Count;
                    ag.stableLoops += stableLoops; ag.transientLoops += transientLoops;
                    ag.battleLoops += battleLoops; ag.bothLoops += bothLoops; ag.ingameLoops += ingameLoops;
                    ag.fires += fires; ag.fStable += fStable; ag.fTransient += fTransient;
                    ag.fBattle += fBattle; ag.fBoth += fBoth; ag.fLeak += fStable + fBattle + fBoth;
                    ag.updates += updates; ag.uStable += uStable; ag.uTransient += uTransient;
                    ag.uBattle += uBattle; ag.uBoth += uBoth;
                    ag.recPub += recPub; ag.recPubStable += recPubStable; ag.recPubTransient += recPubTransient;
                    ag.recPubBattle += recPubBattle; ag.recPubBoth += recPubBoth;
                    ag.recUpdPub += recUpdPub; ag.recUpdLeak += recUpdStable + recUpdBattle + recUpdBoth;
                    ag.recMenuT += recMenuT; ag.recMenuD += recMenuD; ag.recMenuM += recMenuM;
                    ag.recMenuUpd += recMenuUpd; ag.recBattle += recBattle;
                    ag.recDlgDec += recDd; ag.recDlgFire += recDf; ag.recDlgUpd += recDu;

                    topLeak.Add((name, recJunk, fLeak + uLeak, fires, stableLoops, battleLoops + bothLoops));

                    FileLogger.Log($"[catstats] corpus={name} pack={pack} fed={fed.Count} stableLoops={stableLoops} transientLoops={transientLoops} battleLoops={battleLoops} bothLoops={bothLoops} ingameLoops={ingameLoops} bTracker={bTrackLoops} bFfviProbe={bFfviProbe} | simFires={fires} onIngame={fIngame} onStableMenu={fStable} onTransientMenu={fTransient} onBattle={fBattle} onBoth={fBoth} | simUpdates={updates} onIngame={uIngame} onStableMenu={uStable} onTransientMenu={uTransient} onBattle={uBattle} onBoth={uBoth} | recPub={recPub} junkStableMenu={recPubStable} junkBattle={recPubBattle} junkBoth={recPubBoth} okTransient={recPubTransient} noFrame={recPubNoFrame} unstamped={recPubUnstamped} | recUpdPub={recUpdPub} updJunk={recUpdStable + recUpdBattle + recUpdBoth} | recDlgDec={recDd} recDlgFire={recDf} recDlgUpd={recDu} recMenuT={recMenuT} recMenuD={recMenuD} recMenuM={recMenuM} recMenuUpd={recMenuUpd} recBattle={recBattle}");
                }
                catch (Exception ex)
                {
                    corporaErr++;
                    try { FileLogger.Log($"[catstats] corpus={name} ERROR: {ex.Message}"); } catch { }
                    try { Packs.PackRegistry.SetCurrent(prevPack); } catch { }
                }
            }

            // Summary: the C1 headline numbers.
            long simJunkF = tFStable + tFBattle + tFBoth;
            long simJunkU = tUStable + tUBattle + tUBoth;
            long recJunkT = rPubStable + rPubBattle + rPubBoth;
            double junkPct = rPub > 0 ? 100.0 * recJunkT / rPub : 0;
            double simJunkPct = tFires > 0 ? 100.0 * simJunkF / tFires : 0;
            FileLogger.Log($"[catstats] SUMMARY corpora={corporaDone} err={corporaErr} fed={tFed} stableMenuLoops={tStable} transientMenuLoops={tTransient} battleLoops={tBattle} bothLoops={tBoth} ingameLoops={tIngame} bTrackerLoops={tBTrack} bFfviProbeLoops(non-ffvi,labels-on)={tBProbe}");
            var kindsSb = new System.Text.StringBuilder();
            int kindsShown = 0;
            var leakSorted = new List<KeyValuePair<string, int>>(leakKinds);
            leakSorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in leakSorted)
            {
                if (kindsShown >= 20) break;
                kindsSb.Append(kv.Key + "=" + kv.Value + " ");
                kindsShown++;
            }
            FileLogger.Log($"[catstats] SUMMARY sim-attempts(pre-guard) fires={tFires} onIngame={tFIngame} onStableMenu={tFStable} onTransientMenu={tFTransient} onBattle={tFBattle} onBoth={tFBoth} junkPct={simJunkPct:0.0}% updates={tUpd} updJunk={simJunkU} junkKinds=({kindsSb.ToString().Trim()})");
            FileLogger.Log($"[catstats] SUMMARY publish-truth(stamped) recPub={rPub} junkStableMenu={rPubStable} junkBattle={rPubBattle} junkBoth={rPubBoth} JUNKPCT={junkPct:0.0}% okTransient={rPubTransient} noFrame={rPubNoFrame} unstamped={rPubUnstamped} | recUpdPub={rUpdPub} updJunk={rUpdStable + rUpdBattle + rUpdBoth}");
            FileLogger.Log($"[catstats] SUMMARY recorded(truth) recMenuT={rMenuT} recMenuD={rMenuD} recMenuM={rMenuM} recMenuUpd={rMenuUpd} recBattle={rBattle} recDlgDec={rDd} recDlgFire={rDf} recDlgUpd={rDu}");
            foreach (var kv in byGame)
            {
                var a = kv.Value;
                FileLogger.Log($"[catstats] BYGAME pack={kv.Key} corpora={a.corpora} fed={a.fed} stableMenuLoops={a.stableLoops} transientMenuLoops={a.transientLoops} battleLoops={a.battleLoops} bothLoops={a.bothLoops} | simFires={a.fires} simJunk={a.fLeak} simUpdates={a.updates} simUpdJunk={a.uStable + a.uBattle + a.uBoth} | recPub={a.recPub} junkStableMenu={a.recPubStable} junkBattle={a.recPubBattle} junkBoth={a.recPubBoth} okTransient={a.recPubTransient} junkPct={(a.recPub > 0 ? 100.0 * (a.recPubStable + a.recPubBattle + a.recPubBoth) / a.recPub : 0):0.0}% recUpdJunk={a.recUpdLeak} | recMenuT={a.recMenuT} recMenuD={a.recMenuD} recMenuM={a.recMenuM} recMenuUpd={a.recMenuUpd} recBattle={a.recBattle} recDlgDec={a.recDlgDec} recDlgFire={a.recDlgFire} recDlgUpd={a.recDlgUpd}");
            }
            // Ranked by LIVE-truth junk (publish-level), sim leak beside it.
            topLeak.Sort((x, y) => y.recJunk != x.recJunk ? y.recJunk.CompareTo(x.recJunk) : y.simLeak.CompareTo(x.simLeak));
            for (int i = 0; i < topLeak.Count && i < 15; i++)
            {
                var tl = topLeak[i];
                FileLogger.Log($"[catstats] TOPJUNK corpus={tl.name} recJunk={tl.recJunk} simLeak={tl.simLeak} fires={tl.fires} stableLoops={tl.stable} battleLoops={tl.battle}");
            }
            FileLogger.Log("[catstats] done (observe-only, exit 0)");
            return 0;
        }
    }
}
