using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Input;

namespace Lumi;

/// The bar's shortcut on Linux: an X keysym and X modifier mask, where key 0 is the standard Ctrl+Space. Alt + the key
/// left of 1, the shortcut on Mac and Windows, already switches between an app's windows in GNOME and KDE; Ctrl+Space is
/// what Linux launchers such as Ulauncher and Albert use.
public sealed record Shortcut(uint Modifiers, uint Key)
{
    public const uint Shift = 1, Control = 4, Alt = 8, Super = 64;
    public const uint Grave = 0x60;

    public static Shortcut Standard { get; } = new(0, 0);
    public static Shortcut Current => new(Settings.Shared.HotkeyModifiers, Settings.Shared.HotkeyKey);
    public bool IsStandard => Key == 0;
    public const uint Space = 0x20;
    public uint Keysym => IsStandard ? Space : Key;
    public uint Mask => IsStandard ? Control : Modifiers;

    /// The shortcut a key press makes, or null when it can't be one: a plain letter would fire while typing,
    /// so it needs Ctrl, Alt or Super, or has to be a function key.
    public static Shortcut? From(Key key, KeyModifiers modifiers)
    {
        uint? sym = key switch
        {
            >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z => 0x61u + (uint)(key - Avalonia.Input.Key.A),
            >= Avalonia.Input.Key.D0 and <= Avalonia.Input.Key.D9 => 0x30u + (uint)(key - Avalonia.Input.Key.D0),
            >= Avalonia.Input.Key.F1 and <= Avalonia.Input.Key.F12 => 0xFFBEu + (uint)(key - Avalonia.Input.Key.F1),
            Avalonia.Input.Key.Space => 0x20u,
            Avalonia.Input.Key.OemTilde => Grave,
            _ => null,
        };
        if (sym == null) return null;
        uint mask = 0;
        if (modifiers.HasFlag(KeyModifiers.Shift)) mask |= Shift;
        if (modifiers.HasFlag(KeyModifiers.Control)) mask |= Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) mask |= Alt;
        if (modifiers.HasFlag(KeyModifiers.Meta)) mask |= Super;
        var isFunctionKey = sym >= 0xFFBE;
        if ((mask & (Control | Alt | Super)) == 0 && !isFunctionKey) return null;
        return new Shortcut(mask, sym.Value);
    }

    /// Labels for key caps, modifiers first.
    public string[] Caps
    {
        get
        {
            if (IsStandard) return new[] { "Ctrl", S.KeySpace };
            var caps = new System.Collections.Generic.List<string>();
            if ((Modifiers & Control) != 0) caps.Add("Ctrl");
            if ((Modifiers & Alt) != 0) caps.Add("Alt");
            if ((Modifiers & Shift) != 0) caps.Add("Shift");
            if ((Modifiers & Super) != 0) caps.Add("Super");
            caps.Add(Key switch
            {
                0x20 => S.KeySpace,
                Grave => Theme.HotkeyKey,
                >= 0xFFBE => "F" + (Key - 0xFFBE + 1),
                _ => ((char)Key).ToString().ToUpperInvariant(),
            });
            return caps.ToArray();
        }
    }

    public string Label => string.Join("+", Caps);

    /// The shortcut in GNOME's notation: <Control><Alt>k.
    public string Gnome
    {
        get
        {
            var s = "";
            if ((Mask & Control) != 0) s += "<Control>";
            if ((Mask & Alt) != 0) s += "<Alt>";
            if ((Mask & Shift) != 0) s += "<Shift>";
            if ((Mask & Super) != 0) s += "<Super>";
            return s + (Keysym switch
            {
                0x20 => "space",
                Grave => "grave",
                >= 0xFFBE => "F" + (Keysym - 0xFFBE + 1),
                _ => ((char)Keysym).ToString(),
            });
        }
    }
}

/// How the shortcut reaches Lumi on this desktop.
public enum HotkeyMode
{
    /// X11: Lumi grabs the keys itself.
    X11,
    /// GNOME on Wayland: a custom shortcut in GNOME's settings runs `lumi --toggle`.
    Gnome,
    /// Elsewhere on Wayland: the person adds the shortcut in the system settings.
    Manual,
}

public static class Hotkeys
{
    public static HotkeyMode Mode { get; } = Detect();

