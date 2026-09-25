using System.Collections.Generic;
using WunderLanguage.Host.Ocr;

namespace WunderLanguage.Host.Capture
{
    // Battle-frame positional filter (core, title-agnostic — no per-game strings).
    // One home for "where on screen" rules so CaptureController stays wiring-only.
    // Bands are resolution-relative (ny of block top): top rewards <= 0.25,
    // mid dialogue 0.25-0.65, bottom battle >= 0.65.
    public static class BattleFrame
    {
        // 0 = not battle, 1 = bottom-only (suppress dialogue), 2 = top rewards (isolate).
        public static int Classify(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, System.Collections.Generic.IReadOnlyList<string> weaponLabels, out string topRaw)
        {
            topRaw = "";
            if (fw <= 1 || fh <= 1 || blocks == null || blocks.Count < 3) return 0;
            int bottomJa = 0, midJa = 0, topJa = 0;
            foreach (var b in blocks)
            {
                if (!isJa(b.Text)) continue;
                double ny = (double)b.Y / fh;
                if (ny < 0.25) topJa++;
                else if (ny < 0.65) midJa++;
                else bottomJa++;
            }
            if (midJa > 0) return 0;
            // Rewards arm: top reporting bar + party/status below, no mid dialogue.
            // Bypasses the narrow-shape tests (reward bars are legitimately wide).
            if (topJa >= 1 && bottomJa >= 1) return FinishTop(blocks, fh, isJa, out topRaw);
            // Item-box arm: two X-disjoint TALL JA columns in the bottom band
            // (inventory name columns; counts ride alongside). Tall = merged
            // multi-row text no normal battle box reaches (monster/action
            // talls stay well short of it). The controller trusts class 3 only
            // mid-battle (battle context), so tall field menus never route here.
            if (ExtractItemColumns(blocks, fh, isJa).cols.Count >= 2) { topRaw = ""; return 3; }
            // Weapon-select arm (mockup "Weapon Select Open"): the on-top
            // equip box always shows its slot labels (pack WeaponLabels), so
            // labels — not geometry — mark the state. Geometry alone cannot
            // separate a merged label block from a merged action-command
            // block (same band, same size), and live capture frames carry
            // different borders than any traced mockup, so no absolute width
            // or Y band is trusted here. Solo-line safety: the arm needs
            // company (2+ bottom JA blocks). The controller trusts class 4
            // only mid-battle, like class 3. No labels (unknown game) = the
            // arm stays off, everything routes as before.
            if (HasWeaponLabels(blocks, fh, isJa, weaponLabels))
            {
                int wBottom = 0;
                foreach (var b in blocks)
                {
                    if (!isJa(b.Text)) continue;
                    if ((double)b.Y / fh >= 0.65) wBottom++;
                }
                if (wBottom >= 2) { topRaw = ""; return 4; }
            }
            if (bottomJa < 2) return 0;
            int shortNarrow = 0;
            bool anyWide = false;
            foreach (var b in blocks)
            {
                if (!isJa(b.Text)) continue;
                double ny = (double)b.Y / fh;
                if (ny < 0.65) continue;
                double nw = (double)b.Width / fw, nh = (double)b.Height / fh;
                if (nw >= 0.35) { anyWide = true; continue; }
                if (nh < 0.15) shortNarrow++;
            }
            // Wide short rows veto the short arm only (Zelda dialogue safety);
            // wide TALL columns still pass via the tall arm below (item lists).
            bool battle = shortNarrow >= 2 && !anyWide;
            if (!battle)
            {
                var talls = new List<OcrBlock>();
                foreach (var b in blocks)
                {
                    if (!isJa(b.Text)) continue;
                    double ny = (double)b.Y / fh;
                    if (ny < 0.65) continue;
                    if ((double)b.Height / fh > 0.15 && (double)b.Width / fw < 0.45) talls.Add(b);
                }
                for (int i = 0; i < talls.Count && !battle; i++)
                    for (int j = i + 1; j < talls.Count; j++)
                    {
                        int ax0 = talls[i].X, ax1 = talls[i].X + talls[i].Width;
                        int bx0 = talls[j].X, bx1 = talls[j].X + talls[j].Width;
                        int gap = ax0 < bx0 ? bx0 - ax1 : ax0 - bx1;
                        if (gap > 50) { battle = true; break; }
                    }
            }
            if (!battle) return 0;
            return FinishTop(blocks, fh, isJa, out topRaw);
        }

