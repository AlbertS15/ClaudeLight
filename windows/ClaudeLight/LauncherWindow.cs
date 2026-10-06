using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeLight;

/// The Spotlight-style bar: one text box, then either results or an answer.
public sealed class LauncherWindow : Window
{
    private readonly FileSearch _search = new();
    private readonly ClaudeRunner _claude = new();
    private readonly ApiRunner _api = new();
    private readonly GeminiRunner _gemini = new();
    private readonly CodexRunner _codex = new();

    private readonly TextBox _input = new();
    private readonly TextBlock _placeholder;
    private readonly MascotView _mascot = new();
    private readonly Button _modelButton = new();
    private readonly StackPanel _results = new();
    private readonly ScrollViewer _resultsScroll;
    private readonly TextBox _answer = new();
    private readonly TextBlock _asked;
    private readonly TextBlock _error;
    private readonly TextBlock _footer;
    private readonly ScrollViewer _answerScroll;
    private readonly StackPanel _answerPanel;
    private readonly Border _divider;

    private List<Hit> _hits = new();
    private int _selection;
    private bool _hasAnswer;
    private bool _isAnswering;
    private DateTime _hiddenAt = DateTime.MinValue;

    public LauncherWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");

        _input.FontSize = 22;
        _input.BorderThickness = new Thickness(0);
        _input.Background = Brushes.Transparent;
        _input.VerticalContentAlignment = VerticalAlignment.Center;
        _input.TextChanged += (_, _) => OnQueryChanged();
        _input.PreviewKeyDown += OnKey;

        _placeholder = Theme.Label("", 22, Theme.Secondary);
        _placeholder.IsHitTestVisible = false;
        _placeholder.Margin = new Thickness(3, 0, 0, 0);

        _mascot.Width = 32;
        _mascot.Height = 22;
        _mascot.Margin = new Thickness(0, 0, 12, 0);

        _modelButton.Padding = new Thickness(10, 3, 10, 3);
        _modelButton.BorderThickness = new Thickness(0);
        _modelButton.FontSize = 12;
        _modelButton.Cursor = Cursors.Hand;
        _modelButton.Click += (_, _) => ShowModelMenu();

        var inputGrid = new Grid();
        inputGrid.Children.Add(_placeholder);
        inputGrid.Children.Add(_input);

        var top = new DockPanel { Height = 58, Margin = new Thickness(18, 0, 14, 0) };
        DockPanel.SetDock(_mascot, Dock.Left);
        DockPanel.SetDock(_modelButton, Dock.Right);
        top.Children.Add(_mascot);
        top.Children.Add(_modelButton);
        top.Children.Add(inputGrid);
        _modelButton.VerticalAlignment = VerticalAlignment.Center;

        _divider = new Border { Height = 1, Visibility = Visibility.Collapsed };

        _resultsScroll = new ScrollViewer
        {
            Content = _results,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(8),
        };

