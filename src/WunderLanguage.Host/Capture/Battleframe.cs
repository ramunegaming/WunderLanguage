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
        public static int Classify(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, System.Collections.Generic.IReadOnlyList<string> weaponLabels, out string topRaw, bool itemOpen = false)
        {
            topRaw = "";
            // Minimum 2: the item box merges to exactly 2 tall columns
            // (left + right); a <3 gate misroutes every clean 2-block
            // item frame to dialogue and wipes battle context (2026-09-30
            // log: 2-block item fired as in-game text). Mid-band JA still
            // vetoes below, so 2-block dialogue never routes here.
            if (fw <= 1 || fh <= 1 || blocks == null || blocks.Count < 2) return 0;
            int bottomJa = 0, midJa = 0, topJa = 0;
            foreach (var b in blocks)
            {
                // Chrome edge: capture slivers above the game canvas
                // (emulator menu header at y=-1, 2026-09-30 log fired as a
                // 130ch battle top bar). Game reward bars sit at y≈60+.
                if (b.Y < 8) continue;
                if (!isJa(b.Text)) continue;
                double ny = (double)b.Y / fh;
                if (ny < 0.25)
                {
                    // Reward bars are SHORT wide rows; tall top blocks are
                    // emulator menu dropdowns (h310 at y83 cited), never tops.
                    if ((double)b.Height / fh < 0.15) topJa++;
                }
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
            if (ExtractItemColumns(blocks, fw, fh, isJa, itemOpen, weaponLabels).cols.Count >= 2) { topRaw = ""; return 3; }
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
                // Same chrome/tall exclusions as the count above: menu
                // dropdown text never joins a reward bar.
                if (b.Y >= 8 && isJa(b.Text) && (double)b.Y / fh < 0.25 && (double)b.Height / fh < 0.15)
                    tops.Add(b.Text.Trim());
            if (tops.Count > 0) { topRaw = string.Join(" ", tops); return 2; }
            topRaw = "";
            return 1;
        }

        public static bool IsBattle(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, System.Collections.Generic.IReadOnlyList<string> weaponLabels, bool itemOpen = false)
        {
            string unused;
            return Classify(blocks, fw, fh, isJa, weaponLabels, out unused, itemOpen) != 0;
        }

        // Scroll-remnant hold (cited 2026-09-30: item scroll fragments into
        // 1-col tall or short-row blocks that Classify can't own — 1 JA tall
        // + dots + digits, h84 short rows — and the cls==0 wipe then kills
        // battle context, so follow-up item frames fall to dialogue and fire
        // hallucinations as in-game text). Remnant = bottom-only JA, no
        // top/mid JA, no wide bottom block (wide = real dialogue box, which
        // must still fire). Controller-only: suppresses dialogue and holds
        // the battle frame (no wipe, no fire) while context is alive.
        // Outside battle context it is never consulted: behavior identical.
        public static bool IsBattleRemnant(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa)
        {
            if (blocks == null || fw <= 1 || fh <= 1 || blocks.Count == 0) return false;
            bool anyBottomJa = false;
            foreach (var b in blocks)
            {
                if (b.Y < 8 || b.Width < 10 || b.Height < 12) continue;
                string t = (b.Text ?? "").Trim();
                if (t.Length < 1) continue;
                double ny = (double)b.Y / fh;
                double nw = (double)b.Width / fw;
                if (ny < 0.25) { if (isJa(t) && (double)b.Height / fh < 0.15) return false; }
                else if (ny < 0.65) { if (isJa(t)) return false; }
                else
                {
                    if (isJa(t)) anyBottomJa = true;
                    if (nw >= 0.60) return false;
                }
            }
            return anyBottomJa;
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
        private static bool ContainsWeaponLabel(string t, System.Collections.Generic.IReadOnlyList<string>? labels)
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
            // Tail = bottom TWO lines strictly below the labels that carry
            // JA names (the peeking item rows — usually one, two when the
            // box sits higher). Halves ship count-less when counts flicker
            // out (wrong counts are worse than none).
            var tailLines = new System.Collections.Generic.List<System.Collections.Generic.List<OcrBlock>>();
            foreach (var ln in lines.OrderBy(l => l.y))
            {
                if (ln.y <= labelBottom + 8) continue;
                bool hasJa = false;
                foreach (var c in ln.bs) if (isJa((c.Text ?? "").Trim())) { hasJa = true; break; }
                if (hasJa) tailLines.Add(ln.bs);
            }
            // Bottom-align: peeking rows sit at the box bottom (rows 3-4 of
            // the top-open window underneath — cited 2026-10-02 invariant:
            // scroll-up-to-weapon always leaves rows 1-4 showing). Prepend
            // blank hard-rows so top-fill never drags them upward; the
            // normal split/pad/anchor/settle below then treats them like
            // any rows (anchor agrees: mem holds the same row-4 positions).
            var tailSet = new System.Collections.Generic.HashSet<OcrBlock>();
            var tailCols = new System.Collections.Generic.List<string>();
            if (tailLines.Count > 0)
            {
                while (tailLines.Count > 2) tailLines.RemoveAt(0);
                var leftNames = new System.Collections.Generic.List<string>();
                var rightNames = new System.Collections.Generic.List<string>();
                var leftDigs = new System.Collections.Generic.List<string>();
                var rightDigs = new System.Collections.Generic.List<string>();
                foreach (var tl in tailLines)
                {
                    var ordered = new System.Collections.Generic.List<OcrBlock>(tl);
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
                    var ln_ = new System.Collections.Generic.List<string>();
                    var rn = new System.Collections.Generic.List<string>();
                    var ld = new System.Collections.Generic.List<string>();
                    var rd = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        string t = (ordered[i].Text ?? "").Trim();
                        if (isJa(t)) (i < split ? ln_ : rn).Add(t);
                        else if (HasDigit(t)) (i < split ? ld : rd).Add(t);
                    }
                    if (ln_.Count == 0 && rn.Count == 0) continue;
                    leftNames.Add(string.Join(" ", ln_));
                    rightNames.Add(string.Join(" ", rn));
                    leftDigs.Add(string.Join(" ", ld));
                    rightDigs.Add(string.Join(" ", rd));
                }
                foreach (var tl in tailLines) foreach (var c in tl) tailSet.Add(c);
                int rows = System.Math.Max(leftNames.Count, rightNames.Count);
                if (rows > 0)
                {
                    while (leftNames.Count < rows) leftNames.Add("");
                    while (rightNames.Count < rows) rightNames.Add("");
                    while (leftDigs.Count < rows) leftDigs.Add("");
                    while (rightDigs.Count < rows) rightDigs.Add("");
                    string pad = string.Concat(System.Linq.Enumerable.Repeat("　\n", System.Math.Max(0, 4 - rows)));
                    tailCols.Add(pad + string.Join("\n", leftNames) + "\t" + string.Join(" ", leftDigs));
                    tailCols.Add(pad + string.Join("\n", rightNames) + "\t" + string.Join(" ", rightDigs));
                }
            }
            var cells = new System.Collections.Generic.HashSet<OcrBlock>();
            var parts = new System.Collections.Generic.List<(int y, int x, int h, string t)>();
            foreach (var l in labels)
            {
                string lt = (l.Text ?? "").Trim();
                cells.Add(l);
                // Positional split (cited 2026-10-02: "みぎてゆメタルナックル"
                // glued label row + name row into one tall block, posting +
                // translating as a sentence). The label rides row 1; whatever
                // follows it is the equipped name on row 2 below. Emitted
                // label is the exact pack string (never OCR's spelling).
                string? found = null;
                int fix = -1;
                if (weaponLabels != null)
                    foreach (var w in weaponLabels)
                    {
                        if (string.IsNullOrEmpty(w)) continue;
                        int q = lt.IndexOf(w, System.StringComparison.Ordinal);
                        if (q >= 0 && (fix < 0 || q < fix)) { fix = q; found = w; }
                    }
                if (found != null)
                {
                    parts.Add((l.Y, l.X, l.Height, found));
                    string rest = (lt.Substring(0, fix) + " " + lt.Substring(fix + found.Length)).Trim(' ', '　', '・', '…', '．', '.', '「', '」', '"', '\'', '、', '。');
                    rest = StripGlovePrefix(rest);
                    if (rest.Length >= 2) parts.Add((l.Y + System.Math.Max(30, l.Height / 2), l.X, l.Height, rest));
                }
                else parts.Add((l.Y, l.X, l.Height, lt));
            }
            foreach (var p in shorts)
            {
                if (labels.Contains(p)) continue;
                if (tailSet.Contains(p)) continue;
                string t = StripGlovePrefix((p.Text ?? "").Trim());
                if (!isJa(t)) continue;
                cells.Add(p);
                parts.Add((p.Y, p.X, p.Height, t));
            }
            // Tail cells belong to the peeking item row, never to base rows:
            // fold them into the claim set so monster/action/party slots
            // can't pick them up during transitions (cited 2026-10-02:
            // tail Y|Kd fired as action + party while the item box showed
            // blanks).
            foreach (var c in tailSet) cells.Add(c);
            return new WeaponLayer(cells, parts, tailCols, labels.Count);
        }

        // Glove-cursor prefix strip, weapon path only (cited 2026-10-02:
        // the selection glove reads as ゆ/お/の — and as Latin A (panel
        // "Aメタルナックル", stable) — ahead of the equipped name; cited
        // 2026-10-03: the glove also reads as ４ ("４メタルナックル").
        // Icons are not text: they strip at extraction, never translate.
        // Digits are safe here — item counts never pass this path, and no
        // weapon name starts with one. Single leading glove char only,
        // remainder must stay a real name (≥2 chars) so a genuine short
        // ゆ-start (ゆみ-class, 2 chars) never strips. Dialogue text never
        // passes here — ゆく-class verbs and Zelda Ａボタン prompts are
        // safe (no labels there = layer off).
        private static string StripGlovePrefix(string t)
        {
            string s = (t ?? "").TrimStart(' ', '　', '・', '…', '．', '.', '「', '」', '"', '\'', '、', '。', '!', '！', '?', '？', ',', '，');
            if (s.Length >= 3 && (s[0] == 'ゆ' || s[0] == 'お' || s[0] == 'の' || s[0] == 'A' || s[0] == 'Ａ' || s[0] == '４' || s[0] == '4'))
            {
                string rest = s.Substring(1).TrimStart(' ', '　', '・', '…', '．', '.', '「', '」', '"', '\'', '、', '。');
                if (rest.Length >= 2) return rest;
            }
            return s;
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
        private static (System.Collections.Generic.HashSet<OcrBlock> cells, System.Collections.Generic.List<string> cols) ExtractItemColumns(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, bool itemOpen = false, System.Collections.Generic.IReadOnlyList<string>? weaponLabels = null)
        {
            var cells = new System.Collections.Generic.HashSet<OcrBlock>();
            var cols = new System.Collections.Generic.List<string>();
            if (blocks == null || fw <= 1 || fh <= 1) return (cells, cols);
            // Latched open box: scrolled short rows (bottom-of-list views too
            // sparse for tall merges) assemble as item columns. Names gate at
            // ~2 merged rows (single rows, monster h~48 and party h~42 stay
            // out); counts ride lower; wide JA (dialogue boxes) never counts
            // as a column. Unlatched path is byte-identical to before.
            double nameGate = itemOpen ? 0.09 : 0.13;
            double countGate = itemOpen ? 0.05 : 0.13;
            var names = new System.Collections.Generic.List<OcrBlock>();
            var counts = new System.Collections.Generic.List<OcrBlock>();
            foreach (var b in blocks)
            {
                string t = (b.Text ?? "").Trim();
                if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                // Weapon slot labels (みぎて/ひだりて) are equip-box cells,
                // never item columns — without this the weapon box's merged
                // label+name block (tall!) routes into the item grid and its
                // text blanks via the item canonical policy (cited
                // 2026-10-02: whole weapon-open output went blank). Labels
                // never occur in item text, so this is a no-op otherwise.
                if (ContainsWeaponLabel(t, weaponLabels)) continue;
                if ((double)b.Y / fh < 0.5) continue;
                if (itemOpen && (double)b.Width / fw >= 0.60) continue;
                if ((double)b.Height / fh < countGate) continue;
                if ((double)b.Height / fh < nameGate)
                {
                    if (HasDigit(t)) counts.Add(b);
                    continue;
                }
                // Count-ghost drop (cited 2026-10-01: narrow tall ニュース
                // blocks posted as item names 3 runs): a tall narrow block
                // with no digit is a ghosted counts column, never a name —
                // real name columns are 3+ chars wide (shortest cited names
                // run 3 chars ≈ 70px+). Digit blocks still route to counts.
                if ((double)b.Width / fw < 0.065 && !HasDigit(t)) continue;
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
                // Counts pair by geometry, not index (cited 2026-10-01: the
                // counts block sits at its own column's right edge — x671
                // paired left by index while belonging right, starving the
                // right column of the digits its endanchor needed). Overlap
                // wins; else smallest edge gap; tie goes left (index order,
                // the old behavior). Multiple blocks on one column concat
                // (zip gates on agreement anyway, so extras only veto).
                var ctb = new System.Text.StringBuilder();
                foreach (var cb in ccol)
                {
                    int best = -1, bestGap = int.MaxValue;
                    for (int ni = 0; ni < ncol.Count; ni++)
                    {
                        int nx0 = ncol[ni].X, nx1 = ncol[ni].X + ncol[ni].Width;
                        int cx0 = cb.X, cx1 = cb.X + cb.Width;
                        int gap = cx1 < nx0 ? nx0 - cx1 : (nx1 < cx0 ? cx0 - nx1 : 0);
                        if (gap < bestGap) { bestGap = gap; best = ni; }
                    }
                    if (best == i)
                    {
                        if (ctb.Length > 0) ctb.Append(' ');
                        ctb.Append((cb.Text ?? "").Trim());
                    }
                }
                string ct = ctb.ToString();
                cells.Add(ncol[i]);
                cols.Add(nt + "\t" + ct);
            }
            foreach (var cb in ccol) cells.Add(cb);
            return (cells, cols);
        }
        // Slot split for the panel mirror: top reporting bar, bottom-left monster
        // lines, bottom-left action box (overlapping), bottom-right party names.
        // JA mirror keeps count digits with monsters; party slot is JA names only
        // (HP/timebar never ship). Same-Y fragments join one line ("name 3").
        // Noise (ellipsis-only) never ships.
        public static BattleSlots ExtractSlots(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, System.Collections.Generic.IReadOnlyList<string> weaponLabels, System.Func<string, bool> isNoise, bool itemOpen = false)
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
            var itemClaim = ExtractItemColumns(blocks, fw, fh, isJa, itemOpen, weaponLabels);
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
                JoinLinesPaired(weaponLayer.parts));
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
            // B2: ability pool is open-ended (fight first, magic/item last
            // two — indices float with pack length so まどう-class commands
            // join without shifting the fixed rows). Priority = list order.
            string fight = commands[0], magic = commands[commands.Count - 2], item = commands[commands.Count - 1];
            var abilities = new List<string>();
            for (int i = 1; i < commands.Count - 2 && i < commands.Count; i++) abilities.Add(commands[i]);
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
        // Weapon 2×2 grid (cited 2026-10-03 screenshots: row 1 = slot labels
        // みぎて|ひだりて side by side, row 2 = equipped names offset right
        // under their labels). Same Y-grouping as JoinLines, but halves stay
        // split as "left\tRight" pairs for the 2-column render + per-side
        // glossary pins. Split point comes from the widest row (the label
        // row, 2 cells); single-cell rows fall left/right of it. Unknown
        // games never reach here (no labels = no weapon layer at all).
        public static List<string> JoinLinesPaired(List<(int y, int x, int h, string t)> parts)
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
            var ordered = lines.OrderBy(l => l.y).ToList();
            // Split point = midpoint of the widest intra-line gap (the label
            // row's inter-column gap dwarfs intra-cell spacing). No multi-cell
            // line: everything lands left, right holds blank.
            int splitX = int.MaxValue;
            {
                int bestGap = 40, bestMid = int.MaxValue;
                foreach (var ln in ordered)
                {
                    var xs = ln.cells.OrderBy(c => c.x).ToList();
                    for (int i = 1; i < xs.Count; i++)
                    {
                        int gap = xs[i].x - xs[i - 1].x;
                        if (gap > bestGap) { bestGap = gap; bestMid = (xs[i - 1].x + xs[i].x) / 2; }
                    }
                }
                if (bestMid != int.MaxValue) splitX = bestMid;
            }
            var rows = new List<string>();
            foreach (var ln in ordered)
            {
                var left = new System.Collections.Generic.List<string>();
                var right = new System.Collections.Generic.List<string>();
                foreach (var c in ln.cells.OrderBy(c => c.x))
                    (c.x < splitX ? left : right).Add(c.t);
                // No multi-cell row anywhere: everything lands left, right
                // stays a blank hold (positional, like item pairs).
                rows.Add(string.Join(" ", left) + "\t" + string.Join(" ", right));
            }
            return rows;
        }
    }
}