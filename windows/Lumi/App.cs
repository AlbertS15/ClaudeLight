using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace Lumi;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One copy at a time; a second launch asks the first to show its window and exits.
        using var mutex = new Mutex(true, "Lumi.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting("Lumi.ShowWelcome");
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The first copy is still starting; it will show its own window.
            }
            return;
        }
        // The same app under its old name would grab the same hotkey.
        foreach (var old in System.Diagnostics.Process.GetProcessesByName("ClaudeLight"))
        {
            try { old.Kill(); } catch { }
        }
        try { Settings.MigrateLaunchAtLogin(); } catch { }

        var app = new App(isStartup: args.Contains("--startup"));
        app.Run();
    }
}

public sealed class App : Application
{
    private readonly bool _isStartup;
    private LauncherWindow? _launcher;
    private WelcomeWindow? _welcome;
    private System.Windows.Forms.NotifyIcon? _tray;
    private IDisposable? _hotKey;

    public new static App Current => (App)Application.Current;

    public App(bool isStartup)
    {
        _isStartup = isStartup;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _launcher = new LauncherWindow();
        _welcome = new WelcomeWindow(() => _launcher.ShowBar());

        // Alt + the key left of 1, on any keyboard layout; RegisterHotKey on VK_OEM_3 if the hook can't be set.
        var leftOfOne = new LeftOfOneHotKey(() => _launcher.Toggle());
        _hotKey = leftOfOne.IsInstalled ? leftOfOne : new HotKey(HotKey.ModAlt, 0xC0, () => _launcher.Toggle());
        if (!leftOfOne.IsInstalled) leftOfOne.Dispose();

        Updates.CheckDaily(version => Dispatcher.Invoke(() =>
        {
            _welcome?.ShowUpdate(version);
            _tray?.ShowBalloonTip(8000, "Lumi", S.UpdateAvailable(version), System.Windows.Forms.ToolTipIcon.Info);
        }));

        _tray = new System.Windows.Forms.NotifyIcon { Icon = Mascot.TrayIcon(), Text = "Lumi", Visible = true };
        _tray.MouseClick += (_, a) =>
        {
            if (a.Button == System.Windows.Forms.MouseButtons.Left) _launcher.Toggle();
        };
        _tray.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => BuildTrayMenu();
        BuildTrayMenu();

        WatchSecondLaunch();
        if (Settings.Shared.ShowWelcomeOnLaunch && !_isStartup) ShowWelcome();
    }

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
        var menu = _tray!.ContextMenuStrip!;
        menu.Items.Clear();
        if (Updates.NewVersion is { } version)
        {
            menu.Items.Add("⬆ " + S.UpdateAvailable(version), null, (_, _) => Updates.OpenReleases());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        }
        menu.Items.Add(S.MenuOpenSearch("Alt+" + Theme.HotkeyKey), null, (_, _) => _launcher!.ShowBar());
        menu.Items.Add(S.MenuWindow, null, (_, _) => ShowWelcome());

        var models = new System.Windows.Forms.ToolStripMenuItem(S.Model);
        foreach (var m in ClaudeModel.All)
        {
            var choice = "claude:" + m.Id;
            var item = new System.Windows.Forms.ToolStripMenuItem($"{m.Title} — {m.Note}") { Checked = Settings.Shared.Choice == choice };
            item.Click += (_, _) => Settings.Shared.Select(choice);
            models.DropDownItems.Add(item);
        }
        models.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        var codex = new System.Windows.Forms.ToolStripMenuItem($"{S.CodexTitle} — {S.NoteCodex}") { Checked = Settings.Shared.IsCodex };
        codex.Click += (_, _) => Settings.Shared.Select("codex:");
        models.DropDownItems.Add(codex);
        models.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        foreach (var m in GeminiModel.All)
        {
            var choice = "gemini:" + m.Id;
            var item = new System.Windows.Forms.ToolStripMenuItem($"{m.Title} — {m.Note}") { Checked = Settings.Shared.Choice == choice };
            item.Click += (_, _) => Settings.Shared.Select(choice);
            models.DropDownItems.Add(item);
        }
        if (Settings.Shared.Connections.Count > 0)
        {
            models.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            foreach (var c in Settings.Shared.Connections)
            {
                var choice = "custom:" + c.Id;
                var item = new System.Windows.Forms.ToolStripMenuItem($"{c.Name} — {c.Model}") { Checked = Settings.Shared.Choice == choice };
                item.Click += (_, _) => Settings.Shared.Select(choice);
                models.DropDownItems.Add(item);
            }
        }
        models.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        models.DropDownItems.Add(S.ConnectModel, null, (_, _) => ShowWelcome(addConnection: true));
        menu.Items.Add(models);

