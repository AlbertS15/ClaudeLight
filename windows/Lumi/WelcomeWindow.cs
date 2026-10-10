using System;
using System.Collections.Generic;
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

        stack.Children.Add(HotkeyRow());

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
        if (!Settings.Shared.Connections.Exists(c => Ollama.IsOllama(c.BaseUrl)))
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

    private bool _recordingHotkey;
    private bool _hotkeyTaken;

    /// The shortcut as key caps, with a way to pick another one.
    private UIElement HotkeyRow()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        var keys = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (_recordingHotkey)
        {
            var prompt = Theme.Label(S.HotkeyRecord, 12, Theme.Accent);
            prompt.TextWrapping = TextWrapping.Wrap;
            prompt.TextAlignment = TextAlignment.Center;
            prompt.MaxWidth = 320;
            keys.Children.Add(prompt);
        }
        else
        {
            foreach (var cap in Shortcut.Current.Caps) keys.Children.Add(KeyCap(cap));
            keys.Children.Add(Theme.Label(" " + S.HotkeyHint, 13, Theme.Secondary));
        }
        panel.Children.Add(keys);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        var change = LinkButton(_recordingHotkey ? S.Cancel : S.HotkeyChange, () =>
        {
            if (_recordingHotkey) FinishRecording(null);
            else StartRecording();
        });
        change.FontSize = 11;
        actions.Children.Add(change);
        if (!Shortcut.Current.IsStandard && !_recordingHotkey)
        {
            var reset = LinkButton(S.HotkeyReset, () => FinishRecording(Shortcut.Standard));
            reset.FontSize = 11;
            reset.Margin = new Thickness(12, 0, 0, 0);
            actions.Children.Add(reset);
        }
        panel.Children.Add(actions);
        if (_hotkeyTaken)
        {
            var taken = Theme.Label(S.HotkeyTaken, 11, Theme.Error);
            taken.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(taken);
        }
        return panel;
    }

    private void StartRecording()
    {
        _recordingHotkey = true;
        _hotkeyTaken = false;
        // Let go of the current shortcut, so pressing it now records it instead of opening the bar.
        App.Current.SetHotkey(null);
        PreviewKeyDown += OnRecordKey;
        Build();
    }

    private void OnRecordKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            FinishRecording(null);
            return;
        }
        var win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        if (Shortcut.From(key, Keyboard.Modifiers, win) is { } shortcut) FinishRecording(shortcut);
    }

    /// Applies the new shortcut (null keeps the old one); an app already holding it brings the old one back.
    private void FinishRecording(Shortcut? key)
    {
        PreviewKeyDown -= OnRecordKey;
        _recordingHotkey = false;
        _hotkeyTaken = false;
        if (key != null && App.Current.SetHotkey(key))
        {
            Settings.Shared.HotkeyModifiers = key.Modifiers;
            Settings.Shared.HotkeyKey = key.Key;
            Settings.Shared.Save();
        }
        else
        {
            _hotkeyTaken = key != null;
            App.Current.SetHotkey(Shortcut.Current);
        }
        Build();
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
    private readonly StackPanel _lumiPanel = new() { Margin = new Thickness(0, 6, 0, 6) };
    private LumiRecipe _recipe = Ollama.Recommended;
    private System.Threading.CancellationTokenSource? _install;
    private double _installed = -1;
    private bool _building;
    private bool _ollamaMissing;
    private double _ollamaDownload = -1;
    private bool _ollamaStarting;
    private string? _installError;
    private List<string> _models = new();

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
        _ollamaMissing = models == null;
        if (models == null)
        {
            _models = new();
            _ollamaHelp.Children.Add(Note(S.OllamaMissing));
            _ollamaHelp.Children.Remove(_lumiPanel);
            _ollamaHelp.Children.Add(_lumiPanel);
            RenderLumi();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            var download = new Button { Content = S.OllamaDownload, Padding = new Thickness(12, 3, 12, 3) };
            download.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OllamaDownload) { UseShellExecute = true });
            var again = new Button { Content = S.CheckAgain, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
            again.Click += (_, _) => CheckOllama();
            buttons.Children.Add(download);
            buttons.Children.Add(again);
            _ollamaHelp.Children.Add(buttons);
        }
        else
        {
            _models = models;
            _ollamaHelp.Children.Add(Note("✓ " + S.OllamaReady(models.Count.ToString()), Theme.Accent));
            _ollamaHelp.Children.Remove(_lumiPanel);
            _ollamaHelp.Children.Add(_lumiPanel);
            RenderLumi();
            if (models.Count == 0) return;
            if (_model.Text.Trim().Length == 0) _model.Text = models[0];
            // The exact installed names, so the model field can't hold a name Ollama doesn't have.
            _ollamaHelp.Children.Add(Note(S.OllamaPick, Theme.Secondary));
            var list = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var m in models)
            {
                var pick = new Button { Content = m, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 6) };
                pick.Click += (_, _) => _model.Text = m;
                list.Children.Add(pick);
            }
            _ollamaHelp.Children.Add(list);
        }
    }

    /// Lumi's own models: pick a size, then download and set it up in one press.
    private void RenderLumi()
    {
        _lumiPanel.Children.Clear();
        TextBlock Note(string s, Brush? color = null)
        {
            var label = Theme.Label(s, 12, color ?? Theme.Text);
            label.TextWrapping = TextWrapping.Wrap;
            label.Margin = new Thickness(0, 2, 0, 2);
            return label;
        }
        var busy = _install != null;
        _lumiPanel.Children.Add(Note(S.LumiOwn));
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        foreach (var r in LumiModels.All)
        {
            var option = new RadioButton { Content = r.Title, IsChecked = r == _recipe, IsEnabled = !busy, Margin = new Thickness(0, 0, 14, 0), Foreground = Theme.Text };
            option.Checked += (_, _) => { _recipe = r; _installError = null; RenderLumi(); };
            sizes.Children.Add(option);
        }
        _lumiPanel.Children.Add(sizes);
        var fit = S.LumiFit(_recipe.Title, _recipe.MemoryGB.ToString(), Ollama.MemoryGB.ToString());
        if (_recipe.MemoryGB > Ollama.MemoryGB) fit += " " + S.LumiSlow;
        _lumiPanel.Children.Add(Note(fit, Theme.Secondary));

        if (busy)
        {
            var part = _ollamaDownload >= 0 ? _ollamaDownload : Math.Max(0, _installed);
            var waiting = _building || _ollamaStarting;
            var label = _ollamaStarting ? S.OllamaStarting
                : _ollamaDownload >= 0 ? S.OllamaDownloading(((int)(part * 100)).ToString())
                : _building ? S.LumiBuilding(_recipe.Title)
                : S.LumiDownloading(_recipe.Title, ((int)(part * 100)).ToString());
            _lumiPanel.Children.Add(Note(label));
            _lumiPanel.Children.Add(new ProgressBar { Height = 6, Maximum = 1, Value = part, IsIndeterminate = waiting, Foreground = Theme.Accent, Margin = new Thickness(0, 2, 0, 0) });
            return;
        }
        if (_ollamaMissing && _installError == null) _lumiPanel.Children.Add(Note(S.OllamaAuto(OllamaSetup.SizeGB), Theme.Secondary));
        if (_installError != null) _lumiPanel.Children.Add(Note(S.LumiFailed(_installError), Theme.Error));
        if (Ollama.IsInstalled(_recipe, _models))
        {
            var use = new Button { Content = S.LumiUse(_recipe.Title), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            use.Click += (_, _) => _model.Text = _recipe.Name;
            _lumiPanel.Children.Add(use);
        }
        else
        {
            var size = _recipe.SizeGB.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
            var install = new Button { Content = S.LumiInstall(_recipe.Title, size), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Foreground = Brushes.White, Background = Theme.Accent, BorderThickness = new Thickness(0) };
            install.Click += (_, _) => InstallLumi();
            _lumiPanel.Children.Add(install);
        }
    }

    /// Downloads the chosen model's base, builds the Lumi model on it and fills it in.
    private async void InstallLumi()
    {
        var recipe = _recipe;
        _install = new System.Threading.CancellationTokenSource();
        _installed = 0;
        _building = false;
        _installError = null;
        RenderLumi();
        try
        {
            if (_ollamaMissing)
            {
                _ollamaDownload = 0;
                RenderLumi();
                var shown = -1;
                await OllamaSetup.InstallAsync(
                    part => Dispatcher.InvokeAsync(() =>
                    {
                        _ollamaDownload = part;
                        var percent = (int)(part * 100);
                        if (percent != shown) { shown = percent; RenderLumi(); }
                    }),
                    () => Dispatcher.InvokeAsync(() => { _ollamaDownload = -1; _ollamaStarting = true; RenderLumi(); }),
                    _install.Token);
                _ollamaStarting = false;
                _ollamaMissing = false;
                _models = await Ollama.ModelsAsync() ?? new();
                RenderLumi();
            }
            var last = -1;
            await Ollama.PullAsync(recipe.Base, part => Dispatcher.InvokeAsync(() =>
            {
                _installed = part;
                // Redraw on whole percents only, not on every chunk.
                var percent = (int)(part * 100);
                if (percent != last) { last = percent; RenderLumi(); }
            }), _install.Token);
            _building = true;
            RenderLumi();
            await Ollama.CreateAsync(recipe, _install.Token);
            _model.Text = recipe.Name;
            _install = null;
            CheckOllama();
        }
        catch (Exception e)
        {
            _install = null;
            _ollamaDownload = -1;
            _ollamaStarting = false;
            if (IsVisible) { _installError = e is OperationCanceledException ? "timeout" : e.Message; RenderLumi(); }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _install?.Cancel();
        base.OnClosed(e);
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
