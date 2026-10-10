using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Lumi;

public static class Program
{
    /// One socket per user: a second launch passes its wish ("toggle" or "show") to the running copy and exits.
    private static string SocketPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run ? run : Path.GetTempPath(),
        $"lumi-{Environment.UserName}.sock");

    public static bool StartedForToggle { get; private set; }
    public static bool IsStartup { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartedForToggle = args.Contains("--toggle");
        IsStartup = args.Contains("--startup");
        if (Send(StartedForToggle ? "toggle" : "show")) return;
        Listen();
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    private static bool Send(string message)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            socket.Send(Encoding.UTF8.GetBytes(message));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Listen()
    {
        try
        {
            File.Delete(SocketPath); // left over from a copy that crashed
            var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            server.Bind(new UnixDomainSocketEndPoint(SocketPath));
            server.Listen(4);
            new Thread(() =>
            {
                var buffer = new byte[64];
                while (true)
                {
                    try
                    {
                        using var client = server.Accept();
                        var message = Encoding.UTF8.GetString(buffer, 0, client.Receive(buffer));
                        Dispatcher.UIThread.Post(() => (Application.Current as App)?.OnMessage(message));
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                }
            }) { IsBackground = true, Name = "Lumi socket" }.Start();
        }
        catch
        {
            // Without the socket a second launch just starts a second copy.
        }
    }
}

public sealed class App : Application
{
    private LauncherWindow? _launcher;
    private WelcomeWindow? _welcome;
    private TrayIcon? _tray;

    public new static App Current => (App)Application.Current!;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Default;
        Name = "Lumi";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _launcher = new LauncherWindow();
        _welcome = new WelcomeWindow(() => _launcher.ShowBar());
        SetHotkey(Shortcut.Current);
        _ = OllamaSetup.StartIfInstalledAsync();

        Updates.CheckDaily(version => Dispatcher.UIThread.Post(() =>
        {
            _welcome?.ShowUpdate(version);
            BuildTrayMenu();
        }));
        _tray = new TrayIcon { Icon = new WindowIcon(Mascot.Icon()), ToolTipText = "Lumi", IsVisible = true };
        _tray.Clicked += (_, _) => _launcher.Toggle();
        BuildTrayMenu();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        Settings.Shared.Changed += () => Dispatcher.UIThread.Post(BuildTrayMenu);

        if (Program.StartedForToggle) _launcher.ShowBar();
        else if (Settings.Shared.ShowWelcomeOnLaunch && !Program.IsStartup) ShowWelcome();
        base.OnFrameworkInitializationCompleted();
    }

    /// A message from a second launch: `lumi --toggle` from a desktop shortcut, or a plain start.
    public void OnMessage(string message)
    {
        if (message == "toggle") _launcher?.Toggle();
        else ShowWelcome();
    }

    /// Sets the bar's shortcut up; null lets go of it. False when another app already holds the new one.
    public bool SetHotkey(Shortcut? key) => Hotkeys.Apply(key, () => _launcher?.Toggle());

    public void ShowWelcome(bool addConnection = false)
    {
        if (_welcome == null) return;
        _welcome.Show();
        _welcome.WindowState = WindowState.Normal;
        _welcome.Activate();
        if (addConnection) _welcome.AddConnection();
    }

    private void BuildTrayMenu()
    {
        if (_tray == null) return;
        var menu = new NativeMenu();
        if (Updates.NewVersion is { } version)
        {
            menu.Add(Item("⬆ " + S.UpdateAvailable(version), Updates.OpenReleases));
            menu.Add(new NativeMenuItemSeparator());
        }
        menu.Add(Item(S.MenuOpenSearch(Shortcut.Current.Label), () => _launcher?.ShowBar()));
        menu.Add(Item(S.MenuWindow, () => ShowWelcome()));
        var models = new NativeMenuItem(S.Model) { Menu = ModelMenu.Native(() => ShowWelcome(addConnection: true)) };
        menu.Add(models);
        var login = Item(S.MenuLaunchAtLogin, () => Settings.LaunchesAtLogin = !Settings.LaunchesAtLogin);
        login.ToggleType = NativeMenuItemToggleType.CheckBox;
        login.IsChecked = Settings.LaunchesAtLogin;
        menu.Add(login);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(S.MenuQuit, Quit));
        _tray.Menu = menu;
    }

    public static NativeMenuItem Item(string header, Action onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => onClick();
        return item;
    }

    public void Quit()
    {
        SetHotkey(null);
        if (_tray != null) _tray.IsVisible = false;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}