        _asked = Theme.Label("", 13, null, FontWeights.SemiBold);
        _asked.TextWrapping = TextWrapping.Wrap;
        _answer.IsReadOnly = true;
        _answer.TextWrapping = TextWrapping.Wrap;
        _answer.BorderThickness = new Thickness(0);
        _answer.Background = Brushes.Transparent;
        _answer.FontSize = 15;
        _answer.Margin = new Thickness(0, 8, 0, 0);
        _error = Theme.Label("", 13, Theme.Error);
        _error.TextWrapping = TextWrapping.Wrap;
        _answerPanel = new StackPanel { Margin = new Thickness(18, 14, 18, 10) };
        _answerPanel.Children.Add(_asked);
        _answerPanel.Children.Add(_answer);
        _answerPanel.Children.Add(_error);
        _answerScroll = new ScrollViewer
        {
            Content = _answerPanel,
            MaxHeight = 460,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed,
        };
        _footer = Theme.Label("", 11);
        _footer.Margin = new Thickness(18, 0, 18, 10);
        _footer.Visibility = Visibility.Collapsed;

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_divider);
        stack.Children.Add(_resultsScroll);
        stack.Children.Add(_answerScroll);
        stack.Children.Add(_footer);

        Content = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Child = stack,
            Margin = new Thickness(16), // room for the shadow
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35 },
        };

        Deactivated += (_, _) => HideBar();
        SizeChanged += (_, _) => Place();
        Settings.Shared.Changed += () => Dispatcher.Invoke(() =>
        {
            Refresh();
            RetryAfterError();
        });
        Refresh();
    }

    /// Re-applies colours and texts: theme, language and model may have changed.
    private void Refresh()
    {
        var border = (Border)Content;
        border.Background = Theme.Background;
        border.BorderBrush = Theme.Border;
        _divider.Background = Theme.Border;
        _input.Foreground = Theme.Text;
        _input.CaretBrush = Theme.Text;
        _answer.Foreground = Theme.Text;
        _asked.Foreground = Theme.Secondary;
        _footer.Foreground = Theme.Secondary;
        _placeholder.Text = _hasAnswer ? S.FollowupPlaceholder : S.SearchPlaceholder;
        _modelButton.Content = Settings.Shared.ChoiceTitle;
        _modelButton.Background = Theme.Card;
        _modelButton.Foreground = Theme.Text;
        _footer.Text = _isAnswering ? S.HintStop : S.HintDone("Ctrl+C");
        RenderResults();
    }

    // MARK: showing and hiding

    public void Toggle()
    {
        if (IsVisible) HideBar();
        else ShowBar();
    }

    public void ShowBar()
    {
        // A fresh start after a few minutes away, like Spotlight; a quick reopen keeps the answer.
        if (DateTime.Now - _hiddenAt > TimeSpan.FromMinutes(5)) ResetAll();
        Refresh();
        Show();
        Place();
        Activate();
        _input.Focus();
        Keyboard.Focus(_input);
    }

    private void HideBar()
    {
        if (!IsVisible) return;
        Hide();
        _hiddenAt = DateTime.Now;
    }

    /// Centred on the screen under the mouse, its top edge a fifth of the way down.
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var left = screen.Left * scale + (screen.Width * scale - ActualWidth) / 2;
        var top = screen.Top * scale + screen.Height * scale * 0.2;
        Left = double.IsNaN(left) ? area.Left : left;
        Top = top;
    }

    private void ResetAll()
    {
        _claude.Reset();
        _api.Reset();
        _gemini.Reset();
        _codex.Reset();
        _hasAnswer = false;
        _isAnswering = false;
        _input.Text = "";
        _hits = new List<Hit>();
        _selection = 0;
        ShowResultsMode();
    }

    // MARK: searching

    private void OnQueryChanged()
    {
        _placeholder.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_hasAnswer) return;
        var text = _input.Text;
        _selection = LooksLikeQuestion(text) ? 0 : FirstHit;
        _search.Search(text, hits => Dispatcher.InvokeAsync(() =>
        {
            if (_input.Text != text) return;
            _hits = hits;
            if (!LooksLikeQuestion(text) && _selection == 0 && hits.Count > 0) _selection = FirstHit;
            if (_selection >= RowCount) _selection = Math.Max(RowCount - 1, 0);
            RenderResults();
        }));
        RenderResults();
    }

    private static bool LooksLikeQuestion(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.EndsWith("?")) return true;
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 4) return true;
        return words.Length > 0 && Lang.QuestionWords.Contains(words[0]);
    }

    /// Rows: ask the model, then "Ask in ChatGPT" when shown, then the hits from FirstHit on.
    private int FirstHit => Settings.Shared.ShowChatGpt ? 2 : 1;
    private int RowCount => _input.Text.Trim().Length == 0 ? 0 : FirstHit + _hits.Count;
    private bool IsChatGptRow(int i) => Settings.Shared.ShowChatGpt && i == 1;

    /// Ctrl+Shift+Enter or the ChatGPT row: opens chatgpt.com with the typed text, else the question on screen.
    private void AskChatGpt()
    {
        var text = _input.Text.Trim();
        var question = text.Length > 0 ? text : _asked.Text;
        if (question.Length == 0) return;
        Process.Start(new ProcessStartInfo("https://chatgpt.com/?q=" + Uri.EscapeDataString(question)) { UseShellExecute = true });
        HideBar();
    }

    private void RenderResults()
    {
        if (_hasAnswer) return;
        _results.Children.Clear();
        var count = RowCount;
        _divider.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _resultsScroll.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (count == 0) return;
        if (_selection >= count) _selection = count - 1;

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var selected = i == _selection;
            var row = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };
            FrameworkElement icon;
            StackPanel texts = new() { VerticalAlignment = VerticalAlignment.Center };
            var fg = selected ? Brushes.White : Theme.Text;
            var sub = selected ? Theme.Brush("#DDFFFFFF") : Theme.Secondary;
            if (i == 0)
            {
                icon = new MascotView(selected ? Brushes.White : null, selected ? Theme.Selection : null) { Width = 28, Height = 20 };
                texts.Children.Add(Theme.Label(S.AskRow(Settings.Shared.ChoiceTitle), 14, fg, FontWeights.SemiBold));
                texts.Children.Add(Theme.Label(_input.Text, 12, sub));
                var hint = Theme.Label("Ctrl+↩", 12, sub);
                DockPanel.SetDock(hint, Dock.Right);
                row.Children.Add(hint);
            }
            else if (IsChatGptRow(i))
            {
                icon = Theme.Label("💬", 18, fg);
                texts.Children.Add(Theme.Label(S.AskChatgpt, 14, fg, FontWeights.SemiBold));
                texts.Children.Add(Theme.Label(S.ChatgptNote, 12, sub));
                var hint = Theme.Label("Ctrl+Shift+↩", 12, sub);
                DockPanel.SetDock(hint, Dock.Right);
                row.Children.Add(hint);
            }
            else
            {
                var hit = _hits[i - FirstHit];
                icon = new System.Windows.Controls.Image { Width = 28, Height = 28, Source = IconFor(hit.Path) };
                texts.Children.Add(Theme.Label(hit.Name, 14, fg, FontWeights.Medium));
                texts.Children.Add(Theme.Label(hit.Subtitle, 11, sub));
            }
            icon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(texts);

            var item = new Border
            {
                Child = row,
                CornerRadius = new CornerRadius(8),
                Background = selected ? Theme.Selection : Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            item.MouseLeftButtonUp += (_, _) =>
            {
                _selection = index;
                Run(false, false);
            };
            _results.Children.Add(item);
        }
    }

    private static readonly Dictionary<string, ImageSource?> IconCache = new();

    private static ImageSource? IconFor(string path)
    {
        if (IconCache.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon != null)
            {
                image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
            }
        }
        catch
        {
            // Folders and locked files have no extractable icon.
        }
        IconCache[path] = image;
        return image;
    }

    // MARK: keys

    private void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        switch (e.Key)
        {
            case Key.Escape:
                Escape();
                e.Handled = true;
                break;
            case Key.Down when !_hasAnswer && RowCount > 0:
                _selection = (_selection + 1) % RowCount;
                RenderResults();
                e.Handled = true;
                break;
            case Key.Up when !_hasAnswer && RowCount > 0:
                _selection = (_selection - 1 + RowCount) % RowCount;
                RenderResults();
                e.Handled = true;
                break;
            case Key.Enter when ctrl && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                AskChatGpt();
                e.Handled = true;
                break;
            case Key.Enter:
                Run(ctrl, alt);
                e.Handled = true;
                break;
            case Key.System when e.SystemKey == Key.Enter:
                Run(false, true);
                e.Handled = true;
                break;
            case Key.C when ctrl && _hasAnswer && _input.SelectionLength == 0 && _input.Text.Length == 0:
                if (_answer.Text.Length > 0) Clipboard.SetText(_answer.Text);
                e.Handled = true;
                break;
        }
    }

    private void Run(bool forceAsk, bool reveal)
    {
        var text = _input.Text.Trim();
        if (text.Length == 0)
        {
            RetryAfterError();
            return;
        }
        if (_hasAnswer || forceAsk || _selection == 0 || _selection >= RowCount)
        {
            Ask(text);
            return;
        }
        if (IsChatGptRow(_selection))
        {
            AskChatGpt();
            return;
        }
        var hit = _hits[_selection - FirstHit];
        try
        {
            if (reveal) Process.Start("explorer.exe", $"/select,\"{hit.Path}\"");
            else Process.Start(new ProcessStartInfo(hit.Path) { UseShellExecute = true });
        }
        catch
        {
            System.Media.SystemSounds.Beep.Play();
        }
        HideBar();
    }

    private void Escape()
    {
        if (_isAnswering)
        {
            _claude.Cancel();
            _api.Cancel();
            _gemini.Cancel();
            _codex.Cancel();
            SetAnswering(false);
        }
        else if (_hasAnswer)
        {
            _claude.Reset();
            _api.Reset();
            _gemini.Reset();
            _codex.Reset();
            _hasAnswer = false;
            _input.Text = "";
            ShowResultsMode();
        }
        else if (_input.Text.Length > 0)
        {
            _input.Text = "";
        }
        else
        {
            HideBar();
        }
    }

    // MARK: answering

    private void ShowResultsMode()
    {
        _answerScroll.Visibility = Visibility.Collapsed;
        _footer.Visibility = Visibility.Collapsed;
        _placeholder.Text = S.SearchPlaceholder;
        RenderResults();
    }

    private void SetAnswering(bool value)
    {
        _isAnswering = value;
        _mascot.IsWalking = value;
        _footer.Text = value ? S.HintStop : S.HintDone("Ctrl+C");
        if (!value && _answer.Text.Length == 0 && _error.Text.Length == 0) _answer.Text = "…";
    }

    /// Asks the last question again when its answer failed: Enter on an empty bar, or a switch of model.
    private void RetryAfterError()
    {
        if (!_hasAnswer || _isAnswering || _error.Text.Length == 0 || _asked.Text.Length == 0) return;
        _claude.Reset();
        _api.Reset();
        _gemini.Reset();
        _codex.Reset();
        Ask(_asked.Text);
    }

    private void Ask(string question)
    {
        _hasAnswer = true;
        _asked.Text = question;
        _answer.Text = "";
        _error.Text = "";
        _error.Visibility = Visibility.Collapsed;
        _input.Text = "";
        _placeholder.Text = S.FollowupPlaceholder;
        _resultsScroll.Visibility = Visibility.Collapsed;
        _divider.Visibility = Visibility.Visible;
        _answerScroll.Visibility = Visibility.Visible;
        _footer.Visibility = Visibility.Visible;
        _answer.Text = S.Thinking;
        var first = true;
        SetAnswering(true);

        void Post(Action a) => Dispatcher.InvokeAsync(a);
        void OnText(string chunk)
        {
            if (first)
            {
                _answer.Text = "";
                first = false;
            }
            _answer.Text += chunk;
            _answerScroll.ScrollToEnd();
        }
        void OnDone(string? error)
        {
            if (first) _answer.Text = "";
            SetAnswering(false);
            if (error != null)
            {
                _error.Text = error;
                _error.Visibility = Visibility.Visible;
            }
        }

        _claude.Cancel();
        _api.Cancel();
        _gemini.Cancel();
        _codex.Cancel();
        if (Settings.Shared.Connection is Connection c) _api.Ask(question, c, Post, OnText, OnDone);
        else if (Settings.Shared.GeminiModel is GeminiModel g) _gemini.Ask(question, g, Post, OnText, OnDone);
        else if (Settings.Shared.IsCodex) _codex.Ask(question, Post, OnText, OnDone);
        else _claude.Ask(question, Post, OnText, OnDone);
    }

    // MARK: model menu

    private void ShowModelMenu()
    {
        var menu = ModelMenu.Build(() => App.Current.ShowWelcome(addConnection: true));
        menu.PlacementTarget = _modelButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}