    private static HotkeyMode Detect()
    {
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "";
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        if (session != "wayland" && Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 }) return HotkeyMode.X11;
        if (desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase) && Tools.Find("gsettings") != null) return HotkeyMode.Gnome;
        return HotkeyMode.Manual;
    }

    /// The command that opens or hides the bar, for desktop shortcuts.
    public static string ToggleCommand => $"\"{Settings.LaunchPath}\" --toggle";

    private static X11Grab? _grab;

    /// Sets the shortcut up; null lets go of it. False when another app already holds it.
    public static bool Apply(Shortcut? key, Action onPress)
    {
        _grab?.Dispose();
        _grab = null;
        switch (Mode)
        {
            case HotkeyMode.X11:
                if (key == null) return true;
                var grab = new X11Grab(key.Keysym, key.Mask, onPress);
                if (!grab.IsGrabbed)
                {
                    grab.Dispose();
                    return false;
                }
                _grab = grab;
                return true;
            case HotkeyMode.Gnome:
                SetGnomeShortcut(key);
                return true;
            default:
                return true;
        }
    }

    private const string GnomeList = "org.gnome.settings-daemon.plugins.media-keys";
    private const string GnomePath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/lumi/";

    /// Adds (or with null, disables) Lumi's entry among GNOME's custom shortcuts.
    private static void SetGnomeShortcut(Shortcut? key)
    {
        try
        {
            var list = Run("get", GnomeList, "custom-keybindings").Trim();
            if (!list.Contains(GnomePath))
            {
                var inner = list.StartsWith("@as") ? "" : list.Trim('[', ']').Trim();
                Run("set", GnomeList, "custom-keybindings", "[" + (inner.Length > 0 ? inner + ", " : "") + $"'{GnomePath}']");
            }
            var schema = $"{GnomeList}.custom-keybinding:{GnomePath}";
            Run("set", schema, "name", "Lumi");
            Run("set", schema, "command", ToggleCommand);
            Run("set", schema, "binding", key?.Gnome ?? "");
        }
        catch
        {
            // Without gsettings the welcome window still explains how to add the shortcut by hand.
        }
    }

    private static string Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Tools.Find("gsettings") ?? "gsettings") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(3000);
        return output;
    }
}

/// A system-wide key grab on X11, served by its own display connection on a background thread.
public sealed class X11Grab : IDisposable
{
    private const string Lib = "libX11.so.6";
    private const uint LockMask = 2, NumLockMask = 16;
    private const int KeyPress = 2, GrabModeAsync = 1;

    [DllImport(Lib)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(Lib)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(Lib)] private static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);
    [DllImport(Lib)] private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window, bool ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(Lib)] private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window);
    [DllImport(Lib)] private static extern int XPending(IntPtr display);
    [DllImport(Lib)] private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);
    [DllImport(Lib)] private static extern int XSync(IntPtr display, bool discard);
    [DllImport(Lib)] private static extern IntPtr XSetErrorHandler(ErrorHandler handler);
    [DllImport(Lib, EntryPoint = "XSetErrorHandler")] private static extern IntPtr RestoreErrorHandler(IntPtr handler);

    private delegate int ErrorHandler(IntPtr display, IntPtr errorEvent);

    // Held for the life of the app: X calls it from native code.
    private static readonly ErrorHandler OnError = (_, _) =>
    {
        _failed = true;
        return 0;
    };
    private static volatile bool _failed;

    private readonly IntPtr _display;
    private readonly IntPtr _root;
    private readonly int _keycode;
    private readonly uint _mask;
    private readonly Thread? _thread;
    private volatile bool _stop;

    public bool IsGrabbed { get; }

    public X11Grab(uint keysym, uint mask, Action onPress)
    {
        try
        {
            _display = XOpenDisplay(IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            return;
        }
        if (_display == IntPtr.Zero) return;
        _root = XDefaultRootWindow(_display);
        _keycode = XKeysymToKeycode(_display, (IntPtr)keysym);
        // The key left of 1 has keycode 49 on every layout; a Russian layout first in the list has no grave keysym.
        if (_keycode == 0 && keysym == Shortcut.Grave) _keycode = 49;
        if (_keycode == 0 && keysym == Shortcut.Space) _keycode = 65;
        _mask = mask;
        if (_keycode == 0) return;
        // Another app holding the keys raises BadAccess, which by default would end the app.
        var previous = XSetErrorHandler(OnError);
        _failed = false;
        foreach (var extra in new[] { 0u, LockMask, NumLockMask, LockMask | NumLockMask })
            XGrabKey(_display, _keycode, mask | extra, _root, false, GrabModeAsync, GrabModeAsync);
        XSync(_display, false);
        // Give Avalonia's own handler back.
        RestoreErrorHandler(previous);
        if (_failed)
        {
            Ungrab();
            return;
        }
        IsGrabbed = true;
        _thread = new Thread(() =>
        {
            var ev = Marshal.AllocHGlobal(192);
            try
            {
                while (!_stop)
                {
                    while (XPending(_display) > 0)
                    {
                        XNextEvent(_display, ev);
                        if (Marshal.ReadInt32(ev) == KeyPress) Avalonia.Threading.Dispatcher.UIThread.Post(onPress);
                    }
                    Thread.Sleep(30);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ev);
            }
        }) { IsBackground = true, Name = "Lumi hotkey" };
        _thread.Start();
    }

    private void Ungrab()
    {
        foreach (var extra in new[] { 0u, LockMask, NumLockMask, LockMask | NumLockMask })
            XUngrabKey(_display, _keycode, _mask | extra, _root);
        XSync(_display, false);
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(500);
        if (_display == IntPtr.Zero) return;
        if (IsGrabbed) Ungrab();
        XCloseDisplay(_display);
    }
}
