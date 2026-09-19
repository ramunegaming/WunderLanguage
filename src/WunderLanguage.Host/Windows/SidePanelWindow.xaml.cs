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