/// The model list shared by the bar's button and the welcome window.
public static class ModelMenu
{
    public static ContextMenu Build(Action addConnection)
    {
        var menu = new ContextMenu();
        foreach (var m in ClaudeModel.All)
        {
            var choice = "claude:" + m.Id;
            var item = new MenuItem { Header = $"{m.Title} — {m.Note}", IsChecked = Settings.Shared.Choice == choice };
            item.Click += (_, _) => Settings.Shared.Select(choice);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var codex = new MenuItem { Header = $"{S.CodexTitle} — {S.NoteCodex}", IsChecked = Settings.Shared.IsCodex };
        codex.Click += (_, _) => Settings.Shared.Select("codex:");
        menu.Items.Add(codex);
        menu.Items.Add(new Separator());
        foreach (var m in GeminiModel.All)
        {
            var choice = "gemini:" + m.Id;
            var item = new MenuItem { Header = $"{m.Title} — {m.Note}", IsChecked = Settings.Shared.Choice == choice };
            item.Click += (_, _) => Settings.Shared.Select(choice);
            menu.Items.Add(item);
        }
        if (Settings.Shared.Connections.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var c in Settings.Shared.Connections)
            {
                var choice = "custom:" + c.Id;
                var item = new MenuItem { Header = $"{c.Name} — {c.Model}", IsChecked = Settings.Shared.Choice == choice };
                item.Click += (_, _) => Settings.Shared.Select(choice);
                menu.Items.Add(item);
            }
        }
        menu.Items.Add(new Separator());
        var add = new MenuItem { Header = S.ConnectModel };
        add.Click += (_, _) => addConnection();
        menu.Items.Add(add);
        return menu;
    }
}
