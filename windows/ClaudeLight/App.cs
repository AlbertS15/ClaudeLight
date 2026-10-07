using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace ClaudeLight;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One copy at a time; a second launch asks the first to show its window and exits.
        using var mutex = new Mutex(true, "ClaudeLight.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting("ClaudeLight.ShowWelcome");
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The first copy is still starting; it will show its own window.
            }
            return;
        }
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
    private HotKey? _hotKey;

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

        // Alt + the key left of 1 (` on English layouts, Ё on Russian): VK_OEM_3.
        _hotKey = new HotKey(HotKey.ModAlt, 0xC0, () => _launcher.Toggle());

        _tray = new System.Windows.Forms.NotifyIcon { Icon = Mascot.TrayIcon(), Text = "ClaudeLight", Visible = true };
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
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "ClaudeLight.ShowWelcome");
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
        _source = new HwndSource(new HwndSourceParameters("ClaudeLightHotKey") { Width = 0, Height = 0, WindowStyle = 0 });
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
