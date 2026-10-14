using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WunderLanguage.Host.Windows
{
    public partial class SidePanelWindow : Window
    {
        private const double AtBottomThresholdPx = 40;
        private bool _placeholderShown = true;
        private TextBox? _lastJapaneseBlock;
        private TextBox? _lastEnglishBlock;

        public string MenuDisplayMode { get; set; } = "Text";
        private string? _lastMenuRawJa;
        private FrameworkElement? _lastMenuAsciiEntry;
        private StackPanel? _lastMenuAsciiJaHost;
        private StackPanel? _lastMenuAsciiEnHost;
        // Battle frame: one persistent entry, slot hosts refreshed in place.
        // _battleIsLast implements the last-entry rule: any feed append clears
        // it, so a new frame spawns only once other text came between.
        private FrameworkElement? _lastBattleEntry;
        private bool _battleIsLast;
        private StackPanel? _battleJaTopHost;
        private Border? _battleJaTopBox;
        private StackPanel? _battleMonJaHost;
        private Border? _battleMonJaBox;
        private StackPanel? _battleActJaHost;
        private Border? _battleActJaBox;
        private FrameworkElement? _battleLeftJaCell;
        private StackPanel? _battleRightJaHost;
        private Border? _battleItemJaBox;
        private StackPanel? _battleItemJaLeft;
        private StackPanel? _battleItemJaRight;
        private Border? _battleWeaponJaBox;
        private StackPanel? _battleWeaponJaLeft;
        private StackPanel? _battleWeaponJaRight;
        private FrameworkElement? _battleJaHalfGrid;
        private StackPanel? _battleEnTopHost;
        private Border? _battleEnTopBox;
        private StackPanel? _battleMonEnHost;
        private Border? _battleMonEnBox;
        private StackPanel? _battleActEnHost;
        private Border? _battleActEnBox;
        private FrameworkElement? _battleLeftEnCell;
        private StackPanel? _battleRightEnHost;
        private Border? _battleEnItemBox;
        private StackPanel? _battleEnItemLeft;
        private StackPanel? _battleEnItemRight;
        private Border? _battleEnWeaponBox;
        private StackPanel? _battleEnWeaponLeft;
        private StackPanel? _battleEnWeaponRight;
        private FrameworkElement? _battleEnHalfGrid;
        // Battle boxes mirror small in-game pixel text: one game row must stay
        // one panel row ("name 3" never wraps). Single knob for all battle text.
        private const int BattleFontSize = 9;

        private const int MaxAsciiInner = 28;

        private static int DisplayWidth(string s)
        {
            int w = 0;
            foreach (char c in s)
            {
                if ((c >= '\u3040' && c <= '\u30FF') || (c >= '\u4E00' && c <= '\u9FFF')
                    || (c >= '\uFF00' && c <= '\uFFEF')) w += 2;
                else w += 1;
            }
            return w;
        }

        private static List<string> WrapAsciiLine(string line, int maxInner)
        {
            line = line.TrimEnd('\r');
            var out_ = new List<string>();
            if (DisplayWidth(line) <= maxInner || maxInner <= 4)
            {
                out_.Add(line);
                return out_;
            }
            int i = 0;
            while (i < line.Length)
            {
                int w = 0;
                int lastSpace = -1;
                int j = i;
                while (j < line.Length)
                {
                    char c = line[j];
                    int cw = ((c >= '\u3040' && c <= '\u30FF') || (c >= '\u4E00' && c <= '\u9FFF')
                        || (c >= '\uFF00' && c <= '\uFFEF')) ? 2 : 1;
                    if (w + cw > maxInner) break;
                    w += cw;
                    if (c == ' ') lastSpace = j;
                    j++;
                }
                if (j >= line.Length) { out_.Add(line.Substring(i)); break; }
                // Prefer space break for Latin, anywhere for CJK.
                if (lastSpace > i && j - lastSpace < 10) { out_.Add(line.Substring(i, lastSpace - i)); i = lastSpace + 1; }
                else { out_.Add(line.Substring(i, j - i)); i = j; }
            }
            return out_;
        }

        private static string FormatAsciiBox(string japanese, string english)
        {
            var wrappedJa = new List<string>();
            foreach (var l in japanese.Split('\n'))
                wrappedJa.AddRange(WrapAsciiLine(l, MaxAsciiInner));
            var wrappedEn = new List<string>();
            foreach (var l in english.Split('\n'))
                wrappedEn.AddRange(WrapAsciiLine(l, MaxAsciiInner));
            int w = 4;
            foreach (var l in wrappedJa) w = Math.Max(w, DisplayWidth(l) + 2);
            foreach (var l in wrappedEn) w = Math.Max(w, DisplayWidth(l) + 2);
            w = Math.Min(w, MaxAsciiInner);
            var sb = new StringBuilder();
            sb.AppendLine("┌" + new string('─', w) + "┐");
            foreach (var l in wrappedJa) sb.AppendLine("│ " + l + new string(' ', Math.Max(0, w - 2 - DisplayWidth(l))) + " │");
            sb.AppendLine("├" + new string('─', w) + "┤");
            foreach (var l in wrappedEn) sb.AppendLine("│ " + l + new string(' ', Math.Max(0, w - 2 - DisplayWidth(l))) + " │");
            sb.Append("└" + new string('─', w) + "┘");
            return sb.ToString();
        }

        // Full-history copy buffer. UI stays per-box selectable; this is what
        // Ctrl+Shift+C copies (timestamps included). Updated in place on growth.
        private readonly List<(string time, string ja, string en)> _history = new();

        public SidePanelWindow()
        {
            InitializeComponent();
            // Bubbles from child TextBoxes (they only handle plain Ctrl+C).
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                    && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    CopyAllHistory();
                    e.Handled = true;
                }
            };
        }

        private void CopyAllHistory()
        {
            Dispatcher.Invoke(() =>
            {
                var sb = new StringBuilder();
                foreach (var (time, ja, en) in _history)
                {
                    sb.AppendLine(time);
                    sb.AppendLine("IN-GAME TEXT");
                    sb.AppendLine(NormalizeEllipsisForDisplay(ja));
                    sb.AppendLine("TRANSLATION");
                    sb.AppendLine(en);
                    sb.AppendLine();
                }
                try { Clipboard.SetText(sb.ToString()); }
                catch { /* clipboard busy - try again next time */ }
            });
        }

        public void ShowMenu(string kind, string japanese, string english)
        {
            Dispatcher.Invoke(() =>
            {
                if (_placeholderShown)
                {
                    HistoryPanel.Children.Clear();
                    _placeholderShown = false;
                }
                bool wasAtBottom = HistoryScroller.VerticalOffset >= HistoryScroller.ScrollableHeight - AtBottomThresholdPx;
                string stamp = System.DateTime.Now.ToString("HH:mm:ss");
                _battleIsLast = false;
                if (MenuDisplayMode == "Ascii")
                {
                    var asciiEntry = BuildAsciiMenuEntry(stamp, kind, SplitMenuLines(japanese), SplitMenuLines(english));
                    HistoryPanel.Children.Add(asciiEntry);
                    _lastMenuAsciiEntry = asciiEntry;
                    _lastMenuRawJa = japanese;
                    _lastJapaneseBlock = null;
                    _lastEnglishBlock = null;
                    _history.Add((stamp, kind + "\n" + japanese, english));
                    if (wasAtBottom)
                        Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                    return;
                }
                var (entry, jaBlock, enBlock) = BuildEntry(stamp, japanese, english, kind);
                HistoryPanel.Children.Add(entry);
                _lastJapaneseBlock = jaBlock;
                _lastEnglishBlock = enBlock;
                _lastMenuRawJa = null;
                _lastMenuAsciiEntry = null;
                _lastMenuAsciiJaHost = null;
                _lastMenuAsciiEnHost = null;
                _history.Add((stamp, kind + "\n" + japanese, english));
                if (wasAtBottom)
                    Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
            });
        }

        // Menu growth: edit in place on match, DROP on mismatch (never appends,
        // so menus can never duplicate dialogue).
        public void UpdateMenuIfMatches(string expectedOldJa, string kind, string japanese, string english)
        {
            Dispatcher.Invoke(() =>
            {
                if (MenuDisplayMode == "Ascii")
                {
                    if (_lastMenuRawJa is null || _lastMenuAsciiJaHost is null || _lastMenuAsciiEnHost is null) return;
                    if (NormalizeEllipsisForDisplay(_lastMenuRawJa) != NormalizeEllipsisForDisplay(expectedOldJa)) return;
                    RefreshAsciiRows(_lastMenuAsciiJaHost, SplitMenuLines(japanese), 13);
                    RefreshAsciiRows(_lastMenuAsciiEnHost, SplitMenuLines(english), 13);
                    _lastMenuRawJa = japanese;
                    if (_history.Count > 0)
                        _history[^1] = (_history[^1].time, kind + "\n" + japanese, english);
                    Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                    return;
                }
                if (_lastJapaneseBlock is null || _lastEnglishBlock is null) return;
                if (NormalizeEllipsisForDisplay(_lastJapaneseBlock.Text) != NormalizeEllipsisForDisplay(expectedOldJa)) return;
                _lastJapaneseBlock.Text = NormalizeEllipsisForDisplay(japanese);
                _lastEnglishBlock.Text = english;
                if (_history.Count > 0)
                    _history[^1] = (_history[^1].time, japanese, english);
                Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
            });
        }


        // Battle frame: JA mirror on top (monster peeking behind the overlapping
        // action box, positions 1:1), EN mirror below. Refreshed in place while
        // it is the last entry; anything else between spawns a new frame.
        // Every call logs its branch so a stale frame is always explainable.
        // Battle frame is ONE persistent thing: top bar, bottom-left
        // (monster + overlapping action menu), bottom-right stay drawn in
        // fixed positions for the whole fight. Only CONTENTS refresh — the
        // action box alone despawns on cooldown and reappears after.
        private void RefreshBattleHalf(StackPanel topHost, Border topBox, StackPanel monHost, Border monBox, StackPanel actHost, Border actBox, FrameworkElement leftCell, StackPanel rightHost, Border itemBox, StackPanel itemLeft, StackPanel itemRight, Border weaponBox, StackPanel weaponLeft, StackPanel weaponRight, FrameworkElement halfGrid, string top, List<string> mon, List<string> act, List<string> right, List<string> item, List<string> weapon)
        {
            RefreshAsciiRows(topHost, string.IsNullOrWhiteSpace(top) ? new List<string>() : new List<string> { top }, BattleFontSize, true);
            topBox.Visibility = Visibility.Visible;
            RefreshAsciiRows(monHost, mon, BattleFontSize, true);
            RefreshAsciiRows(actHost, ActRows(act), BattleFontSize, true);
            RefreshAsciiRows(rightHost, right, BattleFontSize, true);
            RefreshItemBox(itemBox, itemLeft, itemRight, item);
            RefreshWeaponBox(weaponBox, weaponLeft, weaponRight, weapon);
            // Mockup layering: behind boxes always stay drawn — a new box
            // opens ON TOP (action over monster, item over action+bottom row,
            // weapon over item) and despawns on close, never replacing the
            // row underneath.
            halfGrid.Visibility = Visibility.Visible;
            monBox.Visibility = Visibility.Visible;
            FitBattleRows(monHost, monBox);
            FitBattleRows(actHost, actBox);
            actBox.Visibility = act.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            leftCell.Visibility = Visibility.Visible;
        }

        // Blank action rows (absent magic) render as a full-height ideographic
        // space: the positional gap stays visible instead of collapsing.
        private static List<string> ActRows(List<string> act)
        {
            var rows = new List<string>(act.Count);
            foreach (var a in act) rows.Add(a.Length == 0 ? "　" : a);
            return rows;
        }
        // One game row stays one panel row: shrink a battle box's rows
        // uniformly to fit its widest row, measured with real font metrics
        // against the real box width — no pixel math, no DPI/fallback-font
        // guessing. Floor + NoWrap: rows shrink, never wrap, never clip.
        // Collapsed boxes (closed overlays) have no width by design — skip
        // silently. Deferred to Render (after layout) so ActualWidth is exact;
        // retries cover the first tick before layout lands.
        private void FitBattleRows(StackPanel host, Border box, double widthDiv = 1.0, int attempt = 0)
        {
            try
            {
                if (box.Visibility != Visibility.Visible) return;
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (box.Visibility != Visibility.Visible) return;
                        double avail = (box.ActualWidth - box.Padding.Left - box.Padding.Right) / widthDiv;
                        if (avail <= 20)
                        {
                            if (attempt < 5) FitBattleRows(host, box, widthDiv, attempt + 1);
                            else FileLogger.Log($"[battle] autofit skip avail={avail:0} rows={host.Children.Count}");
                            return;
                        }
                        var dpi = VisualTreeHelper.GetDpi(this);
                        var tf = new Typeface(new FontFamily("MS Gothic, NSimSun, Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                        double ratio = 1.0;
                        var tbs = new List<TextBlock>();
                        foreach (var ch in host.Children)
                        {
                            if (ch is not TextBlock tb) continue;
                            tbs.Add(tb);
                            string t = tb.Text ?? "";
                            if (string.IsNullOrWhiteSpace(t)) continue;
                            var ft = new FormattedText(t, System.Globalization.CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight, tf, tb.FontSize, Brushes.Black, dpi.PixelsPerDip);
                            if (ft.Width > avail) ratio = System.Math.Min(ratio, avail / ft.Width);
                        }
                        if (ratio < 1.0)
                        {
                            double fs = System.Math.Max(6.5, BattleFontSize * ratio);
                            FileLogger.Log($"[battle] autofit shrink avail={avail:0} ratio={ratio:0.00} rows={tbs.Count} fs={fs:0.0}");
                            foreach (var tb in tbs) tb.FontSize = fs;
                        }
                    }
                    catch { }
                }, DispatcherPriority.Render);
            }
            catch { }
        }

        // Item box: full-width 2-column grid between the top bar and the
        // bottom row. Pairs arrive "left\tRight"; missing sides render
        // full-height blank so rows stay aligned. Despawns on close like the
        // action box; scroll states just refresh in place.
        private static List<(string l, string r)> SplitItemPairs(List<string> pairs)
        {
            var out_ = new List<(string l, string r)>(pairs?.Count ?? 0);
            foreach (var q in pairs ?? new List<string>())
            {
                string p = q ?? "";
                int ti = p.IndexOf('\t');
                string l = ti < 0 ? p : p.Substring(0, ti);
                string r = ti < 0 ? "" : p.Substring(ti + 1);
                if (l.Length == 0) l = "　";
                if (r.Length == 0) r = "　";
                out_.Add((l, r));
            }
            return out_;
        }
        private void RefreshItemBox(Border itemBox, StackPanel itemLeft, StackPanel itemRight, List<string> item)
        {
            var rows = SplitItemPairs(item);
            RefreshAsciiRows(itemLeft, rows.ConvertAll(p => p.l), BattleFontSize, true);
            RefreshAsciiRows(itemRight, rows.ConvertAll(p => p.r), BattleFontSize, true);
            itemBox.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            FitBattleRows(itemLeft, itemBox, 2.0);
            FitBattleRows(itemRight, itemBox, 2.0);
        }
        // Weapon box (cited 2026-10-03 screenshots: 2 columns × 2 rows —
        // row 1 slot labels みぎて|ひだりて side by side, row 2 equipped
        // names offset right underneath). Same 2-column grid + peek strip
        // as the item box (Margin-top 12 over the item box below), Z3.
        // Despawns on close; selection changes just refresh in place.
        private void RefreshWeaponBox(Border weaponBox, StackPanel weaponLeft, StackPanel weaponRight, List<string> weapon)
        {
            var rows = SplitItemPairs(weapon);
            RefreshAsciiRows(weaponLeft, rows.ConvertAll(p => p.l), BattleFontSize, true);
            RefreshAsciiRows(weaponRight, rows.ConvertAll(p => p.r), BattleFontSize, true);
            weaponBox.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            FitBattleRows(weaponLeft, weaponBox, 2.0);
            FitBattleRows(weaponRight, weaponBox, 2.0);
        }
        private void BuildWeaponBox(Panel parent, Brush textBrush, Brush coverBrush, List<string> weapon, out Border weaponBox, out StackPanel weaponLeft, out StackPanel weaponRight)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) });
            weaponLeft = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
            weaponRight = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetColumn(weaponRight, 1);
            grid.Children.Add(weaponLeft);
            grid.Children.Add(weaponRight);
            weaponBox = new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 12, 0, 0),
                Background = coverBrush,
                Visibility = Visibility.Collapsed,
                // Top-anchored, not stretched: the weapon box hugs its 2
                // content rows at the cell top (cited 2026-10-03 game shot:
                // weapon box covers the upper box, item row 4 stays visible
                // below). The item box (Z2, full-cell) stays drawn behind;
                // its bottom rows show through under this box's bottom edge.
                VerticalAlignment = VerticalAlignment.Top,
                Child = grid
            };
            Panel.SetZIndex(weaponBox, 3);
            parent.Children.Add(weaponBox);
            RefreshWeaponBox(weaponBox, weaponLeft, weaponRight, weapon);
        }
        private void BuildItemBox(Panel parent, Brush textBrush, Brush coverBrush, List<string> item, out Border itemBox, out StackPanel itemLeft, out StackPanel itemRight)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) });
            itemLeft = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
            itemRight = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetColumn(itemRight, 1);
            grid.Children.Add(itemLeft);
            grid.Children.Add(itemRight);
            itemBox = new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                // Peek strip: the cover sits 12px below the cell top so the
                // top borders of the boxes beneath stay visible (in-game box
                // stack effect) instead of a full replace.
                Margin = new Thickness(0, 12, 0, 0),
                Background = coverBrush,
                Visibility = Visibility.Collapsed,
                Child = grid
            };
            Panel.SetZIndex(itemBox, 2);
            parent.Children.Add(itemBox);
            RefreshItemBox(itemBox, itemLeft, itemRight, item);
        }
        private void BuildBattleHalf(StackPanel parent, Brush textBrush, Brush innerBrush, string top, List<string> mon, List<string> act, List<string> right, List<string> item, List<string> weapon, bool withBottomMargin, out StackPanel topHost, out Border topBox, out StackPanel monHost, out Border monBox, out StackPanel actHost, out Border actBox, out FrameworkElement leftCell, out StackPanel rightHost, out Border itemBox, out StackPanel itemLeft, out StackPanel itemRight, out Border weaponBox, out StackPanel weaponLeft, out StackPanel weaponRight, out FrameworkElement halfGrid)
        {
            topHost = new StackPanel();
            topBox = new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Visible
            };
            if (!string.IsNullOrWhiteSpace(top))
                AddAsciiRows(topHost, new List<string> { top }, BattleFontSize, true);
            topBox.Child = topHost;
            parent.Children.Add(topBox);
            // One bottom cell, Z layers: the half grid is always drawn at Z0;
            // the action box (Z1), item box (Z2) and weapon box (Z3) are opaque
            // covers in the SAME cell — a new box opens ABOVE the previous
            // (a layer above, not north of it), and the row underneath returns
            // current on close. No layout jumps, no replace.
            var bottomCell = new Grid { Margin = withBottomMargin ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 0) };
            var half = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            // 38/62 mirrors the in-game bottom boxes (monster ~37% measured);
            // doubles as shrink room so one game row stays one panel row.
            half.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
            half.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
            // Left cell: the monster box fills it edge to edge. The action box
            // no longer squeezes in beside it — it overlays in front (actLayer
            // Z1 below, same 38/62 split so it lands where the game puts it).
            var cell = new Grid { Margin = new Thickness(0, 0, 4, 0) };
            monBox = new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                ClipToBounds = true,
                Visibility = Visibility.Visible
            };
            monHost = new StackPanel();
            AddAsciiRows(monHost, mon, BattleFontSize, true);
            monBox.Child = monHost;
            cell.Children.Add(monBox);
            Grid.SetColumn(cell, 0);
            half.Children.Add(cell);
            leftCell = cell;
            var rightBox = new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(4, 0, 0, 0)
            };
            rightHost = new StackPanel();
            AddAsciiRows(rightHost, right, BattleFontSize, true);
            rightBox.Child = rightHost;
            Grid.SetColumn(rightBox, 1);
            half.Children.Add(rightBox);
            bottomCell.Children.Add(half);
            // Action overlay (mockup state 1): in front of the half-grid (Z1),
            // under item (Z2) / weapon (Z3). Same 38/62 split as half so the
            // box sits over the monster zone where the game puts it.
            // Collapsed when closed so the monster box shows through
            // untouched; one row per command, the blank magic slot holding
            // the positional gap (まほう posts there on magic characters).
            var actLayer = new Grid { Margin = new Thickness(0, 0, 0, 0), IsHitTestVisible = false };
            actLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
            actLayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
            actBox = new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(14, 4, 6, 4),
                Background = innerBrush,
                Visibility = act.Count == 0 ? Visibility.Collapsed : Visibility.Visible
            };
            actHost = new StackPanel();
            AddAsciiRows(actHost, ActRows(act), BattleFontSize, true);
            actBox.Child = actHost;
            Grid.SetColumn(actBox, 0);
            actLayer.Children.Add(actBox);
            Panel.SetZIndex(actLayer, 1);
            bottomCell.Children.Add(actLayer);
            FitBattleRows(monHost, monBox);
            FitBattleRows(actHost, actBox);
            BuildItemBox(bottomCell, textBrush, innerBrush, item, out itemBox, out itemLeft, out itemRight);
            BuildWeaponBox(bottomCell, textBrush, innerBrush, weapon, out weaponBox, out weaponLeft, out weaponRight);
            parent.Children.Add(bottomCell);
            // Behind boxes always stay drawn (Z0); covers toggle above them.
            // The row keeps refreshing silently underneath either way.
            halfGrid = half;
            halfGrid.Visibility = Visibility.Visible;
        }

        public void ShowOrUpdateBattleFrame(string topJa, string topEn, List<string> monJa, List<string> monEn, List<string> actJa, List<string> actEn, List<string> rightJa, List<string> rightEn, List<string> itemJa, List<string> itemEn, List<string> weaponJa, List<string> weaponEn)
        {
            Dispatcher.Invoke(() =>
            {
                if (_placeholderShown)
                {
                    HistoryPanel.Children.Clear();
                    _placeholderShown = false;
                }
                bool wasAtBottom = HistoryScroller.VerticalOffset >= HistoryScroller.ScrollableHeight - AtBottomThresholdPx;
                string stamp = System.DateTime.Now.ToString("HH:mm:ss");
                string jaSummary = "BATTLE\n" + topJa + "\n" + string.Join("\n", monJa) + "\n" + string.Join("\n", actJa) + "\n" + string.Join("\n", rightJa) + "\n" + string.Join("\n", itemJa) + "\n" + string.Join("\n", weaponJa);
                string enSummary = topEn + "\n" + string.Join("\n", monEn) + "\n" + string.Join("\n", actEn) + "\n" + string.Join("\n", rightEn) + "\n" + string.Join("\n", itemEn) + "\n" + string.Join("\n", weaponEn);
                if (_battleIsLast && _lastBattleEntry != null
                    && _battleJaTopHost != null && _battleJaTopBox != null
                    && _battleMonJaHost != null && _battleMonJaBox != null && _battleActJaHost != null && _battleActJaBox != null && _battleLeftJaCell != null && _battleRightJaHost != null
                    && _battleItemJaBox != null && _battleItemJaLeft != null && _battleItemJaRight != null && _battleWeaponJaBox != null && _battleWeaponJaLeft != null && _battleWeaponJaRight != null && _battleJaHalfGrid != null
                    && _battleEnTopHost != null && _battleEnTopBox != null
                    && _battleMonEnHost != null && _battleMonEnBox != null && _battleActEnHost != null && _battleActEnBox != null && _battleLeftEnCell != null && _battleRightEnHost != null
                    && _battleEnItemBox != null && _battleEnItemLeft != null && _battleEnItemRight != null && _battleEnWeaponBox != null && _battleEnWeaponLeft != null && _battleEnWeaponRight != null && _battleEnHalfGrid != null)
                {
                    RefreshBattleHalf(_battleJaTopHost, _battleJaTopBox, _battleMonJaHost, _battleMonJaBox, _battleActJaHost, _battleActJaBox, _battleLeftJaCell, _battleRightJaHost, _battleItemJaBox, _battleItemJaLeft, _battleItemJaRight, _battleWeaponJaBox, _battleWeaponJaLeft, _battleWeaponJaRight, _battleJaHalfGrid, topJa, monJa, actJa, rightJa, itemJa, weaponJa);
                    RefreshBattleHalf(_battleEnTopHost, _battleEnTopBox, _battleMonEnHost, _battleMonEnBox, _battleActEnHost, _battleActEnBox, _battleLeftEnCell, _battleRightEnHost, _battleEnItemBox, _battleEnItemLeft, _battleEnItemRight, _battleEnWeaponBox, _battleEnWeaponLeft, _battleEnWeaponRight, _battleEnHalfGrid, topEn, monEn, actEn, rightEn, itemEn, weaponEn);
                    if (_history.Count > 0)
                        _history[^1] = (_history[^1].time, jaSummary, enSummary);
                    FileLogger.Log($"[battle] panel refresh top={topJa.Length}ch mon={monJa.Count} act={actJa.Count} right={rightJa.Count} item={itemJa.Count} weapon={weaponJa.Count}");
                    if (wasAtBottom)
                        Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                    return;
                }
                var subtleBrush = (Brush)FindResource("SubtleTextBrush");
                var textBrush = (Brush)FindResource("TextBrush");
                var borderBrush = (Brush)FindResource("BorderBrush");
                var innerBrush = (Brush)FindResource("InnerPanelBrush");
                var container = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
                if (HistoryPanel.Children.Count > 0)
                {
                    container.Children.Add(new Border
                    {
                        BorderBrush = borderBrush,
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Margin = new Thickness(0, 0, 0, 16)
                    });
                }
                container.Children.Add(new TextBlock
                {
                    Text = stamp,
                    FontSize = 10,
                    Foreground = subtleBrush,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                container.Children.Add(new TextBlock
                {
                    Text = "BATTLE",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = subtleBrush,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                var outer = new Border
                {
                    BorderBrush = textBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 10, 12, 10)
                };
                var inner = new StackPanel();
                BuildBattleHalf(inner, textBrush, innerBrush, topJa, monJa, actJa, rightJa, itemJa, weaponJa, true, out var jaTopHost, out var jaTopBox, out var monJaHost, out var monJaBox, out var actJaHost, out var actJaBox, out var leftJaCell, out var rightJaHost, out var jaItemBox, out var jaItemLeft, out var jaItemRight, out var jaWeaponBox, out var jaWeaponLeft, out var jaWeaponRight, out var jaHalfGrid);
                inner.Children.Add(new Border
                {
                    BorderBrush = textBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(0, 8, 0, 8)
                });
                BuildBattleHalf(inner, textBrush, innerBrush, topEn, monEn, actEn, rightEn, itemEn, weaponEn, false, out var enTopHost, out var enTopBox, out var monEnHost, out var monEnBox, out var actEnHost, out var actEnBox, out var leftEnCell, out var rightEnHost, out var enItemBox, out var enItemLeft, out var enItemRight, out var enWeaponBox, out var enWeaponLeft, out var enWeaponRight, out var enHalfGrid);
                outer.Child = inner;
                container.Children.Add(outer);
                HistoryPanel.Children.Add(container);
                _lastBattleEntry = container;
                _battleIsLast = true;
                _battleJaTopHost = jaTopHost;
                _battleJaTopBox = jaTopBox;
                _battleMonJaHost = monJaHost;
                _battleMonJaBox = monJaBox;
                _battleActJaHost = actJaHost;
                _battleActJaBox = actJaBox;
                _battleLeftJaCell = leftJaCell;
                _battleRightJaHost = rightJaHost;
                _battleItemJaBox = jaItemBox;
                _battleItemJaLeft = jaItemLeft;
                _battleItemJaRight = jaItemRight;
                _battleWeaponJaBox = jaWeaponBox;
                _battleWeaponJaLeft = jaWeaponLeft;
                _battleWeaponJaRight = jaWeaponRight;
                _battleJaHalfGrid = jaHalfGrid;
                _battleEnTopHost = enTopHost;
                _battleEnTopBox = enTopBox;
                _battleMonEnHost = monEnHost;
                _battleMonEnBox = monEnBox;
                _battleActEnHost = actEnHost;
                _battleActEnBox = actEnBox;
                _battleLeftEnCell = leftEnCell;
                _battleRightEnHost = rightEnHost;
                _battleEnItemBox = enItemBox;
                _battleEnItemLeft = enItemLeft;
                _battleEnItemRight = enItemRight;
                _battleEnWeaponBox = enWeaponBox;
                _battleEnWeaponLeft = enWeaponLeft;
                _battleEnWeaponRight = enWeaponRight;
                _battleEnHalfGrid = enHalfGrid;
                _history.Add((stamp, jaSummary, enSummary));
                FileLogger.Log($"[battle] panel new top={topJa.Length}ch mon={monJa.Count} act={actJa.Count} right={rightJa.Count} item={itemJa.Count} weapon={weaponJa.Count}");
                if (wasAtBottom)
                    Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
            });
        }

        public void ShowTranslation(string japanese, string english)
        {
            Dispatcher.Invoke(() =>
            {
                if (_placeholderShown)
                {
                    HistoryPanel.Children.Clear();
                    _placeholderShown = false;
                }

                bool wasAtBottom = HistoryScroller.VerticalOffset >= HistoryScroller.ScrollableHeight - AtBottomThresholdPx;

                string stamp = System.DateTime.Now.ToString("HH:mm:ss");
                // B16b bubble absorption (2026-10-15, supersedes growth-merge:
                // async translations complete OUT OF ORDER, so arrival-order
                // merging misses (ここに prefix1+prefix2 posted as two, update
                // revised only one). Order-independent: fold by CONTENT
                // against recent plain-dialogue bubbles (ring with element
                // refs). Fire extends a recent bubble -> revise it. Fire
                // covered by a recent bubble -> drop it (already shown).
                // Strict stripped containment +6, 15s window (growth bursts
                // are fast; translation variance delays matches ~9s live;
                // distinct turns are slower). Display-only: capture,
                // translation, sim untouched. Revert: delete ring + blocks.
                try
                {
                    string incomingStripped = ConFold(StripOverlapPunct(StripSpaces(japanese)));
                    for (int ri = _bubbleRing.Count - 1; ri >= 0; ri--)
                    {
                        var rb = _bubbleRing[ri];
                        if (GapSeconds(rb.time, stamp) > 15) continue;
                        string shownStripped = ConFold(StripOverlapPunct(StripSpaces(rb.jaRaw)));
                        if (shownStripped.Length < 6 || incomingStripped.Length < 6) continue;
                        if (incomingStripped.Length - shownStripped.Length >= 6
                            && incomingStripped.StartsWith(shownStripped, System.StringComparison.Ordinal))
                        {
                            rb.ja.Text = NormalizeEllipsisForDisplay(japanese);
                            rb.en.Text = english;
                            ReplaceHistoryRow(rb.time, rb.jaRaw, stamp, japanese, english);
                            _lastJapaneseBlock = rb.ja;
                            _lastEnglishBlock = rb.en;
                            try { _bubbleRing.RemoveAt(ri); } catch { }
                            try { _bubbleRing.Add((rb.entry, rb.ja, rb.en, stamp, japanese)); } catch { }
                            TrimBubbleRing();
                            FileLogger.Log("[panel] absorb-growth (fire extends recent bubble, revised in place)");
                            if (wasAtBottom)
                                Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                            return;
                        }
                        if (shownStripped.Length - incomingStripped.Length >= 0
                            && shownStripped.Contains(incomingStripped, System.StringComparison.Ordinal)
                            && shownStripped.Length > incomingStripped.Length)
                        {
                            FileLogger.Log("[panel] absorb-drop (fire already covered by recent bubble)");
                            return;
                        }
                    }
                }
                catch { }
                _battleIsLast = false;
                var (entry, jaBlock, enBlock) = BuildEntry(stamp, japanese, english);
                HistoryPanel.Children.Add(entry);
                _lastJapaneseBlock = jaBlock;
                _lastEnglishBlock = enBlock;
                _lastMenuRawJa = null;
                _lastMenuAsciiEntry = null;
                _lastMenuAsciiJaHost = null;
                _lastMenuAsciiEnHost = null;
                _history.Add((stamp, japanese, english));
                try { _bubbleRing.Add((entry, jaBlock, enBlock, stamp, japanese)); } catch { }
                TrimBubbleRing();

                if (wasAtBottom)
                {
                    Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                }
            });
        }

        // Terminal-state notice (2026-10-13): engine-dead panel line.
        // System row, never translation data: not added to _history,
        // so updates/clears/replay treat it as chrome, and corpus miner
        // never sees it (event path bypasses CorpusRecord entirely).
        // One-shot per session; no focus steal (no popup mid-game).
        // Revert: delete method + subscription.
        private bool _noticeShown;
        public void ShowNotice(string text)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_noticeShown) return;
                    _noticeShown = true;
                    if (_placeholderShown)
                    {
                        HistoryPanel.Children.Clear();
                        _placeholderShown = false;
                    }
                    HistoryPanel.Children.Add(new TextBlock
                    {
                        TextWrapping = System.Windows.TextWrapping.Wrap,
                        FontSize = 13,
                        FontStyle = System.Windows.FontStyles.Italic,
                        Foreground = (Brush)FindResource("TextBrush"),
                        Margin = new Thickness(0, 6, 0, 6),
                        Text = "⚠ " + text
                    });
                    HistoryScroller.ScrollToEnd();
                }
                catch { }
            });
        }

        public void ClearHistory()
        {
            _noticeShown = false;
            Dispatcher.Invoke(() =>
            {
                HistoryPanel.Children.Clear();
                _history.Clear();
                _bubbleRing.Clear();
                _placeholderShown = true;
                _battleIsLast = false;
                _lastBattleEntry = null;
                _lastJapaneseBlock = null;
                _lastEnglishBlock = null;
                _lastMenuRawJa = null;
                HistoryPanel.Children.Add(new TextBlock
                {
                    Name = "PlaceholderText",
                    Text = "Waiting for dialogue...",
                    FontSize = 14,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("SubtleTextBrush")
                });
            });
        }

        // Growth update with identity check: only edits the bubble if it still
        // shows the text this translation grew from. If a newer entry landed
        // while translation was in flight, appends instead of overwriting it.
        public void UpdateLastTranslationIfMatches(string expectedOldJa, string japanese, string english)
        {
            Dispatcher.Invoke(() =>
            {
                if (_lastJapaneseBlock is null || _lastEnglishBlock is null)
                {
                    ShowTranslation(japanese, english);
                    return;
                }
                if (NormalizeEllipsisForDisplay(_lastJapaneseBlock.Text) != NormalizeEllipsisForDisplay(expectedOldJa))
                {
                    // Deeper-history match (dupe fix): async completion for an older bubble
                    // updates its history row in place instead of appending a dupe entry.
                    string want = NormalizeEllipsisForDisplay(expectedOldJa);
                    for (int i = _history.Count - 1; i >= 0 && i >= _history.Count - 8; i--)
                    {
                        string hja = _history[i].ja;
                        int nl = hja.IndexOf('\n');
                        string hbody = nl >= 0 ? hja.Substring(nl + 1) : hja;
                        if (NormalizeEllipsisForDisplay(hbody) == want)
                        {
                            _history[i] = (_history[i].time, nl >= 0 ? hja.Substring(0, nl + 1) + japanese : japanese, english);
                            return;
                        }
                    }
                    ShowTranslation(japanese, english);
                    return;
                }
                _lastJapaneseBlock.Text = NormalizeEllipsisForDisplay(japanese);
                _lastEnglishBlock.Text = english;
                if (_history.Count > 0)
                    _history[^1] = (_history[^1].time, japanese, english);
                AbsorbContained(_lastJapaneseBlock, japanese, System.DateTime.Now.ToString("HH:mm:ss"), wasAtBottom: false);
                Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
            });
        }

        // B16b absorption sweep: after a revise, fold any OTHER recent plain
        // bubble strictly contained in the new text (stale growth prefixes
        // the primary match did not cover). Order-independent convergence:
        // whichever lands last folds the earlier. Display-only.
        private void AbsorbContained(TextBox? keep, string japanese, string stamp, bool wasAtBottom)
        {
            try
            {
                string incomingStripped = ConFold(StripOverlapPunct(StripSpaces(japanese)));
                if (incomingStripped.Length < 12) return;
                var dropIdx = new System.Collections.Generic.List<int>();
                for (int ri = 0; ri < _bubbleRing.Count; ri++)
                {
                    var rb = _bubbleRing[ri];
                    if (ReferenceEquals(rb.ja, keep)) continue;
                    if (GapSeconds(rb.time, stamp) > 15) continue;
                    string shownStripped = ConFold(StripOverlapPunct(StripSpaces(rb.jaRaw)));
                    if (shownStripped.Length < 6) continue;
                    if (incomingStripped.Length - shownStripped.Length >= 6
                        && incomingStripped.Contains(shownStripped, System.StringComparison.Ordinal))
                        dropIdx.Add(ri);
                }
                for (int di = dropIdx.Count - 1; di >= 0; di--)
                {
                    try
                    {
                        var rb = _bubbleRing[dropIdx[di]];
                        HistoryPanel.Children.Remove(rb.entry);
                        RemoveHistoryRow(rb.time, rb.jaRaw);
                        _bubbleRing.RemoveAt(dropIdx[di]);
                        FileLogger.Log("[panel] absorb-fold (stale prefix folded into revision)");
                    }
                    catch { }
                }
                if (dropIdx.Count > 0 && wasAtBottom)
                    Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
            }
            catch { }
        }

                private static List<string> SplitMenuLines(string s)
        {
            var out_ = new List<string>();
            foreach (var raw in s.Split('\n'))
                out_.Add(raw.TrimEnd('\r'));
            return out_;
        }

        // Battle rows pass noWrap=true: one game row stays one panel row —
        // shrink-to-fit (FitBattleRows) instead of wrapping. Menus keep Wrap.
        private void AddAsciiRows(StackPanel host, List<string> lines, int fontSize, bool noWrap = false)
        {
            var textBrush = (Brush)FindResource("TextBrush");
            var mono = new FontFamily("MS Gothic, NSimSun, Consolas, Courier New");
            foreach (var line in lines)
            {
                // Intentional battle gaps ("　") render as full-height blank
                // rows so 4-line menus hold their slots; everywhere else a
                // short spacer stays.
                if (string.IsNullOrWhiteSpace(line) && !(noWrap && line == "　"))
                {
                    host.Children.Add(new Border { Height = 10 });
                    continue;
                }
                host.Children.Add(new TextBlock
                {
                    Text = line,
                    FontFamily = mono,
                    FontSize = fontSize,
                    Foreground = textBrush,
                    TextWrapping = noWrap ? TextWrapping.NoWrap : TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }
        }

        private void RefreshAsciiRows(StackPanel host, List<string> lines, int fontSize, bool noWrap = false)
        {
            host.Children.Clear();
            AddAsciiRows(host, lines, fontSize, noWrap);
        }

        private FrameworkElement BuildAsciiMenuEntry(string stamp, string kind, List<string> jaLines, List<string> enLines)
        {
            var subtleBrush = (Brush)FindResource("SubtleTextBrush");
            var textBrush = (Brush)FindResource("TextBrush");
            var borderBrush = (Brush)FindResource("BorderBrush");
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            if (HistoryPanel.Children.Count > 0)
            {
                container.Children.Add(new Border
                {
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 0, 0, 16)
                });
            }
            container.Children.Add(new TextBlock
            {
                Text = stamp,
                FontSize = 10,
                Foreground = subtleBrush,
                Margin = new Thickness(0, 0, 0, 4)
            });
            container.Children.Add(new TextBlock
            {
                Text = kind,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = subtleBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var outer = new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10)
            };
            var inner = new StackPanel();
            var jaHost = new StackPanel();
            AddAsciiRows(jaHost, jaLines, 13);
            inner.Children.Add(jaHost);
            inner.Children.Add(new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 8, 0, 8)
            });
            var enHost = new StackPanel();
            AddAsciiRows(enHost, enLines, 13);
            inner.Children.Add(enHost);
            outer.Child = inner;
            container.Children.Add(outer);
            _lastMenuAsciiJaHost = jaHost;
            _lastMenuAsciiEnHost = enHost;
            return container;
        }

        private static TextBox SelectableBlock(string text, int fontSize, Brush brush, Thickness margin)
        {
            return new TextBox
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = fontSize,
                Foreground = brush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = margin,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                IsTabStop = false,
                Focusable = true,
                Cursor = Cursors.Arrow,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
        }

                private (FrameworkElement entry, TextBox ja, TextBox en) BuildEntry(string stamp, string japanese, string english, string kind = "IN-GAME TEXT")
        {
            var subtleBrush = (Brush)FindResource("SubtleTextBrush");
            var textBrush = (Brush)FindResource("TextBrush");
            var borderBrush = (Brush)FindResource("BorderBrush");

            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

            if (HistoryPanel.Children.Count > 0)
            {
                container.Children.Add(new Border
                {
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 0, 0, 16)
                });
            }

            // TEMP Phase-1 correlation aid: wall-clock posting time per entry.
            container.Children.Add(new TextBlock
            {
                Text = stamp,
                FontSize = 10,
                Foreground = subtleBrush,
                Margin = new Thickness(0, 0, 0, 4)
            });

            container.Children.Add(new TextBlock
            {
                Text = kind,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = subtleBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var jaBlock = SelectableBlock(NormalizeEllipsisForDisplay(japanese), 16, textBrush, new Thickness(0, 0, 0, 16));
            container.Children.Add(jaBlock);
            container.Children.Add(new TextBlock
            {
                Text = "TRANSLATION",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = subtleBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var enBlock = SelectableBlock(english, 14, textBrush, new Thickness(0, 0, 0, 0));
            container.Children.Add(enBlock);

            return (container, jaBlock, enBlock);
        }

        private static readonly Regex EllipsisRunPattern = new(@"[．.]{2,}", RegexOptions.Compiled);

        private static string NormalizeEllipsisForDisplay(string text)
        {
            text = EllipsisRunPattern.Replace(text, "…");
            return text;
        }

        private readonly List<(FrameworkElement entry, TextBox ja, TextBox en, string time, string jaRaw)> _bubbleRing = new();

        private void TrimBubbleRing()
        {
            try { while (_bubbleRing.Count > 8) _bubbleRing.RemoveAt(0); } catch { }
        }

        private void ReplaceHistoryRow(string oldTime, string oldJa, string stamp, string japanese, string english)
        {
            try
            {
                for (int i = _history.Count - 1; i >= 0; i--)
                {
                    if (_history[i].time == oldTime && _history[i].ja == oldJa)
                    {
                        _history[i] = (stamp, japanese, english);
                        return;
                    }
                }
            }
            catch { }
        }

        private void RemoveHistoryRow(string oldTime, string oldJa)
        {
            try
            {
                for (int i = _history.Count - 1; i >= 0; i--)
                {
                    if (_history[i].time == oldTime && _history[i].ja == oldJa)
                    {
                        _history.RemoveAt(i);
                        return;
                    }
                }
            }
            catch { }
        }

        private static string StripSpaces(string text)
        {
            try { return (text ?? "").Replace(" ", "").Replace("　", ""); } catch { return text ?? ""; }
        }

        private static string StripOverlapPunct(string text)
        {
            try
            {
                return (text ?? "").Replace("。", "").Replace("、", "").Replace(",", "").Replace("，", "")
                    .Replace("…", "").Replace("．", "").Replace(".", "");
            }
            catch { return text ?? ""; }
        }

        private static System.Collections.Generic.Dictionary<char, char>? _conFold;
        private static string ConFold(string text)
        {
            // Confusion-fold for MATCHING only (display/history stay raw):
            // map inventoried visual-confusion pairs to one canonical side
            // so wobble variants (ナジ/ナゾ, ささば/ささげ) compare equal.
            // Uses the live Loop2 inventory (game-blind, evidenced); never
            // rewrites text. Falls back to input on any failure.
            try
            {
                _conFold ??= BuildConFold();
                if (_conFold.Count == 0) return text ?? "";
                var sb = new System.Text.StringBuilder((text ?? "").Length);
                foreach (char c in text ?? "")
                    sb.Append(_conFold.TryGetValue(c, out char u) ? u : c);
                return sb.ToString();
            }
            catch { return text ?? ""; }
        }

        private static System.Collections.Generic.Dictionary<char, char> BuildConFold()
        {
            var d = new System.Collections.Generic.Dictionary<char, char>();
            try
            {
                foreach (var (a, b) in Capture.CaptureController.ConfusionPairs)
                {
                    char canon = a < b ? a : b;
                    if (!d.ContainsKey(a)) d[a] = canon;
                    if (!d.ContainsKey(b)) d[b] = canon;
                }
            }
            catch { }
            return d;
        }

        private static int GapSeconds(string before, string after)
        {
            try
            {
                var a = before.Split(':');
                var b = after.Split(':');
                int sa = int.Parse(a[0]) * 3600 + int.Parse(a[1]) * 60 + int.Parse(a[2]);
                int sb = int.Parse(b[0]) * 3600 + int.Parse(b[1]) * 60 + int.Parse(b[2]);
                int d = sb - sa;
                if (d < 0) d += 86400;
                return d;
            }
            catch { return 999; }
        }
    }
}