using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lumi;

/// The Spotlight-style bar: one text box, then either results or an answer.
public sealed class LauncherWindow : Window
{
    private readonly FileSearch _search = new();
    private readonly ClaudeRunner _claude = new();
    private readonly ApiRunner _api = new();
    private readonly GeminiRunner _gemini = new();
    private readonly CodexRunner _codex = new();

    private readonly TextBox _input = new();
    private readonly MascotView _mascot = new();
    private readonly Button _modelButton = new();
    private readonly StackPanel _results = new();
    private readonly ScrollViewer _resultsScroll;
    private readonly SelectableTextBlock _answer = new();
    private readonly TextBlock _asked;
    private readonly TextBlock _error;
    private readonly TextBlock _footer;
    private readonly ScrollViewer _answerScroll;
    private readonly StackPanel _answerPanel;
    private readonly Border _divider;
    private readonly Border _frame;

    private List<Hit> _hits = new();
    private int _selection;
    private bool _hasAnswer;
    private bool _isAnswering;
    private DateTime _hiddenAt = DateTime.MinValue;

    public LauncherWindow()
    {
        Title = "Lumi";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Icon = new WindowIcon(Mascot.Icon());

        _input.FontSize = 22;
        _input.BorderThickness = new Thickness(0);
        _input.Background = Brushes.Transparent;
        _input.VerticalContentAlignment = VerticalAlignment.Center;
        _input.MinHeight = 40;
        _input.Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
        _input.Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
        _input.Resources["TextControlBorderBrushFocused"] = Brushes.Transparent;
        _input.Resources["TextControlBorderBrushPointerOver"] = Brushes.Transparent;
        _input.TextChanged += (_, _) => OnQueryChanged();
        _input.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);

        _mascot.Width = 26;
        _mascot.Height = 24;
        _mascot.Margin = new Thickness(0, 0, 12, 0);
        _mascot.VerticalAlignment = VerticalAlignment.Center;

        _modelButton.Padding = new Thickness(10, 3, 10, 3);
        _modelButton.BorderThickness = new Thickness(0);
        _modelButton.FontSize = 12;
        _modelButton.Cursor = new Cursor(StandardCursorType.Hand);
        _modelButton.VerticalAlignment = VerticalAlignment.Center;
        _modelButton.Click += (_, _) => ShowModelMenu();

        var top = new DockPanel { Height = 58, Margin = new Thickness(18, 0, 14, 0) };
        DockPanel.SetDock(_mascot, Dock.Left);
        DockPanel.SetDock(_modelButton, Dock.Right);
        top.Children.Add(_mascot);
        top.Children.Add(_modelButton);
        top.Children.Add(_input);

        _divider = new Border { Height = 1, IsVisible = false };

        _resultsScroll = new ScrollViewer
        {
            Content = _results,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsVisible = false,
            Padding = new Thickness(8),
        };

