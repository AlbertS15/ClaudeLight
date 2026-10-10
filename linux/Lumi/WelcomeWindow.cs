using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lumi;

/// The window shown when the app is opened: status, shortcut, model, language, connections, startup options.
public sealed class WelcomeWindow : Window
{
    private readonly Action _openSearch;
    private ClaudeAuth.State _auth = ClaudeAuth.State.Checking;
    private bool _recordingHotkey;
    private bool _hotkeyTaken;

    public WelcomeWindow(Action openSearch)
    {
        _openSearch = openSearch;
        Title = "Lumi";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(Mascot.Icon());

        Settings.Shared.Changed += () => Dispatcher.UIThread.Post(Build);
        Activated += async (_, _) =>
        {
            _auth = await ClaudeAuth.CheckAsync();
            Build();
        };
        AddHandler(KeyDownEvent, OnRecordKey, RoutingStrategies.Tunnel);
        Build();
    }

    /// Called once a newer release is found; redraws the window so it shows the download link.
    public void ShowUpdate(string version) => Build();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing only hides: the app keeps running in the tray.
        if (e.CloseReason != WindowCloseReason.ApplicationShutdown)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void Build()
    {
        Background = Look.Background;
        var stack = new StackPanel { Margin = new Thickness(24) };

        var mascot = new MascotView { Width = 84, Height = 72, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        mascot.PointerEntered += (_, _) => mascot.IsWalking = true;
        mascot.PointerExited += (_, _) => mascot.IsWalking = false;
        stack.Children.Add(mascot);

        var title = Look.Label("Lumi", 26, null, FontWeight.Bold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(title);
        var subtitle = Look.Label(S.WelcomeSubtitle, 13, Look.Secondary);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(subtitle);

        stack.Children.Add(HotkeyRow());

        if (Updates.NewVersion is { } version)
        {
            var update = LinkButton("⬆ " + S.UpdateAvailable(version), Updates.OpenReleases);
            update.HorizontalAlignment = HorizontalAlignment.Center;
            update.FontWeight = FontWeight.SemiBold;
            update.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(update);
        }

        var card = new StackPanel();
        card.Children.Add(AuthRow());
        card.Children.Add(Row(S.Model, ModelButton()));
        card.Children.Add(Row(S.Language, LanguageBox()));
        foreach (var c in Settings.Shared.Connections) card.Children.Add(ConnectionRow(c));
        card.Children.Add(Row(null, LinkButton(S.ConnectModel, () => AddConnection())));
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
            Background = Look.Card,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 22, 0, 0),
        });

        var open = new Button
        {
            Content = S.OpenSearch,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Background = Look.Accent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 10, 0, 10),
            Margin = new Thickness(0, 22, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsDefault = true,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        open.Click += (_, _) =>
        {
            Hide();
            _openSearch();
        };
        stack.Children.Add(open);
        var quit = LinkButton(S.QuitApp, () => App.Current.Quit());
        quit.Foreground = Look.Secondary;
        quit.HorizontalAlignment = HorizontalAlignment.Center;
        quit.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(quit);
        Content = stack;
    }

    // MARK: the shortcut

    /// The shortcut as key caps, with a way to pick another one; on desktops where Lumi can't set it, the command to bind.
    private Control HotkeyRow()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        if (Hotkeys.Mode == HotkeyMode.Manual)
        {
            var note = Look.Label(S.HotkeyManual, 12, Look.Secondary);
            note.TextWrapping = TextWrapping.Wrap;
            note.TextAlignment = TextAlignment.Center;
            note.MaxWidth = 360;
            panel.Children.Add(note);
            var command = new SelectableTextBlock
            {
                Text = Hotkeys.ToggleCommand,
                FontFamily = new FontFamily("monospace"),
                FontSize = 12,
                Foreground = Look.Text,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 360,
                Margin = new Thickness(0, 6, 0, 0),
            };
            panel.Children.Add(command);
            var copy = LinkButton(S.Copy, () => Clipboard?.SetTextAsync(Hotkeys.ToggleCommand));
            copy.HorizontalAlignment = HorizontalAlignment.Center;
            copy.FontSize = 11;
            panel.Children.Add(copy);
            return panel;
        }