        private static int FinishTop(List<OcrBlock> blocks, int fh, System.Func<string, bool> isJa, out string topRaw)
        {
            var tops = new List<string>();
            foreach (var b in blocks)
                if (isJa(b.Text) && (double)b.Y / fh < 0.25)
                    tops.Add(b.Text.Trim());
            if (tops.Count > 0) { topRaw = string.Join(" ", tops); return 2; }
            topRaw = "";
            return 1;
        }

        public static bool IsBattle(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, System.Collections.Generic.IReadOnlyList<string> weaponLabels)
        {
            string unused;
            return Classify(blocks, fw, fh, isJa, weaponLabels, out unused) != 0;
        }

        // Party-status cell: short+narrow, right of the enemy/action column.
        public static bool IsPartyStatusBlock(OcrBlock b, int fw, int fh, System.Func<string, bool> isJa)
        {
            if (fw <= 1 || fh <= 1) return false;
            double nx = (double)b.X / fw, ny = (double)b.Y / fh;
            double nw = (double)b.Width / fw, nh = (double)b.Height / fh;
            return ny >= 0.65 && nx >= 0.38 && nw < 0.25 && nh < 0.13 && isJa(b.Text);
        }

        // Two tall blocks sharing a Y-line but X-disjoint = separate columns, never one row.
        public static bool IsTallColumnSplit(OcrBlock b, OcrBlock part)
        {
            if (b.Height <= 150 || part.Height <= 150) return false;
            int ax0 = part.X, ax1 = part.X + part.Width;
            int bx0 = b.X, bx1 = b.X + b.Width;
            int gap = ax0 < bx0 ? bx0 - ax1 : ax0 - bx1;
            return gap > 50;
        }

        public sealed record BattleSlots(string Top, List<string> Monster, List<string> Action, List<string> Right, List<string> Item, List<string> Weapon);