        var login = new System.Windows.Forms.ToolStripMenuItem(S.MenuLaunchAtLogin) { Checked = Settings.LaunchesAtLogin };
        login.Click += (_, _) => Settings.LaunchesAtLogin = !Settings.LaunchesAtLogin;
        menu.Items.Add(login);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(S.MenuQuit, null, (_, _) => Quit());
    }

    /// A second copy of the app signals this event; the first shows its welcome window.
    private void WatchSecondLaunch()
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Lumi.ShowWelcome");
        var thread = new Thread(() =>
        {
            while (true)
            {
                signal.WaitOne();
                Dispatcher.InvokeAsync(() => ShowWelcome());
            }
        }) { IsBackground = true };
        thread.Start();
    }

    public void Quit()
    {
        _hotKey?.Dispose();
        if (_tray != null) _tray.Visible = false;
        Shutdown();
    }
}

/// Alt + the key left of 1, recognised by its position (scan code 0x29) rather than its character, so it works on
/// every keyboard layout: ` on English, Ё on Russian, ^ on German, ² on French. RegisterHotKey only takes a
/// virtual key, which differs between layouts, so a low-level keyboard hook watches for the key instead.
public sealed class LeftOfOneHotKey : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmSysKeyDown = 0x0104;
    private const uint ScanLeftOfOne = 0x29;
    private const uint FlagExtended = 0x01, FlagAltDown = 0x20;
    private const int VkMenu = 0x12, VkControl = 0x11, VkLWin = 0x5B, VkRWin = 0x5C;
    // An unassigned key, pressed after the chord so the foreground app doesn't open its menu when Alt is released.
    private const byte VkMask = 0xE8;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInfo
    {
        public uint VkCode, ScanCode, Flags, Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    private readonly HookProc _proc; // held so the delegate outlives the native hook that calls it
    private readonly IntPtr _hook;
    private readonly Action _onPress;
    private bool _isHeld;

    public LeftOfOneHotKey(Action onPress)
    {
        _onPress = onPress;
        _proc = Hook;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KeyInfo>(lParam);
            if (key.ScanCode == ScanLeftOfOne && (key.Flags & FlagExtended) == 0)
            {
                var msg = wParam.ToInt32();
                var isDown = msg == WmKeyDown || msg == WmSysKeyDown;
                // Ctrl+Alt is AltGr on European layouts, which types characters; leave it alone, and Win too.
                var isChord = ((key.Flags & FlagAltDown) != 0 || IsDown(VkMenu)) && !IsDown(VkControl) && !IsDown(VkLWin) && !IsDown(VkRWin);
                if (isDown && isChord)
                {
                    if (!_isHeld)
                    {
                        _isHeld = true;
                        keybd_event(VkMask, 0, 0, UIntPtr.Zero);
                        keybd_event(VkMask, 0, 0x0002, UIntPtr.Zero);
                        Application.Current.Dispatcher.BeginInvoke(_onPress);
                    }
                    return (IntPtr)1; // swallowed, along with auto-repeats
                }
                if (!isDown && _isHeld)
                {
                    _isHeld = false;
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
    }
}

/// Asks GitHub once a day whether a newer release is out; the tray menu and the welcome window then offer it.
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
        _timer = new System.Threading.Timer(async _ =>
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

    private static System.Threading.Timer? _timer; // held so the daily check isn't collected

    public static void OpenReleases() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ReleasesPage) { UseShellExecute = true });
}

/// A system-wide hotkey through RegisterHotKey, delivered to a hidden message window.
public sealed class HotKey : IDisposable
{
    public const uint ModAlt = 0x0001;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotKey = 0x0312;
    private const int Id = 0xC1A0;

    private readonly HwndSource _source;
    private readonly Action _onPress;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotKey(uint modifiers, uint vk, Action onPress)
    {
        _onPress = onPress;
        _source = new HwndSource(new HwndSourceParameters("LumiHotKey") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(Hook);
        IsRegistered = RegisterHotKey(_source.Handle, Id, modifiers | ModNoRepeat, vk);
    }

    public bool IsRegistered { get; }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == Id)
        {
            _onPress();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_source.Handle, Id);
        _source.Dispose();
    }
}