        var keys = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (_recordingHotkey)
        {
            var prompt = Look.Label(S.HotkeyRecord, 12, Look.Accent);
            prompt.TextWrapping = TextWrapping.Wrap;
            prompt.TextAlignment = TextAlignment.Center;
            prompt.MaxWidth = 320;
            keys.Children.Add(prompt);
        }
        else
        {
            foreach (var cap in Shortcut.Current.Caps) keys.Children.Add(KeyCap(cap));
            keys.Children.Add(Look.Label(" " + S.HotkeyHint, 13, Look.Secondary));
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
            var taken = Look.Label(S.HotkeyTaken, 11, Look.Error);
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
        Build();
    }

    private void OnRecordKey(object? sender, KeyEventArgs e)
    {
        if (!_recordingHotkey) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            FinishRecording(null);
            return;
        }
        if (Shortcut.From(e.Key, e.KeyModifiers) is { } shortcut) FinishRecording(shortcut);
    }

    /// Applies the new shortcut (null keeps the old one); an app already holding it brings the old one back.
    private void FinishRecording(Shortcut? key)
    {
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

    // MARK: rows

    public async void AddConnection(string? preset = null)
    {
        if (!IsVisible) App.Current.ShowWelcome();
        var dialog = new ConnectionDialog(preset);
        if (await dialog.ShowDialog<bool>(this) && dialog.Result != null) Settings.Shared.Add(dialog.Result);
    }

    private Control AuthRow()
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
        left.Children.Add(Look.Label(text));
        Control? action = _auth switch
        {
            ClaudeAuth.State.SignedOut => LinkButton(S.SignIn, ClaudeAuth.SignIn),
            ClaudeAuth.State.Missing => LinkButton(S.Download, () => Updates.Open("https://claude.com/claude-code")),
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

    private Control ConnectionRow(Connection c)
    {
        var texts = new StackPanel();
        texts.Children.Add(Look.Label(c.Name));
        texts.Children.Add(Look.Label(c.Model, 11, Look.Secondary));
        var remove = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Look.Secondary,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(6, 2, 6, 2),
        };
        ToolTip.SetTip(remove, S.DeleteConnection);
        remove.Click += (_, _) => Settings.Shared.Remove(c);
        var dock = new DockPanel { Margin = new Thickness(14, 8, 14, 8) };
        DockPanel.SetDock(remove, Dock.Right);
        dock.Children.Add(remove);
        dock.Children.Add(texts);
        return WithDivider(dock, false);
    }

    private static Control Row(string? label, Control control, bool last = false)
    {
        var dock = new DockPanel { Margin = new Thickness(14, 8, 14, 8) };
        if (label != null)
        {
            DockPanel.SetDock(control, Dock.Right);
            dock.Children.Add(control);
            dock.Children.Add(Look.Label(label));
        }
        else
        {
            dock.Children.Add(control);
        }
        return WithDivider(dock, last);
    }

    private static Control WithDivider(Control content, bool last)
    {
        var panel = new StackPanel();
        panel.Children.Add(content);
        if (!last) panel.Children.Add(new Border { Height = 1, Background = Look.Border, Margin = new Thickness(14, 0, 0, 0) });
        return panel;
    }

    private Control ModelButton()
    {
        var button = new Button
        {
            Content = Settings.Shared.ChoiceTitle + "  ▾",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Look.Text,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Click += (_, _) =>
        {
            var menu = ModelMenu.Build(() => AddConnection());
            menu.Placement = PlacementMode.Bottom;
            menu.Open(button);
        };
        return button;
    }

    private static Control LanguageBox()
    {
        var items = new List<string> { S.LanguageSystem };
        items.AddRange(Lang.Names);
        var box = new ComboBox { MinWidth = 140, ItemsSource = items };
        box.SelectedIndex = Array.IndexOf(Lang.Codes, Settings.Shared.Language) + 1;
        box.SelectionChanged += (_, _) =>
        {
            var language = box.SelectedIndex <= 0 ? "" : Lang.Codes[box.SelectedIndex - 1];
            if (language == Settings.Shared.Language) return;
            Settings.Shared.Language = language;
            Settings.Shared.Save();
        };
        return box;
    }

    private static Control Check(bool value, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
        return box;
    }

    public static Button LinkButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Look.Accent,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control KeyCap(string label) => new Border
    {
        Child = Look.Label(label, 13, null, FontWeight.Medium),
        Padding = new Thickness(8, 2, 8, 2),
        Margin = new Thickness(0, 0, 6, 0),
        CornerRadius = new CornerRadius(6),
        Background = Look.Card,
        BorderBrush = Look.Border,
        BorderThickness = new Thickness(1),
    };
}

/// The add-connection form, with Lumi's own models one press away under the Ollama preset.
public sealed class ConnectionDialog : Window
{
    private readonly ComboBox _preset = new();
    private readonly TextBox _name = new();
    private readonly TextBox _url = new();
    private readonly TextBox _model = new();
    private readonly TextBox _key = new() { PasswordChar = '•' };
    private readonly TextBlock _modelHint;
    private readonly TextBlock _keyHint;
    private readonly StackPanel _ollamaHelp = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _lumiPanel = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly Button _save;

    private LumiRecipe _recipe = Ollama.Recommended;
    private CancellationTokenSource? _install;
    private double _installed = -1;
    private bool _building;
    private bool _ollamaMissing;
    private double _ollamaDownload = -1;
    private bool _ollamaStarting;
    private string? _installError;
    private List<string> _models = new();

    public Connection? Result { get; private set; }

    public ConnectionDialog(string? preset = null)
    {
        Title = S.FormTitle;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Look.Background;

        _preset.ItemsSource = ServicePreset.All;
        _preset.HorizontalAlignment = HorizontalAlignment.Stretch;
        _modelHint = Look.Label("", 11, Look.Secondary);
        _keyHint = Look.Label("", 11, Look.Secondary);

        var stack = new StackPanel { Margin = new Thickness(22) };
        stack.Children.Add(Look.Label(S.FormTitle, 17, null, FontWeight.SemiBold));
        var sub = Look.Label(S.FormSubtitle, 12, Look.Secondary);
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
        cancel.Click += (_, _) => Close(false);
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

    private static Control Field(string label, Control control, TextBlock? hint = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(Look.Label(label, 12, Look.Secondary));
        control.Margin = new Thickness(0, 2, 0, 0);
        panel.Children.Add(control);
        if (hint != null) panel.Children.Add(hint);
        return panel;
    }

    private void ApplyPreset()
    {
        if (_preset.SelectedItem is not ServicePreset p) return;
        var name = _name.Text ?? "";
        var isPresetName = name.Length == 0 || Array.Exists(ServicePreset.All, x => x.Title == name);
        if (isPresetName) _name.Text = p.Title;
        _url.Text = p.BaseUrl;
        _modelHint.Text = p.ModelHint;
        _keyHint.Text = p.Key == "GigaChat" ? S.HintGigachatKey : p.NeedsKey ? S.KeyStored : S.KeyNotNeeded;
        _ollamaHelp.Children.Clear();
        if (p.Key == "ollama") CheckOllama();
        Validate();
    }

    private const string OllamaDownload = "https://ollama.com/download";

    private static TextBlock Note(string s, IBrush? color = null)
    {
        var label = Look.Label(s, 12, color ?? Look.Text);
        label.TextWrapping = TextWrapping.Wrap;
        label.TextTrimming = TextTrimming.None;
        label.Margin = new Thickness(0, 2, 0, 2);
        return label;
    }

    /// Under the Ollama preset: whether it's running, Lumi's own models, and the installed ones to pick from.
    private async void CheckOllama()
    {
        _ollamaHelp.Children.Clear();
        _ollamaHelp.Children.Add(Look.Label("Ollama…", 12, Look.Secondary));
        var models = await Ollama.ModelsAsync();
        if (_preset.SelectedItem is not ServicePreset { Key: "ollama" }) return;
        _ollamaHelp.Children.Clear();
        _ollamaMissing = models == null;
        _models = models ?? new();
        _ollamaHelp.Children.Add(models == null ? Note(S.OllamaMissing) : Note("✓ " + S.OllamaReady(models.Count.ToString()), Look.Accent));
        _ollamaHelp.Children.Add(_lumiPanel);
        RenderLumi();
        if (models == null)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            var download = new Button { Content = S.OllamaDownload, Padding = new Thickness(12, 3, 12, 3) };
            download.Click += (_, _) => Updates.Open(OllamaDownload);
            var again = new Button { Content = S.CheckAgain, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
            again.Click += (_, _) => CheckOllama();
            buttons.Children.Add(download);
            buttons.Children.Add(again);
            _ollamaHelp.Children.Add(buttons);
            return;
        }
        if (models.Count == 0) return;
        if ((_model.Text ?? "").Trim().Length == 0) _model.Text = models[0];
        // The exact installed names, so the model field can't hold a name Ollama doesn't have.
        _ollamaHelp.Children.Add(Note(S.OllamaPick, Look.Secondary));
        var list = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var m in models)
        {
            var pick = new Button { Content = m, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 6) };
            pick.Click += (_, _) => _model.Text = m;
            list.Children.Add(pick);
        }
        _ollamaHelp.Children.Add(list);
    }

    /// Lumi's own models: pick a size, then download and set it up in one press.
    private void RenderLumi()
    {
        _lumiPanel.Children.Clear();
        var busy = _install != null;
        _lumiPanel.Children.Add(Note(S.LumiOwn));
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        foreach (var r in LumiModels.All)
        {
            var option = new RadioButton { Content = r.Title, GroupName = "lumi", IsChecked = r == _recipe, IsEnabled = !busy, Margin = new Thickness(0, 0, 14, 0) };
            option.IsCheckedChanged += (_, _) =>
            {
                if (option.IsChecked != true || _recipe == r) return;
                _recipe = r;
                _installError = null;
                Dispatcher.UIThread.Post(RenderLumi);
            };
            sizes.Children.Add(option);
        }
        _lumiPanel.Children.Add(sizes);
        var fit = S.LumiFit(_recipe.Title, _recipe.MemoryGB.ToString(), Ollama.MemoryGB.ToString());
        if (_recipe.MemoryGB > Ollama.MemoryGB) fit += " " + S.LumiSlow;
        _lumiPanel.Children.Add(Note(fit, Look.Secondary));

        if (busy)
        {
            var part = _ollamaDownload >= 0 ? _ollamaDownload : Math.Max(0, _installed);
            var waiting = _building || _ollamaStarting;
            var label = _ollamaStarting ? S.OllamaStarting
                : _ollamaDownload >= 0 ? S.OllamaDownloading(((int)(part * 100)).ToString())
                : _building ? S.LumiBuilding(_recipe.Title)
                : S.LumiDownloading(_recipe.Title, ((int)(part * 100)).ToString());
            _lumiPanel.Children.Add(Note(label));
            _lumiPanel.Children.Add(new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Value = part, IsIndeterminate = waiting, Foreground = Look.Accent, Margin = new Thickness(0, 2, 0, 0) });
            return;
        }
        if (_ollamaMissing && _installError == null) _lumiPanel.Children.Add(Note(S.OllamaAuto(OllamaSetup.SizeGB), Look.Secondary));
        if (_installError != null) _lumiPanel.Children.Add(Note(S.LumiFailed(_installError), Look.Error));
        if (Ollama.IsInstalled(_recipe, _models))
        {
            var use = new Button { Content = S.LumiUse(_recipe.Title), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 4, 0, 0) };
            use.Click += (_, _) => _model.Text = _recipe.Name;
            _lumiPanel.Children.Add(use);
        }
        else
        {
            var size = _recipe.SizeGB.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
            var install = new Button { Content = S.LumiInstall(_recipe.Title, size), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.White, Background = Look.Accent };
            install.Click += (_, _) => InstallLumi();
            _lumiPanel.Children.Add(install);
        }
    }

    /// Installs Ollama if needed, then downloads the chosen model's base, builds the Lumi model on it and fills it in.
    private async void InstallLumi()
    {
        var recipe = _recipe;
        _install = new CancellationTokenSource();
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
                    part => Dispatcher.UIThread.Post(() =>
                    {
                        _ollamaDownload = part;
                        var percent = (int)(part * 100);
                        if (percent != shown) { shown = percent; RenderLumi(); }
                    }),
                    () => Dispatcher.UIThread.Post(() => { _ollamaDownload = -1; _ollamaStarting = true; RenderLumi(); }),
                    _install.Token);
                _ollamaStarting = false;
                _ollamaMissing = false;
                _models = await Ollama.ModelsAsync() ?? new();
                RenderLumi();
            }
            var last = -1;
            await Ollama.PullAsync(recipe.Base, part => Dispatcher.UIThread.Post(() =>
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
            if (IsVisible)
            {
                _installError = e is OperationCanceledException ? "timeout" : e.Message;
                RenderLumi();
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _install?.Cancel();
        base.OnClosed(e);
    }

    private void Validate()
    {
        _save.IsEnabled = (_name.Text ?? "").Trim().Length > 0
            && Uri.TryCreate((_url.Text ?? "").Trim(), UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http")
            && (_model.Text ?? "").Trim().Length > 0;
    }

    private void Save()
    {
        Result = new Connection { Name = (_name.Text ?? "").Trim(), BaseUrl = (_url.Text ?? "").Trim(), Model = (_model.Text ?? "").Trim() };
        Result.Key = (_key.Text ?? "").Trim();
        Close(true);
    }
}