        // Weapon-select layer (mockup "Weapon Select Open"): the on-top equip
        // box always shows its slot labels (pack WeaponLabels), so labels pin
        // the state; rows below the labels are weapon names, and the
        // bottom-most line below the labels is the peeking item row regrouped
        // into "names\tdigits" columns (widest x-gap split) for the
        // controller's pair zipper. Fully relative (order, not bands): live
        // capture frames carry different borders than any traced mockup, so
        // no absolute width or Y band is trusted. Shorts above the labels
        // stay unclaimed and route normally (base rows peeking through
        // transitions). No labels (unknown game) = everything empty, zero
        // behavior change.
        private static bool ContainsWeaponLabel(string t, System.Collections.Generic.IReadOnlyList<string> labels)
        {
            if (string.IsNullOrEmpty(t) || labels == null) return false;
            foreach (var l in labels)
                if (!string.IsNullOrEmpty(l) && t.Contains(l, System.StringComparison.Ordinal)) return true;
            return false;
        }
        private static bool HasWeaponLabels(System.Collections.Generic.List<OcrBlock> blocks, int fh, System.Func<string, bool> isJa, System.Collections.Generic.IReadOnlyList<string> weaponLabels)
        {
            if (blocks == null || fh <= 1 || weaponLabels == null || weaponLabels.Count == 0) return false;
            foreach (var b in blocks)
            {
                string t = (b.Text ?? "").Trim();
                if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                if (!isJa(t) || (double)b.Y / fh < 0.60) continue;
                if (ContainsWeaponLabel(t, weaponLabels)) return true;
            }
            return false;
        }
        private sealed record WeaponLayer(
            System.Collections.Generic.HashSet<OcrBlock> cells,
            System.Collections.Generic.List<(int y, int x, int h, string t)> parts,
            System.Collections.Generic.List<string> tailCols,
            int labelCount);
        private static WeaponLayer ExtractWeaponLayer(System.Collections.Generic.List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, System.Collections.Generic.IReadOnlyList<string> weaponLabels, System.Collections.Generic.HashSet<OcrBlock> itemCells)
        {
            var none = new WeaponLayer(new System.Collections.Generic.HashSet<OcrBlock>(), new System.Collections.Generic.List<(int y, int x, int h, string t)>(), new System.Collections.Generic.List<string>(), 0);
            if (blocks == null || fw <= 1 || fh <= 1 || weaponLabels == null || weaponLabels.Count == 0) return none;
            var labels = new System.Collections.Generic.List<OcrBlock>();
            foreach (var b in blocks)
            {
                string t = (b.Text ?? "").Trim();
                if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                if (!isJa(t) || (double)b.Y / fh < 0.60) continue;
                if (!ContainsWeaponLabel(t, weaponLabels)) continue;
                labels.Add(b);
            }
            if (labels.Count == 0) return none;
            int labelTop = int.MaxValue, labelBottom = int.MinValue;
            foreach (var l in labels)
            {
                if (l.Y < labelTop) labelTop = l.Y;
                if (l.Y + l.Height > labelBottom) labelBottom = l.Y + l.Height;
            }
            // Short bottom candidates at/below the labels: weapon names + the
            // peeking item row. Covering hides everything else, so whatever
            // is visible here belongs to the top two boxes.
            var shorts = new System.Collections.Generic.List<OcrBlock>();
            foreach (var b in blocks)
            {
                string t = (b.Text ?? "").Trim();
                if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                if (itemCells.Contains(b)) continue;
                if ((double)b.Y / fh < 0.60) continue;
                if ((double)b.Height / fh >= 0.20) continue;
                if (!isJa(t) && !HasDigit(t)) continue;
                if (b.Y < labelTop - 8) continue;
                shorts.Add(b);
            }
            // Y-lines (same tolerance as JoinLines).
            var lines = new System.Collections.Generic.List<(int y, System.Collections.Generic.List<OcrBlock>bs)>();
            foreach (var p in shorts.OrderBy(p => p.Y).ThenBy(p => p.X))
            {
                bool placed = false;
                foreach (var ln in lines)
                {
                    if (System.Math.Abs(p.Y - ln.y) < System.Math.Min(p.Height, 60) / 2 + 8)
                    {
                        ln.bs.Add(p);
                        placed = true;
                        break;
                    }
                }
                if (!placed) lines.Add((p.Y, new System.Collections.Generic.List<OcrBlock> { p }));
            }
            // Tail = bottom-most line strictly below the labels that carries
            // JA names (the peeking item row). Halves ship count-less when
            // counts flicker out (wrong counts are worse than none).
            System.Collections.Generic.List<OcrBlock>? tail = null;
            foreach (var ln in lines.OrderBy(l => l.y))
            {
                if (ln.y <= labelBottom + 8) continue;
                bool hasJa = false;
                foreach (var c in ln.bs) if (isJa((c.Text ?? "").Trim())) { hasJa = true; break; }
                if (hasJa) tail = ln.bs;
            }
            var tailSet = new System.Collections.Generic.HashSet<OcrBlock>();
            var tailCols = new System.Collections.Generic.List<string>();
            if (tail != null && tail.Count > 0)
            {
                foreach (var c in tail) tailSet.Add(c);
                var ordered = new System.Collections.Generic.List<OcrBlock>(tail);
                ordered.Sort((a, b2) => a.X.CompareTo(b2.X));
                int split = ordered.Count;
                if (ordered.Count > 1)
                {
                    int bestGap = 20, bestIx = ordered.Count;
                    for (int i = 1; i < ordered.Count; i++)
                    {
                        int gap = ordered[i].X - (ordered[i - 1].X + ordered[i - 1].Width);
                        if (gap > bestGap) { bestGap = gap; bestIx = i; }
                    }
                    split = bestIx;
                }
                for (int h = 0; h < 2; h++)
                {
                    int lo = h == 0 ? 0 : split, hi = h == 0 ? split : ordered.Count;
                    if (lo >= hi) continue;
                    var names = new System.Collections.Generic.List<string>();
                    var digs = new System.Collections.Generic.List<string>();
                    for (int i = lo; i < hi; i++)
                    {
                        string t = (ordered[i].Text ?? "").Trim();
                        if (isJa(t)) names.Add(t);
                        else if (HasDigit(t)) digs.Add(t);
                    }
                    if (names.Count == 0) continue;
                    tailCols.Add(string.Join("\n", names) + "\t" + string.Join(" ", digs));
                }
            }
            var cells = new System.Collections.Generic.HashSet<OcrBlock>();
            var parts = new System.Collections.Generic.List<(int y, int x, int h, string t)>();
            foreach (var l in labels)
            {
                string lt = (l.Text ?? "").Trim();
                cells.Add(l);
                parts.Add((l.Y, l.X, l.Height, lt));
            }
            foreach (var p in shorts)
            {
                if (labels.Contains(p)) continue;
                if (tailSet.Contains(p)) continue;
                string t = (p.Text ?? "").Trim();
                if (!isJa(t)) continue;
                cells.Add(p);
                parts.Add((p.Y, p.X, p.Height, t));
            }
            return new WeaponLayer(cells, parts, tailCols, labels.Count);
        }