/// The model list, for the bar's button (a context menu) and the tray (a native menu).
public static class ModelMenu
{
    private static (string Title, string Choice)[] Choices()
    {
        var list = ClaudeModel.All.Select(m => ($"{m.Title} — {m.Note}", "claude:" + m.Id)).ToList();
        list.Add(("-", ""));
        list.Add(($"{S.CodexTitle} — {S.NoteCodex}", "codex:"));
        list.Add(("-", ""));
        list.AddRange(GeminiModel.All.Select(m => ($"{m.Title} — {m.Note}", "gemini:" + m.Id)));
        if (Settings.Shared.Connections.Count > 0)
        {
            list.Add(("-", ""));
            list.AddRange(Settings.Shared.Connections.Select(c => ($"{c.Name} — {c.Model}", "custom:" + c.Id)));
        }
        return list.ToArray();
    }

    public static ContextMenu Build(Action addConnection)
    {
        var menu = new ContextMenu();
        var items = new System.Collections.Generic.List<Control>();
        foreach (var (title, choice) in Choices())
        {
            if (title == "-")
            {
                items.Add(new Separator());
                continue;
            }
            var item = new MenuItem { Header = (Settings.Shared.Choice == choice ? "✓  " : "     ") + title };
            item.Click += (_, _) => Settings.Shared.Select(choice);
            items.Add(item);
        }
        items.Add(new Separator());
        var add = new MenuItem { Header = "     " + S.ConnectModel };
        add.Click += (_, _) => addConnection();
        items.Add(add);
        menu.ItemsSource = items;
        return menu;
    }

    public static NativeMenu Native(Action addConnection)
    {
        var menu = new NativeMenu();
        foreach (var (title, choice) in Choices())
        {
            if (title == "-")
            {
                menu.Add(new NativeMenuItemSeparator());
                continue;
            }
            var item = App.Item(title, () => Settings.Shared.Select(choice));
            item.ToggleType = NativeMenuItemToggleType.Radio;
            item.IsChecked = Settings.Shared.Choice == choice;
            menu.Add(item);
        }
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(App.Item(S.ConnectModel, addConnection));
        return menu;
    }
}

/// A daily look at the latest GitHub release.
public static class Updates
{
    private const string LatestRelease = "https://api.github.com/repos/AlbertS15/Lumi/releases/latest";
    private const string ReleasesPage = "https://github.com/AlbertS15/Lumi/releases/latest";

    public static string? NewVersion { get; private set; }

    public static void CheckDaily(Action<string> onFound)
    {
        var current = typeof(Updates).Assembly.GetName().Version;
        // Builds without a release tag (0.0.x from CI, 1.0.0 from a plain dotnet build) have nothing to compare.
        if (current == null || current.Major == 0 || current == new Version(1, 0, 0, 0)) return;
        _timer = new Timer(async _ =>
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Lumi/" + current.ToString(3));
                using var json = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(LatestRelease));
                var tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
                if (Version.TryParse(tag.TrimStart('v'), out var latest) && latest > new Version(current.ToString(3)) && NewVersion != latest.ToString())
                {
                    NewVersion = latest.ToString();
                    onFound(NewVersion);
                }
            }
            catch
            {
                // Offline or rate-limited: try again tomorrow.
            }
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromDays(1));
    }

    private static Timer? _timer; // held so the daily check isn't collected

    public static void OpenReleases() => Open(ReleasesPage);

    public static void Open(string url) =>
        Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false });
}