        _asked = Look.Label("", 13, null, FontWeight.SemiBold);
        _asked.TextWrapping = TextWrapping.Wrap;
        _answer.TextWrapping = TextWrapping.Wrap;
        _answer.FontSize = 15;
        _answer.Margin = new Thickness(0, 8, 0, 0);
        _error = Look.Label("", 13, Look.Error);
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
            IsVisible = false,
        };
        _footer = Look.Label("", 11);
        _footer.Margin = new Thickness(18, 0, 18, 10);
        _footer.IsVisible = false;

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_divider);
        stack.Children.Add(_resultsScroll);
        stack.Children.Add(_answerScroll);
        stack.Children.Add(_footer);

        _frame = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Child = stack,
            Margin = new Thickness(16), // room for the shadow
            BoxShadow = BoxShadows.Parse("0 4 24 0 #59000000"),
        };
        Content = _frame;

        Deactivated += (_, _) => HideBar();
        Settings.Shared.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            Refresh();
            RetryAfterError();
        });
        Refresh();
    }

    /// Re-applies colours and texts: theme, language and model may have changed.
    private void Refresh()
    {
        _frame.Background = Look.Background;
        _frame.BorderBrush = Look.Border;
        _divider.Background = Look.Border;
        _input.Foreground = Look.Text;
        _input.CaretBrush = Look.Text;
        _answer.Foreground = Look.Text;
        _asked.Foreground = Look.Secondary;
        _footer.Foreground = Look.Secondary;
        _input.Watermark = _hasAnswer ? S.FollowupPlaceholder : S.SearchPlaceholder;
        _modelButton.Content = Settings.Shared.ChoiceTitle;
        _modelButton.Background = Look.Card;
        _modelButton.Foreground = Look.Text;
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
    }

    private void HideBar()
    {
        if (!IsVisible) return;
        Hide();
        _hiddenAt = DateTime.Now;
    }

    /// Centred on the primary screen, its top edge a fifth of the way down.
    private void Place()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        var width = (int)(Width * screen.Scaling);
        Position = new PixelPoint(area.X + (area.Width - width) / 2, area.Y + area.Height / 5);
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
        if (_hasAnswer) return;
        var text = _input.Text ?? "";
        _selection = LooksLikeQuestion(text) ? 0 : FirstHit;
        _search.Search(text, hits => Dispatcher.UIThread.Post(() =>
        {
            if ((_input.Text ?? "") != text) return;
            _hits = hits;
            // A search lands on the first file or app; a question stays on the ask row.
            if (!LooksLikeQuestion(text) && _selection < FirstHit && hits.Count > 0) _selection = FirstHit;
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
    private int RowCount => (_input.Text ?? "").Trim().Length == 0 ? 0 : FirstHit + _hits.Count;
    private bool IsChatGptRow(int i) => Settings.Shared.ShowChatGpt && i == 1;

    /// Ctrl+Shift+Enter or the ChatGPT row: opens chatgpt.com with the typed text, else the question on screen.
    private void AskChatGpt()
    {
        var text = (_input.Text ?? "").Trim();
        var question = text.Length > 0 ? text : _asked.Text ?? "";
        if (question.Length == 0) return;
        Updates.Open("https://chatgpt.com/?q=" + Uri.EscapeDataString(question));
        HideBar();
    }

    private void RenderResults()
    {
        if (_hasAnswer) return;
        _results.Children.Clear();
        var count = RowCount;
        _divider.IsVisible = count > 0;
        _resultsScroll.IsVisible = count > 0;
        if (count == 0) return;
        if (_selection >= count) _selection = count - 1;

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var selected = i == _selection;
            var row = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };
            Control icon;
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var fg = selected ? Brushes.White : Look.Text;
            var sub = selected ? Look.Brush("#DDFFFFFF") : Look.Secondary;
            if (i == 0)
            {
                icon = new MascotView(selected ? Brushes.White : null, selected ? Look.Selection : null) { Width = 24, Height = 22 };
                texts.Children.Add(Look.Label(S.AskRow(Settings.Shared.ChoiceTitle), 14, fg, FontWeight.SemiBold));
                texts.Children.Add(Look.Label(_input.Text ?? "", 12, sub));
                var hint = Look.Label("Ctrl+↩", 12, sub);
                DockPanel.SetDock(hint, Dock.Right);
                row.Children.Add(hint);
            }
            else if (IsChatGptRow(i))
            {
                icon = Look.Label("💬", 18, fg);
                texts.Children.Add(Look.Label(S.AskChatgpt, 14, fg, FontWeight.SemiBold));
                texts.Children.Add(Look.Label(S.ChatgptNote, 12, sub));
                var hint = Look.Label("Ctrl+Shift+↩", 12, sub);
                DockPanel.SetDock(hint, Dock.Right);
                row.Children.Add(hint);
            }
            else
            {
                var hit = _hits[i - FirstHit];
                icon = Initial(hit);
                texts.Children.Add(Look.Label(hit.Name, 14, fg, FontWeight.Medium));
                texts.Children.Add(Look.Label(hit.Subtitle, 11, sub));
            }
            icon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(texts);

            var item = new Border
            {
                Child = row,
                CornerRadius = new CornerRadius(8),
                Background = selected ? Look.Selection : Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            item.PointerReleased += (_, _) =>
            {
                _selection = index;
                Run(false, false);
            };
            _results.Children.Add(item);
        }
    }

    /// A tile with the hit's first letter: icon themes differ too much between desktops to look them up reliably.
    private static Control Initial(Hit hit)
    {
        var letter = hit.Name.Length > 0 ? char.ToUpperInvariant(hit.Name[0]).ToString() : "?";
        return new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(7),
            Background = hit.IsApp ? Look.Accent : Look.Card,
            Child = new TextBlock
            {
                Text = letter,
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = hit.IsApp ? Brushes.White : Look.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    // MARK: keys

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
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
            case Key.Enter when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                AskChatGpt();
                e.Handled = true;
                break;
            case Key.Enter:
                Run(ctrl, alt);
                e.Handled = true;
                break;
            case Key.C when ctrl && _hasAnswer && (_input.SelectedText ?? "").Length == 0 && (_input.Text ?? "").Length == 0:
                if ((_answer.Text ?? "").Length > 0) Clipboard?.SetTextAsync(_answer.Text);
                e.Handled = true;
                break;
        }
    }

    private void Run(bool forceAsk, bool reveal)
    {
        var text = (_input.Text ?? "").Trim();
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
            if (reveal) hit.Reveal();
            else hit.Open();
        }
        catch
        {
            // Nothing to open it with; the bar just closes.
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
        else if ((_input.Text ?? "").Length > 0)
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
        _answerScroll.IsVisible = false;
        _footer.IsVisible = false;
        _input.Watermark = S.SearchPlaceholder;
        RenderResults();
    }

    private void SetAnswering(bool value)
    {
        _isAnswering = value;
        _mascot.IsWalking = value;
        _footer.Text = value ? S.HintStop : S.HintDone("Ctrl+C");
    }

    /// Asks the last question again when its answer failed: Enter on an empty bar, or a switch of model.
    private void RetryAfterError()
    {
        if (!_hasAnswer || _isAnswering || (_error.Text ?? "").Length == 0 || (_asked.Text ?? "").Length == 0) return;
        _claude.Reset();
        _api.Reset();
        _gemini.Reset();
        _codex.Reset();
        Ask(_asked.Text!);
    }

    private void Ask(string question)
    {
        _hasAnswer = true;
        _asked.Text = question;
        _error.Text = "";
        _error.IsVisible = false;
        _input.Text = "";
        _input.Watermark = S.FollowupPlaceholder;
        _resultsScroll.IsVisible = false;
        _divider.IsVisible = true;
        _answerScroll.IsVisible = true;
        _footer.IsVisible = true;
        _answer.Text = S.Thinking;
        var first = true;
        SetAnswering(true);

        void Post(Action a) => Dispatcher.UIThread.Post(a);
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
                _error.IsVisible = true;
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
        menu.Placement = PlacementMode.Bottom;
        menu.Open(_modelButton);
    }
}