        private static bool HasDigit(string s)
        {
            foreach (char c in s)
                if ((c >= '0' && c <= '9') || (c >= '０' && c <= '９')) return true;
            return false;
        }

        // Action cell: the tall command box (or its low item tail). Everything else
        // short on the left is monster + count.
        private static bool IsActionCell(OcrBlock b, int fh)
        {
            double ny = (double)b.Y / fh, nh = (double)b.Height / fh;
            return nh >= 0.095 || ny >= 0.85;
        }

        // Item-box inventory columns: the box merges each column (names,
        // counts) into one TALL block, and OCR never preserves the ： — so
        // geometry is the signal, not punctuation. Tall bottom JA blocks that
        // are X-disjoint are name columns; tall digit blocks alongside are
        // their counts. Returns raw "names\tcounts" column pairs left to
        // right (splitting happens in the controller, which owns pack
        // vocabulary + the name memo). Claimed cells never leak into
        // monster/action/party slots during transitions.
        private static (System.Collections.Generic.HashSet<OcrBlock> cells, System.Collections.Generic.List<string> cols) ExtractItemColumns(List<OcrBlock> blocks, int fh, System.Func<string, bool> isJa)
        {
            var cells = new System.Collections.Generic.HashSet<OcrBlock>();
            var cols = new System.Collections.Generic.List<string>();
            if (blocks == null || fh <= 1) return (cells, cols);
            var names = new System.Collections.Generic.List<OcrBlock>();
            var counts = new System.Collections.Generic.List<OcrBlock>();
            foreach (var b in blocks)
            {
                string t = (b.Text ?? "").Trim();
                if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                if ((double)b.Y / fh < 0.5) continue;
                if ((double)b.Height / fh < 0.13) continue;
                if (isJa(t)) names.Add(b);
                else if (HasDigit(t)) counts.Add(b);
            }
            names.Sort((a, b2) => a.X.CompareTo(b2.X));
            counts.Sort((a, b2) => a.X.CompareTo(b2.X));
            // X-disjoint greedy: flicker duplicates of one column overlap and
            // merge to a single column instead of counting twice.
            var ncol = new System.Collections.Generic.List<OcrBlock>();
            foreach (var b in names)
            {
                if (ncol.Count > 0 && b.X < ncol[ncol.Count - 1].X + ncol[ncol.Count - 1].Width + 20) continue;
                ncol.Add(b);
            }
            var ccol = new System.Collections.Generic.List<OcrBlock>();
            foreach (var b in counts)
            {
                if (ccol.Count > 0 && b.X < ccol[ccol.Count - 1].X + ccol[ccol.Count - 1].Width + 20) continue;
                ccol.Add(b);
            }
            for (int i = 0; i < ncol.Count; i++)
            {
                string nt = (ncol[i].Text ?? "").Trim();
                string ct = i < ccol.Count ? (ccol[i].Text ?? "").Trim() : "";
                cells.Add(ncol[i]);
                if (i < ccol.Count) cells.Add(ccol[i]);
                cols.Add(nt + "\t" + ct);
            }
            return (cells, cols);
        }
        // Slot split for the panel mirror: top reporting bar, bottom-left monster
        // lines, bottom-left action box (overlapping), bottom-right party names.
        // JA mirror keeps count digits with monsters; party slot is JA names only
        // (HP/timebar never ship). Same-Y fragments join one line ("name 3").
        // Noise (ellipsis-only) never ships.
        public static BattleSlots ExtractSlots(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, System.Collections.Generic.IReadOnlyList<string> weaponLabels, System.Func<string, bool> isNoise)
        {
            var top = new List<(int x, string t)>();
            var monster = new List<(int y, int x, int h, string t)>();
            var action = new List<(int y, int x, int h, string t)>();
            var right = new List<(int y, int x, int h, string t)>();
            if (blocks == null || fw <= 1 || fh <= 1)
                return new BattleSlots("", new List<string>(), new List<string>(), new List<string>(), new List<string>(), new List<string>());
            // Weapon cells are claimed before item cells: the on-top equip
            // box must never leak its label/name rows into item columns or
            // the right-side party slot (cx >= 0.38). The peeking item tail
            // regroups only under an open weapon box (stacking: item grid
            // underneath, labels pin the state).
            var itemClaim = ExtractItemColumns(blocks, fh, isJa);
            var weaponLayer = ExtractWeaponLayer(blocks, fw, fh, isJa, weaponLabels, itemClaim.cells);
            var itemCols = new System.Collections.Generic.List<string>(itemClaim.cols);
            itemCols.AddRange(weaponLayer.tailCols);
            foreach (var b in blocks)
            {
                string t = (b.Text ?? "").Trim();
                if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                if (isNoise(t)) continue;
                if (weaponLayer.cells.Contains(b)) continue;
                if (itemClaim.cells.Contains(b)) continue;
                double ny = (double)b.Y / fh;
                if (ny < 0.25)
                {
                    if (isJa(t)) top.Add((b.X, t));
                    continue;
                }
                if (ny < 0.65) continue;
                double cx = (double)(b.X + b.Width / 2) / fw;
                if (cx >= 0.38)
                {
                    if (isJa(t)) AddBlockLines(right, b, t);
                    continue;
                }
                if (!isJa(t) && !HasDigit(t)) continue;
                if (IsActionCell(b, fh)) action.Add((b.Y, b.X, b.Height, t));
                else AddBlockLines(monster, b, t);
            }
            top.Sort((a, b2) => a.x.CompareTo(b2.x));
            return new BattleSlots(
                CleanRewardBar(string.Join(" ", top.ConvertAll(p => p.t))),
                JoinLines(monster),
                JoinLines(action),
                JoinLines(right),
                itemCols,
                JoinLines(weaponLayer.parts));
        }

