using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lumi;

/// The window shown when the app is opened: status, model, language, connections, startup options.
public sealed class WelcomeWindow : Window
{
    private readonly Action _openSearch;
    private ClaudeAuth.State _auth = ClaudeAuth.State.Checking;

    public WelcomeWindow(Action openSearch)
    {
        _openSearch = openSearch;
        Title = "Lumi";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            Mascot.TrayIcon().Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

        Settings.Shared.Changed += () => Dispatcher.Invoke(Build);
        Activated += async (_, _) =>
        {
            _auth = await ClaudeAuth.CheckAsync();
            Build();
        };
        Build();
    }

    /// Called once a newer release is found; redraws the window so it shows the download link.
    public void ShowUpdate(string version) => Build();

    private void Build()
    {
        Background = Theme.Background;
        var stack = new StackPanel { Margin = new Thickness(24) };

        var mascot = new MascotView { Width = 84, Height = 72, Margin = new Thickness(0, 10, 0, 0) };
        mascot.MouseEnter += (_, _) => mascot.IsWalking = true;
        mascot.MouseLeave += (_, _) => mascot.IsWalking = false;
        stack.Children.Add(mascot);

        var title = Theme.Label("Lumi", 26, null, FontWeights.Bold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(title);
        var subtitle = Theme.Label(S.WelcomeSubtitle, 13, Theme.Secondary);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(subtitle);

        var hotkey = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        hotkey.Children.Add(KeyCap("Alt"));
        hotkey.Children.Add(KeyCap(Theme.HotkeyKey));
        hotkey.Children.Add(Theme.Label(" " + S.HotkeyHint, 13, Theme.Secondary));
        stack.Children.Add(hotkey);

        if (Updates.NewVersion is { } version)
        {
            var update = LinkButton("⬆ " + S.UpdateAvailable(version), Updates.OpenReleases);
            update.HorizontalAlignment = HorizontalAlignment.Center;
            update.FontWeight = FontWeights.SemiBold;
            update.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(update);
        }

        var card = new StackPanel();
        card.Children.Add(AuthRow());
        card.Children.Add(Row(S.Model, ModelButton()));
        card.Children.Add(Row(S.Language, LanguageBox()));
        foreach (var c in Settings.Shared.Connections) card.Children.Add(ConnectionRow(c));
        var add = LinkButton(S.ConnectModel, () => AddConnection());
        card.Children.Add(Row(null, add));
        if (!Settings.Shared.Connections.Exists(c => c.BaseUrl.Contains("localhost:11434")))
            card.Children.Add(Row(null, LinkButton("✈ " + S.OllamaOffer, () => AddConnection("ollama"))));
        card.Children.Add(Row(S.LaunchAtLogin, Check(Settings.LaunchesAtLogin, v => Settings.LaunchesAtLogin = v)));
        card.Children.Add(Row(S.ShowOnLaunch, Check(Settings.Shared.ShowWelcomeOnLaunch, v =>
        {
            Settings.Shared.ShowWelcomeOnLaunch = v;
            Settings.Shared.Save();
        })));
        card.Children.Add(Row(S.ShowChatgpt, Check(Settings.Shared.ShowChatGpt, v =>
        {
            Settings.Shared.ShowChatGpt = v;
            Settings.Shared.Save();
        }), last: true));
        stack.Children.Add(new Border
        {
            Child = card,
            Background = Theme.Card,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 22, 0, 0),
        });

        var open = new Button
        {
            Content = S.OpenSearch,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = Theme.Accent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 10, 0, 10),
            Margin = new Thickness(0, 22, 0, 0),
            IsDefault = true,
            Cursor = Cursors.Hand,
        };
        open.Click += (_, _) =>
        {
            Hide();
            _openSearch();
        };
        stack.Children.Add(open);
        var quit = LinkButton(S.QuitApp, () => App.Current.Quit());
        quit.Foreground = Theme.Secondary;
        quit.HorizontalAlignment = HorizontalAlignment.Center;
        quit.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(quit);
        Content = stack;
    }

    public void AddConnection(string? preset = null)
    {
        var dialog = new ConnectionDialog(preset) { Owner = IsVisible ? this : null };
        if (dialog.ShowDialog() == true && dialog.Result != null) Settings.Shared.Add(dialog.Result);
    }

    private UIElement AuthRow()
    {
        var (color, text) = _auth switch
        {
            ClaudeAuth.State.SignedIn => (Brushes.MediumSeaGreen, S.AuthOk),
            ClaudeAuth.State.SignedOut => (Brushes.Orange, S.AuthSignedOut),
            ClaudeAuth.State.Missing => (Brushes.IndianRed, S.AuthMissing),
            _ => (Brushes.Gray, S.AuthChecking),
        };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = color, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(Theme.Label(text));
        UIElement? action = _auth switch
        {
            ClaudeAuth.State.SignedOut => LinkButton(S.SignIn, ClaudeAuth.SignIn),
            ClaudeAuth.State.Missing => LinkButton(S.Download, () => Process.Start(new ProcessStartInfo("https://claude.com/claude-code") { UseShellExecute = true })),
            _ => null,
        };
        var dock = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };
        if (action != null)
        {
            DockPanel.SetDock(action, Dock.Right);
            dock.Children.Add(action);
        }
        dock.Children.Add(left);
        return WithDivider(dock, false);
    }

    private UIElement ConnectionRow(Connection c)
    {
        var texts = new StackPanel();
        texts.Children.Add(Theme.Label(c.Name));
        texts.Children.Add(Theme.Label(c.Model, 11, Theme.Secondary));
        var remove = new Button
        {
            Content = "✕",
            ToolTip = S.DeleteConnection,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Theme.Secondary,
            Cursor = Cursors.Hand,
            Padding = new Thickness(6, 2, 6, 2),
        };
        remove.Click += (_, _) => Settings.Shared.Remove(c);
        var dock = new DockPanel { Margin = new Thickness(14, 8, 14, 8) };
        DockPanel.SetDock(remove, Dock.Right);
        dock.Children.Add(remove);
        dock.Children.Add(texts);
        return WithDivider(dock, false);
    }

    private static UIElement Row(string? label, UIElement control, bool last = false)
    {
        var dock = new DockPanel { Margin = new Thickness(14, 8, 14, 8) };
        if (label != null)
        {
            DockPanel.SetDock(control, Dock.Right);
            dock.Children.Add(control);
            dock.Children.Add(Theme.Label(label));
        }
        else
        {
            dock.Children.Add(control);
        }
        return WithDivider(dock, last);
    }

    private static UIElement WithDivider(UIElement content, bool last)
    {
        var panel = new StackPanel();
        panel.Children.Add(content);
        if (!last) panel.Children.Add(new Border { Height = 1, Background = Theme.Border, Margin = new Thickness(14, 0, 0, 0) });
        return panel;
    }

    private UIElement ModelButton()
    {
        var button = new Button
        {
            Content = Settings.Shared.ChoiceTitle + "  ▾",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Theme.Text,
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) =>
        {
            var menu = ModelMenu.Build(() => AddConnection());
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return button;
    }

    private static UIElement LanguageBox()
    {
        var box = new ComboBox { MinWidth = 140 };
        box.Items.Add(S.LanguageSystem);
        foreach (var name in Lang.Names) box.Items.Add(name);
        box.SelectedIndex = Array.IndexOf(Lang.Codes, Settings.Shared.Language) + 1;
        box.SelectionChanged += (_, _) =>
        {
            Settings.Shared.Language = box.SelectedIndex <= 0 ? "" : Lang.Codes[box.SelectedIndex - 1];
            Settings.Shared.Save();
        };
        return box;
    }

    private static UIElement Check(bool value, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => set(box.IsChecked == true);
        return box;
    }

    private static Button LinkButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Theme.Accent,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static UIElement KeyCap(string label) => new Border
    {
        Child = Theme.Label(label, 13, null, FontWeights.Medium),
        Padding = new Thickness(8, 2, 8, 2),
        Margin = new Thickness(0, 0, 6, 0),
        CornerRadius = new CornerRadius(6),
        Background = Theme.Card,
        BorderBrush = Theme.Border,
        BorderThickness = new Thickness(1),
    };

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing only hides: the app keeps running in the tray.
        e.Cancel = true;
        Hide();
    }
}

