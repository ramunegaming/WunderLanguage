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
        public static int Classify(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, out string topRaw)
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

        public static bool IsBattle(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa)
        {
            string unused;
            return Classify(blocks, fw, fh, isJa, out unused) != 0;
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

        public sealed record BattleSlots(string Top, List<string> Monster, List<string> Action, List<string> Right);

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

        // Slot split for the panel mirror: top reporting bar, bottom-left monster
        // lines, bottom-left action box (overlapping), bottom-right party names.
        // JA mirror keeps count digits with monsters; party slot is JA names only
        // (HP/timebar never ship). Same-Y fragments join one line ("name 3").
        // Noise (ellipsis-only) never ships.
        public static BattleSlots ExtractSlots(List<OcrBlock> blocks, int fw, int fh, System.Func<string, bool> isJa, System.Func<string, bool> isNoise)
        {
            var top = new List<(int x, string t)>();
            var monster = new List<(int y, int x, int h, string t)>();
            var action = new List<(int y, int x, int h, string t)>();
            var right = new List<(int y, int x, int h, string t)>();
            if (blocks == null || fw <= 1 || fh <= 1)
                return new BattleSlots("", new List<string>(), new List<string>(), new List<string>());
            foreach (var b in blocks)
            {
                string t = (b.Text ?? "").Trim();
                if (t.Length < 1 || b.Width < 10 || b.Height < 12) continue;
                if (isNoise(t)) continue;
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
                    if (isJa(t)) right.Add((b.Y, b.X, b.Height, t));
                    continue;
                }
                if (!isJa(t) && !HasDigit(t)) continue;
                if (IsActionCell(b, fh)) action.Add((b.Y, b.X, b.Height, t));
                else monster.Add((b.Y, b.X, b.Height, t));
            }
            top.Sort((a, b2) => a.x.CompareTo(b2.x));
            return new BattleSlots(
                CleanRewardBar(string.Join(" ", top.ConvertAll(p => p.t))),
                JoinLines(monster),
                JoinLines(action),
                JoinLines(right));
        }

        // 4-row action menu: fight / ability / magic-or-blank / item. Merged OCR
        // text segments on pack command vocabulary (cited per game); unknown
        // remainder shows raw in the ability row (truthful over clever).
        // Empty/absent rows stay "" so the panel holds the positional gap.
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
            if (joined.Contains(fight, System.StringComparison.Ordinal)) rows[0] = fight;
            foreach (var a in abilities)
                if (joined.Contains(a, System.StringComparison.Ordinal)) { rows[1] = a; break; }
            if (joined.Contains(magic, System.StringComparison.Ordinal)) rows[2] = magic;
            if (joined.Contains(item, System.StringComparison.Ordinal)) rows[3] = item;
            if (rows[0].Length == 0 && rows[1].Length == 0 && rows[2].Length == 0 && rows[3].Length == 0)
                return new List<string>(clean);
            if (rows[1].Length == 0)
            {
                string rest = joined;
                foreach (var c in commands)
                    rest = rest.Replace(c, "", System.StringComparison.Ordinal);
                rest = rest.Trim();
                if (rest.Length > 0) rows[1] = rest;
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