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
        private StackPanel? _battleEnTopHost;
        private Border? _battleEnTopBox;
        private StackPanel? _battleMonEnHost;
        private Border? _battleMonEnBox;
        private StackPanel? _battleActEnHost;
        private Border? _battleActEnBox;
        private FrameworkElement? _battleLeftEnCell;
        private StackPanel? _battleRightEnHost;
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
        private void RefreshBattleHalf(StackPanel topHost, Border topBox, StackPanel monHost, Border monBox, StackPanel actHost, Border actBox, FrameworkElement leftCell, StackPanel rightHost, string top, List<string> mon, List<string> act, List<string> right)
        {
            RefreshAsciiRows(topHost, string.IsNullOrWhiteSpace(top) ? new List<string>() : new List<string> { top }, BattleFontSize);
            topBox.Visibility = Visibility.Visible;
            RefreshAsciiRows(monHost, mon, BattleFontSize);
            RefreshAsciiRows(actHost, ActRows(act), BattleFontSize);
            RefreshAsciiRows(rightHost, right, BattleFontSize);
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
        // guessing. Floor + Wrap backstop: overflow degrades to wrapping,
        // never clipping. Deferred to Loaded so ActualWidth is exact (and
        // harmless on rapid refreshes: it always measures current children).
        private void FitBattleRows(StackPanel host, Border box)
        {
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        double avail = box.ActualWidth - box.Padding.Left - box.Padding.Right;
                        if (avail <= 20) { FileLogger.Log($"[battle] autofit skip avail={avail:0} (box not laid out; next refresh retries)"); return; }
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
                }, DispatcherPriority.Loaded);
            }
            catch { }
        }

        private void BuildBattleHalf(StackPanel parent, Brush textBrush, Brush innerBrush, string top, List<string> mon, List<string> act, List<string> right, bool withBottomMargin, out StackPanel topHost, out Border topBox, out StackPanel monHost, out Border monBox, out StackPanel actHost, out Border actBox, out FrameworkElement leftCell, out StackPanel rightHost)
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
                AddAsciiRows(topHost, new List<string> { top }, BattleFontSize);
            topBox.Child = topHost;
            parent.Children.Add(topBox);
            var half = new Grid { Margin = withBottomMargin ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 0) };
            // 38/62 mirrors the in-game bottom boxes (monster ~37% measured);
            // doubles as wrap room so one game row stays one panel row.
            half.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
            half.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
            var cell = new Grid { Margin = new Thickness(0, 0, 4, 0) };
            // Action-heavy split: 6-kana abilities (ひっきつれざ-class) must fit
            // one panel row. The monster box spans both columns behind the
            // overlay anyway, so only the visible peek narrows.
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22, GridUnitType.Star) });
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78, GridUnitType.Star) });
            monBox = new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                ClipToBounds = true,
                Visibility = Visibility.Visible
            };
            monHost = new StackPanel();
            AddAsciiRows(monHost, mon, BattleFontSize);
            monBox.Child = monHost;
            Grid.SetColumnSpan(monBox, 2);
            cell.Children.Add(monBox);
            actBox = new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(6, 0, 4, 0),
                Background = innerBrush,
                Visibility = act.Count == 0 ? Visibility.Collapsed : Visibility.Visible
            };
            actHost = new StackPanel();
            AddAsciiRows(actHost, ActRows(act), BattleFontSize);
            FitBattleRows(monHost, monBox);
            FitBattleRows(actHost, actBox);
            actBox.Child = actHost;
            Grid.SetColumn(actBox, 1);
            cell.Children.Add(actBox);
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
            AddAsciiRows(rightHost, right, BattleFontSize);
            rightBox.Child = rightHost;
            Grid.SetColumn(rightBox, 1);
            half.Children.Add(rightBox);
            parent.Children.Add(half);
        }

        public void ShowOrUpdateBattleFrame(string topJa, string topEn, List<string> monJa, List<string> monEn, List<string> actJa, List<string> actEn, List<string> rightJa, List<string> rightEn)
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
                string jaSummary = "BATTLE\n" + topJa + "\n" + string.Join("\n", monJa) + "\n" + string.Join("\n", actJa) + "\n" + string.Join("\n", rightJa);
                string enSummary = topEn + "\n" + string.Join("\n", monEn) + "\n" + string.Join("\n", actEn) + "\n" + string.Join("\n", rightEn);
                if (_battleIsLast && _lastBattleEntry != null
                    && _battleJaTopHost != null && _battleJaTopBox != null
                    && _battleMonJaHost != null && _battleMonJaBox != null && _battleActJaHost != null && _battleActJaBox != null && _battleLeftJaCell != null && _battleRightJaHost != null
                    && _battleEnTopHost != null && _battleEnTopBox != null
                    && _battleMonEnHost != null && _battleMonEnBox != null && _battleActEnHost != null && _battleActEnBox != null && _battleLeftEnCell != null && _battleRightEnHost != null)
                {
                    RefreshBattleHalf(_battleJaTopHost, _battleJaTopBox, _battleMonJaHost, _battleMonJaBox, _battleActJaHost, _battleActJaBox, _battleLeftJaCell, _battleRightJaHost, topJa, monJa, actJa, rightJa);
                    RefreshBattleHalf(_battleEnTopHost, _battleEnTopBox, _battleMonEnHost, _battleMonEnBox, _battleActEnHost, _battleActEnBox, _battleLeftEnCell, _battleRightEnHost, topEn, monEn, actEn, rightEn);
                    if (_history.Count > 0)
                        _history[^1] = (_history[^1].time, jaSummary, enSummary);
                    FileLogger.Log($"[battle] panel refresh top={topJa.Length}ch mon={monJa.Count} act={actJa.Count} right={rightJa.Count}");
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
                BuildBattleHalf(inner, textBrush, innerBrush, topJa, monJa, actJa, rightJa, true, out var jaTopHost, out var jaTopBox, out var monJaHost, out var monJaBox, out var actJaHost, out var actJaBox, out var leftJaCell, out var rightJaHost);
                inner.Children.Add(new Border
                {
                    BorderBrush = textBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(0, 8, 0, 8)
                });
                BuildBattleHalf(inner, textBrush, innerBrush, topEn, monEn, actEn, rightEn, false, out var enTopHost, out var enTopBox, out var monEnHost, out var monEnBox, out var actEnHost, out var actEnBox, out var leftEnCell, out var rightEnHost);
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
                _battleEnTopHost = enTopHost;
                _battleEnTopBox = enTopBox;
                _battleMonEnHost = monEnHost;
                _battleMonEnBox = monEnBox;
                _battleActEnHost = actEnHost;
                _battleActEnBox = actEnBox;
                _battleLeftEnCell = leftEnCell;
                _battleRightEnHost = rightEnHost;
                _history.Add((stamp, jaSummary, enSummary));
                FileLogger.Log($"[battle] panel new top={topJa.Length}ch mon={monJa.Count} act={actJa.Count} right={rightJa.Count}");
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

                if (wasAtBottom)
                {
                    Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                }
            });
        }

        public void ClearHistory()
        {
            Dispatcher.Invoke(() =>
            {
                HistoryPanel.Children.Clear();
                _history.Clear();
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
                Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
            });
        }

                private static List<string> SplitMenuLines(string s)
        {
            var out_ = new List<string>();
            foreach (var raw in s.Split('\n'))
                out_.Add(raw.TrimEnd('\r'));
            return out_;
        }

        private void AddAsciiRows(StackPanel host, List<string> lines, int fontSize)
        {
            var textBrush = (Brush)FindResource("TextBrush");
            var mono = new FontFamily("MS Gothic, NSimSun, Consolas, Courier New");
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
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
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }
        }

        private void RefreshAsciiRows(StackPanel host, List<string> lines, int fontSize)
        {
            host.Children.Clear();
            AddAsciiRows(host, lines, fontSize);
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
    }
}