/// The add-connection form.
public sealed class ConnectionDialog : Window
{
    private readonly ComboBox _preset = new();
    private readonly TextBox _name = new();
    private readonly TextBox _url = new();
    private readonly TextBox _model = new();
    private readonly PasswordBox _key = new();
    private readonly TextBlock _modelHint;
    private readonly TextBlock _keyHint;
    private readonly StackPanel _ollamaHelp = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _save;

    public Connection? Result { get; private set; }

    public ConnectionDialog(string? preset = null)
    {
        Title = S.FormTitle;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        Background = Theme.Background;

        foreach (var p in ServicePreset.All) _preset.Items.Add(p);
        _modelHint = Theme.Label("", 11, Theme.Secondary);
        _keyHint = Theme.Label("", 11, Theme.Secondary);

        var stack = new StackPanel { Margin = new Thickness(22) };
        stack.Children.Add(Theme.Label(S.FormTitle, 17, null, FontWeights.SemiBold));
        var sub = Theme.Label(S.FormSubtitle, 12, Theme.Secondary);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 4, 0, 10);
        stack.Children.Add(sub);
        stack.Children.Add(Field(S.Service, _preset));
        stack.Children.Add(Field(S.Name, _name));
        stack.Children.Add(Field(S.Address, _url));
        stack.Children.Add(Field(S.Model, _model, _modelHint));
        stack.Children.Add(Field(S.ApiKey, _key, _keyHint));
        stack.Children.Add(_ollamaHelp);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = S.Cancel, IsCancel = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        _save = new Button { Content = S.Connect, IsDefault = true, Padding = new Thickness(14, 4, 14, 4) };
        _save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_save);
        stack.Children.Add(buttons);
        Content = stack;

        _preset.SelectionChanged += (_, _) => ApplyPreset();
        _name.TextChanged += (_, _) => Validate();
        _url.TextChanged += (_, _) => Validate();
        _model.TextChanged += (_, _) => Validate();
        _preset.SelectedIndex = Math.Max(0, Array.FindIndex(ServicePreset.All, p => p.Key == preset));
    }

    private static UIElement Field(string label, Control control, TextBlock? hint = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(Theme.Label(label, 12, Theme.Secondary));
        control.Margin = new Thickness(0, 2, 0, 0);
        control.Padding = new Thickness(4, 3, 4, 3);
        panel.Children.Add(control);
        if (hint != null) panel.Children.Add(hint);
        return panel;
    }

    private void ApplyPreset()
    {
        if (_preset.SelectedItem is not ServicePreset p) return;
        var isPresetName = _name.Text.Length == 0 || Array.Exists(ServicePreset.All, x => x.Title == _name.Text);
        if (isPresetName) _name.Text = p.Title;
        _url.Text = p.BaseUrl;
        _modelHint.Text = p.ModelHint;
        _keyHint.Text = p.Key == "GigaChat" ? S.HintGigachatKey : p.NeedsKey ? S.KeyStored : S.KeyNotNeeded;
        _ollamaHelp.Children.Clear();
        if (p.Key == "ollama") CheckOllama();
        Validate();
    }

    private const string OllamaDownload = "https://ollama.com/download";
    private const string PullCommand = "ollama pull llama3.2";

    /// Under the Ollama preset: whether it's installed, with a download link and the first command if not.
    private async void CheckOllama()
    {
        _ollamaHelp.Children.Clear();
        _ollamaHelp.Children.Add(Theme.Label("Ollama…", 12, Theme.Secondary));
        var models = await Ollama.ModelsAsync();
        if (_preset.SelectedItem is not ServicePreset { Key: "ollama" }) return;
        _ollamaHelp.Children.Clear();
        TextBlock Note(string s, Brush? color = null)
        {
            var label = Theme.Label(s, 12, color ?? Theme.Text);
            label.TextWrapping = TextWrapping.Wrap;
            label.TextTrimming = TextTrimming.None;
            return label;
        }
        if (models == null)
        {
            _ollamaHelp.Children.Add(Note(S.OllamaMissing));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            var download = new Button { Content = S.OllamaDownload, Padding = new Thickness(12, 3, 12, 3), Foreground = Brushes.White, Background = Theme.Accent, BorderThickness = new Thickness(0) };
            download.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OllamaDownload) { UseShellExecute = true });
            var again = new Button { Content = S.CheckAgain, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
            again.Click += (_, _) => CheckOllama();
            buttons.Children.Add(download);
            buttons.Children.Add(again);
            _ollamaHelp.Children.Add(buttons);
            _ollamaHelp.Children.Add(Note(S.OllamaPull(PullCommand), Theme.Secondary));
        }
        else if (models.Count == 0)
        {
            _ollamaHelp.Children.Add(Note(S.OllamaNoModels(PullCommand)));
            var again = new Button { Content = S.CheckAgain, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            again.Click += (_, _) => CheckOllama();
            _ollamaHelp.Children.Add(again);
        }
        else
        {
            _ollamaHelp.Children.Add(Note("✓ " + S.OllamaReady(models.Count.ToString()), Theme.Accent));
            if (_model.Text.Trim().Length == 0) _model.Text = models[0];
        }
    }

    private void Validate()
    {
        _save.IsEnabled = _name.Text.Trim().Length > 0
            && Uri.TryCreate(_url.Text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http")
            && _model.Text.Trim().Length > 0;
    }

    private void Save()
    {
        Result = new Connection { Name = _name.Text.Trim(), BaseUrl = _url.Text.Trim(), Model = _model.Text.Trim() };
        Result.Key = _key.Password.Trim();
        DialogResult = true;
    }
}