        // Bounded fuzzy command search (the anti-whack-a-mole safeguard): OCR
        // garbles are combinatorial — each kana misreads several ways — so
        // literal rows could never cover the family and every novel variant
        // posted raw and unpinned until cited. Sliding Levenshtein windows
        // (≤2 edits on short commands, ≤3 on long, window within 1 char of
        // pattern length) snap the family to canonical. Magic stays
        // exact-only (blank-unless-seen). Every snap is logged for audit.
        // Action-slot text only: a snap chooses between known commands, never
        // invents text. Unknown games never reach here (verbatim above).
        private static bool FuzzyFind(string text, string pattern, int maxEdits, out string match, out int edits)
        {
            match = "";
            edits = int.MaxValue;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern) || maxEdits < 0) return false;
            int n = text.Length, m = pattern.Length;
            if (text.Contains(pattern, System.StringComparison.Ordinal)) { match = pattern; edits = 0; return true; }
            if (n < m - 1) return false;
            int bestD = int.MaxValue, bestI = -1, bestL = 0;
            for (int i = 0; i < n; i++)
            {
                for (int len = m - 1; len <= m + maxEdits; len++)
                {
                    if (len <= 0 || i + len > n) continue;
                    int d = Levenshtein(text.Substring(i, len), pattern);
                    if (d < bestD) { bestD = d; bestI = i; bestL = len; if (d == 0) break; }
                }
                if (bestD == 0) break;
            }
            if (bestI >= 0 && bestD <= maxEdits) { match = text.Substring(bestI, bestL); edits = bestD; return true; }
            return false;
        }

        private static int Levenshtein(string a, string b)
        {
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;
            var prev = new int[m + 1];
            var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= m; j++)
                    cur[j] = System.Math.Min(System.Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                var tmp = prev; prev = cur; cur = tmp;
            }
            return prev[m];
        }

        // 4-row action menu: fight / ability / magic-or-blank / item. Merged OCR
        // text segments on pack command vocabulary (cited per game) via exact
        // match first, then the FuzzyFind safeguard below; unknown remainder
        // shows raw in the ability row (truthful over clever).
        // Fixed rows (fight row 0, item row 3) pin whenever the box is open:
        // cursor cover or flicker hides them from OCR, never from the game.
        // Ability (row 1) is per-character, matched or remainder, never
        // pinned. Magic (row 2) is blank unless seen. Empty/absent rows stay
        // "" so the panel holds the positional gap.
        // No vocabulary (unknown game) = verbatim passthrough, never hidden.
        public static List<string> SplitActionRows(List<string> actionLines, System.Collections.Generic.IReadOnlyList<string> commands)
        {
            var rows = new List<string> { "", "", "", "" };
            // No input = no box (panel collapses); gaps only exist within content.
            if (actionLines == null || actionLines.Count == 0) return new List<string>();
            // One ability = one row: OCR line-breaks inside a command name
            // (ひっきつ/れざ) are layout noise, not rows — strip them before
            // segmenting, or the ability eats the blank row's slot.
            var clean = actionLines.ConvertAll(l => (l ?? "").Replace("\r", "").Replace("\n", ""));
            if (commands == null || commands.Count < 5) return new List<string>(clean);
            string joined = string.Join("", clean.ConvertAll(l => l.Replace(" ", "").Replace("　", "").Replace("…", "").Replace("．", "").Replace(".", "")));
            if (joined.Length == 0) return new List<string>();
            string fight = commands[0], magic = commands[3], item = commands[4];
            var abilities = new List<string>();
            for (int i = 1; i < 3 && i < commands.Count; i++) abilities.Add(commands[i]);
            var consumed = new List<string>();
            bool MatchCmd(string pattern, out string window)
            {
                window = "";
                if (joined.Contains(pattern, System.StringComparison.Ordinal)) { window = pattern; return true; }
                int budget = pattern.Length <= 4 ? 2 : 3;
                if (FuzzyFind(joined, pattern, budget, out string w, out int edits))
                {
                    window = w;
                    FileLogger.Log($"[battle] action-fuzzy \"{w}\" -> \"{pattern}\" d={edits}");
                    return true;
                }
                return false;
            }
            if (MatchCmd(fight, out string wf)) { rows[0] = fight; if (wf != fight) consumed.Add(wf); }
            foreach (var a in abilities)
                if (MatchCmd(a, out string wa)) { rows[1] = a; if (wa != a) consumed.Add(wa); break; }
            // Magic stays exact-only: blank unless unmistakably seen.
            if (joined.Contains(magic, System.StringComparison.Ordinal)) rows[2] = magic;
            if (MatchCmd(item, out string wi)) { rows[3] = item; if (wi != item) consumed.Add(wi); }
            if (rows[0].Length == 0 && rows[1].Length == 0 && rows[2].Length == 0 && rows[3].Length == 0)
                return new List<string>(clean);
            // Any one matched row proves the box is open — pin the fixed
            // chrome around it.
            if (rows[0].Length == 0) rows[0] = fight;
            if (rows[3].Length == 0) rows[3] = item;
            if (rows[1].Length == 0)
            {
                string rest = joined;
                foreach (var c in commands)
                    rest = rest.Replace(c, "", System.StringComparison.Ordinal);
                foreach (var w in consumed)
                    rest = rest.Replace(w, "", System.StringComparison.Ordinal);
                rest = rest.Trim();
                // Single-char dust (ェ-class) never posts: it burned a full
                // translation into stray EN ("I") every flicker loop.
                if (rest.Length >= 2) rows[1] = rest;
            }
            return rows;
        }

        // Reward-bar digit cleanup (top bar ONLY — never dialogue): fullwidth dots
        // between digits are OCR noise (３．３０→３３０), and a lone g before
        // ポイント/ギル is a ９ misread (１５ｇ→１５９). No dates live up here.
        public static string CleanRewardBar(string top)
        {
            if (string.IsNullOrEmpty(top)) return top;
            string s = System.Text.RegularExpressions.Regex.Replace(top, @"([０-９0-9])[．.](?=[０-９0-9])", "$1");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"([０-９0-9])[gGｇ](?=ポイント|ギル)", "$19");
            return s;
        }

        // Stacked-line split (two-type fights: one tall OCR block can hold a
        // vertical stack like "エピオルニス\nシピーラビット", counts hidden
        // at 1 in-game). Y-joining would flatten the stack into one row, so
        // lines split first with evenly distributed Y — one panel row per
        // line. Single-line blocks pass through unchanged.
        private static void AddBlockLines(List<(int y, int x, int h, string t)> parts, OcrBlock b, string t)
        {
            string[] lines = (t ?? "").Split(new string[] { "\r\n", "\n", "\r" }, System.StringSplitOptions.None);
            var kept = new List<string>();
            foreach (var ln in lines)
            {
                string s = (ln ?? "").Trim();
                if (s.Length > 0) kept.Add(s);
            }
            if (kept.Count <= 1) { parts.Add((b.Y, b.X, b.Height, kept.Count == 1 ? kept[0] : string.Empty)); return; }
            for (int i = 0; i < kept.Count; i++)
            {
                int y = b.Y + (int)(b.Height * (double)i / kept.Count);
                int h = System.Math.Max(8, b.Height / kept.Count);
                parts.Add((y, b.X, h, kept[i]));
            }
        }

        private static List<string> JoinLines(List<(int y, int x, int h, string t)> parts)
        {
            var lines = new List<(int y, List<(int x, string t)> cells)>();
            foreach (var p in parts.OrderBy(p => p.y).ThenBy(p => p.x))
            {
                bool placed = false;
                foreach (var ln in lines)
                {
                    if (System.Math.Abs(p.y - ln.y) < System.Math.Min(p.h, 60) / 2 + 8)
                    {
                        ln.cells.Add((p.x, p.t));
                        placed = true;
                        break;
                    }
                }
                if (!placed) lines.Add((p.y, new List<(int x, string t)> { (p.x, p.t) }));
            }
            var rows = new List<string>();
            foreach (var ln in lines.OrderBy(l => l.y))
                rows.Add(string.Join(" ", ln.cells.OrderBy(c => c.x).Select(c => c.t)));
            return rows;
        }
    }
}