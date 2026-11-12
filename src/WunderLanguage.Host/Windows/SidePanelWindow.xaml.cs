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

        // Pop-out feed (revert: revert popout-feed). The panel only raises
        // intents and reflects the state AppShell hands it - AppShell owns
        // the detached flag, the z-order policy, and persistence.
        public event Action? PopOutRequested;
        public event Action? DockRequested;
        public event Action? PauseRequested;
        public event Action? SettingsRequested;
        public event Action? ShotRequested;

        public SidePanelWindow()
        {
            InitializeComponent();
            try { FileLogger.Log("[build] panel absorb-ring confold ON"); } catch { }
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

        // Detached mode re-shapes the panel: compact control header in, chat
        // title out. Pure presentation - positioning and z-order stay AppShell's
        // job.
        private bool _detached;
        public void SetDetached(bool detached)
        {
            _detached = detached;
            // Feed resize (revert: revert feed-resize): only the popped-out
            // feed may be dragged-resized - the docked panel's size is owned
            // by OnBoundsChanged (SetWindowPos against the game rect) and
            // must stay fixed. MinWidth/MinHeight live in the XAML.
            ResizeMode = detached ? ResizeMode.CanResize : ResizeMode.NoResize;
            DetachedHeader.Visibility = detached ? Visibility.Visible : Visibility.Collapsed;
            FeedTitleBlock.Visibility = detached ? Visibility.Collapsed : Visibility.Visible;
            PopOutButton.Content = detached ? "Dock" : "Pop out";
            PopOutButton.ToolTip = detached
                ? "Re-attach the feed to the game's right edge (Ctrl+Shift+D)."
                : "Detach the feed to its own always-on-top window (Ctrl+Shift+D). Drag the FEED grip to move it.";
        }

        // Offline gate (2026-11-12): --feedresize proves the resize contract
        // with the REAL panel: docked = NoResize, detached = CanResize, the
        // 340x220 minimums with WPF coercion, SizeChanged carrying the new
        // size (what AppShell persists), and FeedWidth/FeedHeight round-
        // tripping through the exact Save() payload + the exact Load() read
        // block - all without touching the real settings.json.
        // No emulator, no sidecar, no GPU; exit 0 only if every assertion
        // holds. Revert: revert feed-resize (delete this method + the
        // --feedresize block in App.xaml.cs + the AppShell/AppSettings
        // wiring + the XAML ResizeMode/MinWidth/MinHeight).
        public static bool RunResizeOfflineTest()
        {
            bool ok = true;
            SidePanelWindow? w = null;
            try
            {
                w = new SidePanelWindow();
                bool mins = w.MinWidth == 340 && w.MinHeight == 220;
                FileLogger.Log($"[feedresize] min {w.MinWidth}x{w.MinHeight} (want 340x220)");
                ok &= mins;

                w.SetDetached(false);
                bool dockedFixed = w.ResizeMode == ResizeMode.NoResize;
                w.SetDetached(true);
                bool detachedResizable = w.ResizeMode == ResizeMode.CanResize;
                FileLogger.Log($"[feedresize] resizeMode docked=NoResize:{dockedFixed} detached=CanResize:{detachedResizable}");
                ok &= dockedFixed && detachedResizable;

                // SizeChanged carries the new DIP size (the signal AppShell
                // persists through the feed-defer debounce).
                double seenW = 0, seenH = 0;
                w.SizeChanged += (_, e) => { seenW = e.NewSize.Width; seenH = e.NewSize.Height; };
                w.Show();
                w.Activate();
                w.Width = 500;
                w.Height = 480;
                Pump(200);
                bool resized = Math.Abs(seenW - 500) < 1 && Math.Abs(seenH - 480) < 1;
                FileLogger.Log($"[feedresize] SizeChanged -> {seenW}x{seenH} (want 500x480)");
                ok &= resized;

                // Below-minimum is coerced, never accepted.
                w.Width = 100;
                w.Height = 50;
                Pump(200);
                bool coerced = w.ActualWidth >= 340 && w.ActualHeight >= 220;
                FileLogger.Log($"[feedresize] below-min coerced to {w.ActualWidth}x{w.ActualHeight} (want >= 340x220)");
                ok &= coerced;

                // Round-trip through the REAL payload writer + the REAL
                // read block (both extracted in AppSettings for this gate).
                var s = new Settings.AppSettings { FeedWidth = 500, FeedHeight = 480 };
                string json = System.Text.Json.JsonSerializer.Serialize(s.BuildPayload());
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    var back = new Settings.AppSettings();
                    Settings.AppSettings.ReadFeedFields(back, doc.RootElement);
                    bool roundTrip = back.FeedWidth == 500 && back.FeedHeight == 480
                                     && json.Contains("\"FeedWidth\":500")
                                     && json.Contains("\"FeedHeight\":480");
                    FileLogger.Log($"[feedresize] payload round-trip FeedWidth/FeedHeight ok={roundTrip}");
                    ok &= roundTrip;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[feedresize] ERROR: {ex.Message}");
                ok = false;
            }
            finally
            {
                try { w?.Close(); } catch { }
            }
            return ok;
        }

        private static void Pump(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                System.Threading.Thread.Sleep(10);
            }
        }

        // Kept in step with BottomBarWindow so the two Pause buttons never
        // disagree about which state they'd toggle into.
        public void SetPauseState(bool paused)
        {
            DetachedPauseButton.Content = paused ? "Resume" : "Pause";
        }

        // Fired from the AppShell hotkey as well: WS_EX_NOACTIVATE means the
        // detached panel can't take keyboard focus, so the panel-local
        // Ctrl+Shift+C handler never sees the key while popped out.
        public void CopyAllHistoryHotkey() => CopyAllHistory();

        private void DetachedHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try
            {
                // Header only - the four buttons set e.Handled on their own
                // MouseLeftButtonDown, so this never fires mid-click.
                DragMove();
            }
            catch
            {
                // DragMove throws if the button was already released - benign.
            }
        }

        private void PopOutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detached) DockRequested?.Invoke();
            else PopOutRequested?.Invoke();
        }

        private void DockButton_Click(object sender, RoutedEventArgs e) => DockRequested?.Invoke();
        private void DetachedPauseButton_Click(object sender, RoutedEventArgs e) => PauseRequested?.Invoke();
        private void DetachedSettingsButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
        private void DetachedShotButton_Click(object sender, RoutedEventArgs e) => ShotRequested?.Invoke();

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
                // Z2 5-box inventory feed (revert: revert z2-5box-view): the
                // inventory screen renders as the pack's 5-box layout inside
                // the standard outline — item name top-right live, four boxes
                // fixed chrome, EN below the grid. Every other menu falls
                // through to the normal path untouched.
                if (kind == "MENU" && Packs.PackRegistry.Current.TryInventoryLayout(japanese, english, out var invLayout))
                {
                    // JP-first fill: same JA + real EN lands in the strip.
                    if (_entryIsInventory && _pendingMenuJa is not null
                        && NormalizeEllipsisForDisplay(_pendingMenuJa) == NormalizeEllipsisForDisplay(japanese))
                    {
                        if (IsPendingEn(english))
                        {
                            FileLogger.Log("[panel] menu jp-first pending-dupe (inventory same JA already pending)");
                            return;
                        }
                        // Mirror the rewrite guard: an update that advanced
                        // the inventory between fire and EN-landing makes
                        // this EN stale — drop it, the update carried its own.
                        if (_inventoryFullJa is null
                            || NormalizeEllipsisForDisplay(_inventoryFullJa) != NormalizeEllipsisForDisplay(japanese))
                        {
                            FileLogger.Log("[panel] menu jp-first fill dropped (inventory advanced since fire)");
                            _pendingMenuJa = null; _pendingMenuEnBlock = null; _pendingMenuAsciiEnHost = null; _pendingMenuJaBlock = null;
                            return;
                        }
                        if (_inventoryEnBlock is not null) _inventoryEnBlock.Text = invLayout.ItemEn;
                        if (_history.Count > 0)
                            _history[^1] = (_history[^1].time, kind + "\n" + japanese, english);
                        FileLogger.Log("[panel] menu jp-first fill (inventory EN refreshed in place)");
                        _pendingMenuJa = null;
                        _pendingMenuEnBlock = null;
                        _pendingMenuAsciiEnHost = null;
                        _pendingMenuJaBlock = null;
                        if (wasAtBottom)
                            Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                        return;
                    }
                    // New inventory entry.
                    _pendingMenuJa = IsPendingEn(english) ? japanese : null;
                    _pendingMenuEnBlock = null;
                    _pendingMenuAsciiEnHost = null;
                    _pendingMenuJaBlock = null;
                    _battleIsLast = false;
                    var invParts = BuildInventoryEntry(stamp, kind, invLayout);
                    HistoryPanel.Children.Add(invParts.entry);
                    _entryIsInventory = true;
                    _inventoryFullJa = japanese;
                    _inventoryContainer = invParts.entry;
                    _inventoryIsBottle = invLayout.IsBottle;
                    _inventoryItemBlock = invParts.item;
                    _inventoryEnBlock = invParts.en;
                    _inventoryResidualBlock = invParts.residual;
                    _pendingMenuEnBlock = invParts.en;
                    _lastJapaneseBlock = null;
                    _lastEnglishBlock = null;
                    _lastMenuRawJa = null;
                    _lastMenuAsciiEntry = null;
                    _lastMenuAsciiJaHost = null;
                    _lastMenuAsciiEnHost = null;
                    _history.Add((stamp, kind + "\n" + japanese, english));
                    if (wasAtBottom)
                        Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                    return;
                }
                // JP-first fill (revert: revert jp-first): the controller posts
                // the menu/decision JA immediately (EN = pending marker), then
                // re-invokes with the same JA and the real EN. That second call
                // fills THIS block in place instead of appending a duplicate.
                // Captured element refs so later appends can't misdirect the
                // fill. Duplicate pending fire of the same JA drops.
                if (!string.IsNullOrEmpty(japanese)
                    && _pendingMenuJa is not null
                    && NormalizeEllipsisForDisplay(_pendingMenuJa) == NormalizeEllipsisForDisplay(japanese))
                {
                    if (IsPendingEn(english))
                    {
                        FileLogger.Log("[panel] menu jp-first pending-dupe (same JA already pending)");
                        return;
                    }
                    try
                    {
                        if (MenuDisplayMode == "Ascii")
                        {
                            if (_pendingMenuAsciiEnHost is not null)
                                RefreshAsciiRows(_pendingMenuAsciiEnHost, SplitMenuLines(english), 13);
                        }
                        else if (_pendingMenuEnBlock is not null)
                        {
                            // Growth-update may have rewritten the bubble between
                            // fire and EN-landing; a rewritten JA means this EN
                            // is stale - drop instead of poisoning the bubble.
                            if (_pendingMenuJaBlock is not null
                                && NormalizeEllipsisForDisplay(_pendingMenuJaBlock.Text) != NormalizeEllipsisForDisplay(_pendingMenuJa))
                            {
                                FileLogger.Log("[panel] menu jp-first fill dropped (bubble rewritten since fire)");
                                _pendingMenuJa = null; _pendingMenuEnBlock = null; _pendingMenuAsciiEnHost = null; _pendingMenuJaBlock = null;
                                return;
                            }
                            _pendingMenuEnBlock.Text = english;
                        }
                        if (_history.Count > 0)
                            _history[^1] = (_history[^1].time, kind + "\n" + japanese, english);
                        FileLogger.Log("[panel] menu jp-first fill (EN refreshed in place)");
                        _pendingMenuJa = null;
                        _pendingMenuEnBlock = null;
                        _pendingMenuAsciiEnHost = null;
                        _pendingMenuJaBlock = null;
                        if (wasAtBottom)
                            Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                        return;
                    }
                    catch { }
                    _pendingMenuJa = null;
                    _pendingMenuEnBlock = null;
                    _pendingMenuAsciiEnHost = null;
                    _pendingMenuJaBlock = null;
                }
                _pendingMenuJa = IsPendingEn(english) ? japanese : null;
                _pendingMenuEnBlock = null;
                _pendingMenuAsciiEnHost = null;
                _pendingMenuJaBlock = null;
                _battleIsLast = false;
                _entryIsInventory = false;
                _inventoryFullJa = null;
                _inventoryContainer = null;
                _inventoryIsBottle = false;
                _inventoryItemBlock = null;
                _inventoryEnBlock = null;
                _inventoryResidualBlock = null;
                if (MenuDisplayMode == "Ascii")
                {
                    var asciiEntry = BuildAsciiMenuEntry(stamp, kind, SplitMenuLines(japanese), SplitMenuLines(english));
                    HistoryPanel.Children.Add(asciiEntry);
                    _lastMenuAsciiEntry = asciiEntry;
                    _lastMenuRawJa = japanese;
                    _lastJapaneseBlock = null;
                    _lastEnglishBlock = null;
                    _pendingMenuAsciiEnHost = _lastMenuAsciiEnHost;
                    _history.Add((stamp, kind + "\n" + japanese, english));
                    if (wasAtBottom)
                        Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                    return;
                }
                var (entry, jaBlock, enBlock) = BuildEntry(stamp, japanese, english, kind);
                HistoryPanel.Children.Add(entry);
                _lastJapaneseBlock = jaBlock;
                _lastEnglishBlock = enBlock;
                _pendingMenuEnBlock = enBlock;
                _pendingMenuJaBlock = jaBlock;
                _lastMenuRawJa = null;
                _lastMenuAsciiEntry = null;
                _lastMenuAsciiJaHost = null;
                _lastMenuAsciiEnHost = null;
                _history.Add((stamp, kind + "\n" + japanese, english));
                if (wasAtBottom)
                    Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
            });
        }

        // JP-first (revert: revert jp-first): pending-menu state - the JA text
        // posted with a pending EN marker, plus the exact EN element to fill
        // (refs captured at append so later bubbles can't steal the fill).
        private string? _pendingMenuJa;
        private TextBox? _pendingMenuEnBlock;
        private TextBox? _pendingMenuJaBlock;
        private StackPanel? _pendingMenuAsciiEnHost;

        // Z2 5-box inventory feed (revert: revert z2-5box-view): the
        // inventory entry owns its state — the full menu JA as the match
        // key, the top-right item box as the live field, plus the EN strip
        // and residual strip so no content is ever dropped.
        private bool _entryIsInventory;
        private string? _inventoryFullJa;
        // Z2 v2 (revert: revert z2-v2-bottle-mirror): the drawn bottle state
        // + the container itself, so an update that flips state rebuilds the
        // grid geometry in place (bottle = one tall box, else two boxes).
        private bool _inventoryIsBottle;
        private FrameworkElement? _inventoryContainer;
        private TextBox? _inventoryItemBlock;
        private TextBox? _inventoryEnBlock;
        private TextBox? _inventoryResidualBlock;

        // Menu growth: edit in place on match, DROP on mismatch (never appends,
        // so menus can never duplicate dialogue).
        public void UpdateMenuIfMatches(string expectedOldJa, string kind, string japanese, string english)
        {
            Dispatcher.Invoke(() =>
            {
                // Z2 5-box inventory feed (revert: revert z2-5box-view):
                // match on the full menu JA, then re-derive the layout —
                // the item box live-refreshes (カンテラ -> ブーメラン), EN +
                // residual follow; the four chrome boxes are pack constants
                // and stay put.
                if (_entryIsInventory)
                {
                    if (_inventoryFullJa is null) return;
                    if (NormalizeEllipsisForDisplay(_inventoryFullJa) != NormalizeEllipsisForDisplay(expectedOldJa)) return;
                    _inventoryFullJa = japanese;
                    if (Packs.PackRegistry.Current.TryInventoryLayout(japanese, english, out var invUpd))
                    {
                        // Z2 v2 (revert: revert z2-v2-bottle-mirror): the
                        // bottle state changes the GEOMETRY (one tall box vs
                        // two), so a flip rebuilds the whole grid in place —
                        // text-only refresh would draw the old shape.
                        if (invUpd.IsBottle != _inventoryIsBottle && _inventoryContainer is not null)
                        {
                            int idx = HistoryPanel.Children.IndexOf(_inventoryContainer);
                            var rebuilt = BuildInventoryEntry(_history[^1].time, kind, invUpd);
                            if (idx >= 0)
                            {
                                HistoryPanel.Children[idx] = rebuilt.entry;
                                _inventoryContainer = rebuilt.entry;
                                _inventoryIsBottle = invUpd.IsBottle;
                                _inventoryItemBlock = rebuilt.item;
                                _inventoryEnBlock = rebuilt.en;
                                _inventoryResidualBlock = rebuilt.residual;
                                _pendingMenuEnBlock = rebuilt.en;
                                FileLogger.Log($"[panel] inventory state flip -> {(invUpd.IsBottle ? "bottle (tall box)" : "normal (two boxes)")} (rebuilt)");
                            }
                        }
                        else
                        {
                        if (_inventoryItemBlock is not null)
                            _inventoryItemBlock.Text = NormalizeEllipsisForDisplay(invUpd.ItemJa);
                        if (_inventoryEnBlock is not null)
                            _inventoryEnBlock.Text = invUpd.ItemEn;
                        if (_inventoryResidualBlock is not null)
                        {
                            _inventoryResidualBlock.Text = NormalizeEllipsisForDisplay(invUpd.Residual);
                            _inventoryResidualBlock.Visibility = string.IsNullOrEmpty(invUpd.Residual) ? Visibility.Collapsed : Visibility.Visible;
                        }
                        }
                    }
                    else
                    {
                        // OCR lost the screen's static markers mid-update —
                        // hold the drawn chrome (constants) rather than
                        // re-route; the next clean update re-derives.
                        FileLogger.Log("[panel] inventory update lost layout (held)");
                    }
                    if (_history.Count > 0)
                        _history[^1] = (_history[^1].time, japanese, english);
                    Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                    return;
                }
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
                    // JP-first fill (revert: revert jp-first): the EN refresh for
                    // a bubble posted at fire time. Exact raw-JA match against
                    // the ring (the pending bubble's JA is verbatim the fired
                    // text), EXEMPT from the 15s gap window (measured EN
                    // latency 8-18s under load, live 2026-10-24), fills only
                    // while the shown EN is still pending and the incoming EN
                    // is real. A duplicate pending fire of the same JA drops.
                    // Display-only: capture/translation/sim untouched.
                    if (!string.IsNullOrEmpty(japanese))
                    {
                        for (int pi = _bubbleRing.Count - 1; pi >= 0; pi--)
                        {
                            var pb = _bubbleRing[pi];
                            if (!string.Equals(pb.jaRaw, japanese, System.StringComparison.Ordinal)) continue;
                            bool shownPending = IsPendingEn(pb.en.Text);
                            if (shownPending && !IsPendingEn(english))
                            {
                                pb.en.Text = english;
                                ReplaceHistoryRow(pb.time, pb.jaRaw, stamp, pb.jaRaw, english);
                                _lastJapaneseBlock = pb.ja;
                                _lastEnglishBlock = pb.en;
                                try { _bubbleRing.RemoveAt(pi); } catch { }
                                try { _bubbleRing.Add((pb.entry, pb.ja, pb.en, stamp, pb.jaRaw)); } catch { }
                                TrimBubbleRing();
                                FileLogger.Log("[panel] jp-first fill (EN refreshed in place)");
                                if (wasAtBottom)
                                    Dispatcher.InvokeAsync(() => HistoryScroller.ScrollToEnd(), DispatcherPriority.Loaded);
                                return;
                            }
                            if (shownPending && IsPendingEn(english))
                            {
                                FileLogger.Log("[panel] jp-first pending-dupe (same JA already pending)");
                                return;
                            }
                            // shown EN already real: fall through to the
                            // absorption logic below (drop-eq collapses it).
                            break;
                        }
                    }
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
                            && shownStripped.Length >= incomingStripped.Length)
                        {
                            // B19 drop-eq (revert: revert drop-eq): identical
                            // (or folded-identical) arrivals collapse — the
                            // strict > let cross-road identical re-fires
                            // (そこで 105+106) post twice. Display-only.
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
        // Error-code chip (2026-10-23; revert: revert error-codes): ONE
        // amber row per session that updates in place as state worsens
        // (E1 retrying -> E2 spent), chrome like ShowNotice - never in
        // _history, never seen by copy-all or the corpus miner. Click ->
        // detail popup (the compact chip line can't hold cause + fix;
        // the registry in Diagnostics/ErrorCodes.cs owns all wording).
        private Border? _errorChip;
        private TextBlock? _errorChipLabel;
        private string? _errorCodeShown;
        // Live line (item 2 transport chip): factual when+cause data
        // captured at the fire site, appended to the chip + popup under
        // the registry's static wording.
        private string? _errorChipLive;
        private Window? _errorPopup;

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

        // Error-code chip (2026-10-23; revert: revert error-codes). One
        // row per session, text updated in place as state worsens;
        // unknown codes log and drop (a bad code must never break the
        // feed path). The chip's screen position is logged once layout
        // settles so UI-automation gates (and support shots) can find it.
        public void ShowError(string code, string? live = null)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var info = Diagnostics.ErrorCodes.Get(code);
                    if (info is null)
                    {
                        FileLogger.Log($"[error] unknown code '{code}' - no chip posted");
                        return;
                    }
                    if (_placeholderShown)
                    {
                        HistoryPanel.Children.Clear();
                        _placeholderShown = false;
                    }
                    if (_errorChip is null)
                    {
                        var label = new TextBlock
                        {
                            TextWrapping = System.Windows.TextWrapping.Wrap,
                            FontSize = 12.5,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x4D)),
                        };
                        var hint = new TextBlock
                        {
                            Text = "(click for details)",
                            FontSize = 10,
                            Margin = new Thickness(0, 3, 0, 0),
                            Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x9A, 0x4F)),
                        };
                        var stack = new StackPanel();
                        stack.Children.Add(label);
                        stack.Children.Add(hint);
                        var chip = new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x21, 0x0C)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0x99, 0x2E)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(5),
                            Padding = new Thickness(9, 6, 9, 6),
                            Margin = new Thickness(0, 6, 0, 6),
                            Child = stack,
                            Cursor = Cursors.Hand,
                            // Left, not Stretch: a MaxWidth-capped stretch
                            // resolves to CENTER (WPF), which would float the
                            // chip mid-feed in any panel wider than 336px.
                            HorizontalAlignment = HorizontalAlignment.Left,
                            MaxWidth = 336,
                        };
                        chip.MouseLeftButtonUp += (_, _) => OnChipClicked();
                        _errorChip = chip;
                        _errorChipLabel = label;
                        HistoryPanel.Children.Add(chip);
                    }
                    bool first = _errorCodeShown != code;
                    _errorCodeShown = code;
                    // Live line (item 2): registry wording first, factual
                    // when+cause data below it — both wrap inside the
                    // chip's max width.
                    _errorChipLive = live;
                    string labelText = string.IsNullOrEmpty(live)
                        ? info.Chip
                        : info.Chip + "\n" + live;
                    if (_errorChipLabel != null && _errorChipLabel.Text != labelText)
                        _errorChipLabel.Text = labelText;
                    FileLogger.Log($"[error] chip {(first ? code + " posted" : code + " refreshed (in place)")}");
                    HistoryScroller.ScrollToEnd();
                    // Re-logged on EVERY post/update, at ApplicationIdle:
                    // pre-render positions are stale (an ownerless window
                    // gets its cascade position only after the first
                    // layout pass - a Loaded-priority read returned a
                    // coordinate the panel then moved away from), so
                    // gates must always trust the NEWEST line. Panel's own
                    // position included for diagnosis.
                    Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (_errorChip is not Border visible) return;
                            var p = visible.PointToScreen(new Point(0, 0));
                            FileLogger.Log($"[error] chip {code} at screen L{(int)p.X} T{(int)p.Y} (panel L{(int)Left} T{(int)Top})");
                        }
                        catch { }
                    }, DispatcherPriority.ApplicationIdle);
                }
                catch (Exception ex) { FileLogger.Log("[error] ShowError failed: " + ex.Message); }
            });
        }

        // One seam for two callers: the real chip MouseLeftButtonUp and
        // the --errortest gate (TestClickChip) - the gate then exercises
        // the exact handler a user's click takes, with no screen
        // coordinates anywhere (the panel can sit behind a fullscreen
        // emulator and the popup still renders for the PNG dump).
        // Revert: revert error-codes.
        private void OnChipClicked()
        {
            if (_errorCodeShown is string open) OpenErrorPopup(open);
        }

        internal Window? TestClickChip()
        {
            OnChipClicked();
            return _errorPopup;
        }

        // Click-through popup: what happened + what to do for one code.
        // Non-modal, closed-then-reopened per click so stale E1 text can
        // never sit over an E2 state; Escape or OK closes; centered on
        // the panel (owner), no topmost games, no focus steal beyond the
        // user's own click. Revert: revert error-codes.
        private void OpenErrorPopup(string code)
        {
            try
            {
                var info = Diagnostics.ErrorCodes.Get(code);
                if (info is null) return;
                try { _errorPopup?.Close(); } catch { }
                _errorPopup = null;

                var root = new StackPanel { Margin = new Thickness(16), MaxWidth = 440 };
                root.Children.Add(new TextBlock
                {
                    Text = info.Title,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x4D)),
                });
                root.Children.Add(ErrorSectionLabel("What happened"));
                if (!string.IsNullOrEmpty(_errorChipLive))
                    root.Children.Add(ErrorBody("\u25B6 " + _errorChipLive));
                root.Children.Add(ErrorBody(info.Detail));
                root.Children.Add(ErrorSectionLabel("What to do"));
                root.Children.Add(ErrorBody(info.Fix));
                root.Children.Add(new TextBlock
                {
                    Text = $"Error code: {info.Code}  \u00B7  this row stays in the feed, app.log holds the full details",
                    FontSize = 10.5,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("SubtleTextBrush"),
                    Margin = new Thickness(0, 12, 0, 0),
                });
                var ok = new Button
                {
                    Content = "OK",
                    Width = 76,
                    Padding = new Thickness(0, 6, 0, 6),
                    Margin = new Thickness(0, 14, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Background = (Brush)FindResource("AccentBrush"),
                    Foreground = Brushes.White,
                };
                ok.Click += (_, _) => { try { _errorPopup?.Close(); } catch { } };
                root.Children.Add(ok);

                var win = new Window
                {
                    Title = "WunderLanguage \u2014 " + info.Title,
                    Content = new ScrollViewer
                    {
                        Content = root,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Padding = new Thickness(0, 0, 4, 0),
                    },
                    Width = 486,
                    MaxHeight = 640,
                    SizeToContent = SizeToContent.Height,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = (Brush)FindResource("BackgroundBrush"),
                    ShowInTaskbar = false,
                };
                win.KeyDown += (_, e) => { if (e.Key == Key.Escape) { try { win.Close(); } catch { } } };
                _errorPopup = win;
                win.Show();
                FileLogger.Log($"[error] popup {code} opened at L{(int)win.Left} T{(int)win.Top} {win.Width:F0}x{win.ActualHeight:F0}");
            }
            catch (Exception ex) { FileLogger.Log("[error] popup failed: " + ex.Message); }
        }

        private static TextBlock ErrorSectionLabel(string text) => new()
        {
            Text = text,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x9A, 0x4F)),
            Margin = new Thickness(0, 14, 0, 0),
        };

        private static TextBlock ErrorBody(string text) => new()
        {
            Text = text,
            FontSize = 12.5,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xED)),
            Margin = new Thickness(0, 4, 0, 0),
        };

        public void ClearHistory()
        {
            _noticeShown = false;
            // Error chip is session chrome like the notice row.
            _errorCodeShown = null;
            _errorChipLive = null;
            _errorChip = null;
            _errorChipLabel = null;
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

        // Z2 5-box inventory feed (revert: revert z2-5box-view): the
        // standard outline Border (same as the ascii/battle entries) around
        // the mockup's 5 boxes (zelda-inventory-mockup.svg; design units
        // 888x735 inside the outline: left-top sprite box empty, top-right
        // item name live, middle-right static, bottom-left actions,
        // bottom-right EQUIPMENT), EN strip below the grid. A SizeChanged
        // scaler keeps the proportions at any panel width.
        private (FrameworkElement entry, TextBox item, TextBox en, TextBox residual) BuildInventoryEntry(string stamp, string kind, Packs.InventoryLayout inv)
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

            // Z2 v2 (revert: revert z2-v2-bottle-mirror): JP grid on top,
            // the SAME box layout duplicated in EN below the separator —
            // item live EN, pack-constant action/mid chrome, EQUIPMENT
            // unchanged. Bottle state = one tall box in BOTH grids (the
            // bottle art has nothing to translate: blank in each).
            var (gridJp, itemJp, _) = BuildInvGrid(inv, en: false, textBrush);
            var (gridEn, itemEnBlock, _) = BuildInvGrid(inv, en: true, textBrush);
            inner.Children.Add(gridJp);

            inner.Children.Add(new Border
            {
                BorderBrush = textBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 8, 0, 8)
            });
            inner.Children.Add(new TextBlock
            {
                Text = "TRANSLATION",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = subtleBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });
            inner.Children.Add(gridEn);
            var residualBlock = SelectableBlock(NormalizeEllipsisForDisplay(inv.Residual), 11, subtleBrush, new Thickness(0, 6, 0, 0));
            residualBlock.Visibility = string.IsNullOrEmpty(inv.Residual) ? Visibility.Collapsed : Visibility.Visible;
            inner.Children.Add(residualBlock);

            outer.Child = inner;
            container.Children.Add(outer);
            return (container, itemJp, itemEnBlock, residualBlock);
        }

        // One inventory grid (JP or EN mirror): 888x735 design space, 5-box
        // normally / item + ONE tall box + actions when the bottle is
        // selected. Returns the grid canvas + the item TextBox (the live
        // field, refreshed by fills and updates) + its action fonts.
        private (Canvas canvas, TextBox item, List<(TextBlock tb, double design)> actionFonts) BuildInvGrid(Packs.InventoryLayout inv, bool en, Brush textBrush)
        {
            TextBox itemBlock = en
                ? SelectableBlock(inv.ItemEn, 14, textBrush, new Thickness(0))
                : SelectableBlock(NormalizeEllipsisForDisplay(inv.ItemJa), 14, textBrush, new Thickness(0));
            string midText = en ? inv.MidTextEn : inv.MidText;
            var midBlock = SelectableBlock(midText, 12, textBrush, new Thickness(0));
            var equipBlock = SelectableBlock(inv.BottomRight, 12, textBrush, new Thickness(0));
            var actionHost = new StackPanel();
            var actionFonts = new List<(TextBlock tb, double design)>();
            foreach (string row in (en ? inv.ActionRowsEn : inv.ActionRows))
            {
                var tb = new TextBlock { Text = row, FontSize = 12, Foreground = textBrush, TextWrapping = TextWrapping.Wrap };
                actionHost.Children.Add(tb);
                actionFonts.Add((tb, 29));
            }

            static Border InvBox(FrameworkElement? child, Brush stroke) => new Border
            {
                BorderBrush = stroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                Child = child
            };
            var leftTop = InvBox(null, textBrush);
            var itemBox = InvBox(itemBlock, textBrush);
            var bottomLeft = InvBox(actionHost, textBrush);
            Border? mergedBox = null, midBox = null, bottomRight = null;
            if (inv.IsBottle)
            {
                // ペンダント + EQUIPMENT merged: bottle art = nothing to
                // translate, blank (same doctrine as box art).
                mergedBox = InvBox(null, textBrush);
            }
            else
            {
                midBox = InvBox(midBlock, textBrush);
                bottomRight = InvBox(equipBlock, textBrush);
            }

            var canvas = new Canvas();
            canvas.Children.Add(leftTop);
            canvas.Children.Add(itemBox);
            canvas.Children.Add(bottomLeft);
            if (mergedBox is not null) canvas.Children.Add(mergedBox);
            else
            {
                canvas.Children.Add(midBox!);
                canvas.Children.Add(bottomRight!);
            }
            canvas.SizeChanged += (_, _) =>
            {
                double w = canvas.ActualWidth;
                if (w <= 0) return;
                double s = w / 888.0;
                canvas.Height = 735 * s;
                static void Place(Border box, double x, double y, double bw, double bh, double s2)
                {
                    Canvas.SetLeft(box, x * s2);
                    Canvas.SetTop(box, y * s2);
                    box.Width = bw * s2;
                    box.Height = bh * s2;
                }
                Place(leftTop, 0, 0, 551, 431, s);
                Place(itemBox, 604, 0, 284, 167, s);
                Place(bottomLeft, 0, 482, 554, 253, s);
                if (mergedBox is not null) Place(mergedBox, 604, 182, 284, 553, s);
                else
                {
                    Place(midBox!, 604, 182, 284, 253, s);
                    Place(bottomRight!, 604, 482, 284, 253, s);
                }
                itemBlock.FontSize = System.Math.Max(9, 34 * s);
                if (mergedBox is null)
                {
                    midBlock.FontSize = System.Math.Max(9, 26 * s);
                    equipBlock.FontSize = System.Math.Max(9, 26 * s);
                }
                foreach (var (tb, design) in actionFonts) tb.FontSize = System.Math.Max(9, design * s);
            };
            return (canvas, itemBlock, actionFonts);
        }

        private static readonly Regex EllipsisRunPattern = new(@"[．.]{2,}", RegexOptions.Compiled);

        private static string NormalizeEllipsisForDisplay(string text)
        {
            text = EllipsisRunPattern.Replace(text, "…");
            return text;
        }

        private readonly List<(FrameworkElement entry, TextBox ja, TextBox en, string time, string jaRaw)> _bubbleRing = new();

        // JP-first (revert: revert jp-first): pending-EN marker. Must match
        // CaptureController.PendingTranslationMarker - a bubble whose EN is
        // this (or blank) is "awaiting translation" and gets filled in place
        // by the fill branch in ShowTranslation / ShowMenu.
        internal const string PendingEnMarker = "…";

        private static bool IsPendingEn(string? en) =>
            string.IsNullOrWhiteSpace(en) || en == PendingEnMarker;

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

        private static string ConFold(string text)
        {
            // Single source of truth lives on the controller (Loop2
            // inventory); this wrapper keeps call sites unchanged.
            try { return Capture.CaptureController.ConFoldText(text ?? ""); }
            catch { return text ?? ""; }
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