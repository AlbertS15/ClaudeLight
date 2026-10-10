using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lumi;

/// An app (its .desktop file) or a file.
public sealed record Hit(string Name, string Path, bool IsApp, string Exec = "", bool InTerminal = false)
{
    public string Subtitle =>
        IsApp ? S.Application : (System.IO.Path.GetDirectoryName(Path) ?? "")
            .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");

    /// Starts the app, or opens the file in its default app.
    public void Open()
    {
        if (!IsApp)
        {
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { Path }, UseShellExecute = false });
            return;
        }
        // gtk-launch reads the .desktop file itself, the way the app menu would; else run its Exec line.
        var id = System.IO.Path.GetFileName(Path);
        if (Tools.Find("gtk-launch") is { } gtk && Path.Contains("/applications/"))
        {
            Process.Start(new ProcessStartInfo(gtk) { ArgumentList = { id }, UseShellExecute = false });
            return;
        }
        var command = Regex.Replace(Exec, "%[fFuUdDnNickvm]", "").Trim();
        if (InTerminal)
        {
            Tools.OpenTerminal("sh", "-c " + Quote(command));
            return;
        }
        Process.Start(new ProcessStartInfo("sh") { ArgumentList = { "-c", command + " &" }, UseShellExecute = false });
    }

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// Shows the file in the file manager.
    public void Reveal()
    {
        var folder = IsApp ? System.IO.Path.GetDirectoryName(Path) : System.IO.Path.GetDirectoryName(Path);
        if (folder != null) Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { folder }, UseShellExecute = false });
    }
}

/// Apps from the desktop's .desktop files, and files by name through locate when it's installed.
public sealed class FileSearch
{
    private readonly List<Hit> _apps = LoadApps();
    private CancellationTokenSource? _cts;

    private static IEnumerable<string> AppFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : System.IO.Path.Combine(home, ".local", "share");
        var dataDirs = (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share").Split(':');
        yield return System.IO.Path.Combine(dataHome, "applications");
        foreach (var dir in dataDirs) yield return System.IO.Path.Combine(dir, "applications");
        yield return "/var/lib/flatpak/exports/share/applications";
        yield return System.IO.Path.Combine(home, ".local/share/flatpak/exports/share/applications");
        yield return "/var/lib/snapd/desktop/applications";
    }

    private static List<Hit> LoadApps()
    {
        var lang = Lang.Codes[Lang.Index];
        var apps = new Dictionary<string, Hit>();
        foreach (var folder in AppFolders().Distinct().Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*.desktop", SearchOption.AllDirectories).ToList(); }
            catch { continue; }
            foreach (var file in files)
            {
                var id = System.IO.Path.GetFileName(file);
                if (apps.ContainsKey(id)) continue; // the first folder wins, as in the app menu
                if (ReadEntry(file, lang) is { } hit) apps[id] = hit;
                else apps[id] = null!;
            }
        }
        return apps.Values.Where(h => h != null).OrderBy(h => h.Name).ToList();
    }

    /// The [Desktop Entry] of an app shown in menus, or null for hidden ones.
    private static Hit? ReadEntry(string file, string lang)
    {
        try
        {
            string? name = null, localized = null, exec = null;
            var terminal = false;
            var inEntry = false;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("["))
                {
                    if (inEntry) break;
                    inEntry = line == "[Desktop Entry]";
                    continue;
                }
                if (!inEntry || !line.Contains('=')) continue;
                var key = line[..line.IndexOf('=')].Trim();
                var value = line[(line.IndexOf('=') + 1)..].Trim();
                switch (key)
                {
                    case "Type" when value != "Application":
                    case "NoDisplay" when value == "true":
                    case "Hidden" when value == "true":
                        return null;
                    case "Name": name = value; break;
                    case "Exec": exec = value; break;
                    case "Terminal": terminal = value == "true"; break;
                    default:
                        if (key == $"Name[{lang}]") localized = value;
                        break;
                }
            }
            var title = localized ?? name;
            return title == null || exec == null ? null : new Hit(title, file, true, exec, terminal);
        }
        catch
        {
            return null;
        }
    }

    /// Calls back with apps at once, then again with files when locate answers.
    public void Search(string query, Action<List<Hit>> done)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var q = query.Trim();
        if (q.Length == 0)
        {
            done(new List<Hit>());
            return;
        }
        var apps = _apps
            .Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Take(6)
            .ToList();
        done(apps);
        if (q.Length < 3) return;
        Task.Run(() =>
        {
            var files = Locate(q, cts.Token);
            if (!cts.IsCancellationRequested && files.Count > 0) done(apps.Concat(files).ToList());
        });
    }

    private static readonly string[] Blocked = { "/.cache/", "/.local/share/Trash/", "/node_modules/", "/.git/", "/.npm/", "/.cargo/" };

    private static List<Hit> Locate(string query, CancellationToken cancel)
    {
        var tool = Tools.Find("plocate") ?? Tools.Find("locate");
        if (tool == null) return new List<Hit>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            var psi = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { "-i", "-b", "-l", "60", query }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var lines = p.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            p.WaitForExit(3000);
            if (cancel.IsCancellationRequested) return new List<Hit>();
            return lines
                .Where(l => l.StartsWith(home + "/") && !l[home.Length..].Contains("/.") && !Blocked.Any(l.Contains))
                .Take(8)
                .Select(l => new Hit(System.IO.Path.GetFileName(l), l, false))
                .ToList();
        }
        catch
        {
            return new List<Hit>();
        }
    }
}
