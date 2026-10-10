using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lumi;

public sealed record Hit(string Name, string Path, bool IsApp)
{
    public string Subtitle =>
        IsApp ? S.Application : (System.IO.Path.GetDirectoryName(Path) ?? "")
            .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
}

/// Start-menu shortcuts for apps, and the Windows Search index for files (what Explorer's search box reads).
public sealed class FileSearch
{
    private readonly List<Hit> _apps;
    private CancellationTokenSource? _cts;

    public FileSearch()
    {
        _apps = LoadApps();
    }

    private static List<Hit> LoadApps()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        var apps = new Dictionary<string, Hit>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    apps.TryAdd(name, new Hit(name, file, true));
                }
            }
            catch
            {
                // An unreadable folder is skipped.
            }
        }
        return apps.Values.ToList();
    }

    /// Debounced search; `onResults` runs on a background thread with the newest results only.
    public void Search(string text, Action<List<Hit>> onResults)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var term = text.Trim();
        if (term.Length == 0)
        {
            onResults(new List<Hit>());
            return;
        }

        Task.Run(async () =>
        {
            await Task.Delay(120, cts.Token);
            var apps = _apps
                .Where(a => a.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(a => a.Name.Length)
                .Take(5)
                .ToList();
            if (!cts.IsCancellationRequested) onResults(apps);

            var files = QueryIndex(term, cts.Token);
            if (cts.IsCancellationRequested) return;
            var named = files.Where(f => f.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
            var rest = files.Where(f => !f.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
            onResults(apps.Concat(named.Concat(rest).Take(12 - apps.Count)).ToList());
        }, cts.Token);
    }

    private static List<Hit> QueryIndex(string term, CancellationToken token)
    {
        var hits = new List<Hit>();
        var like = term.Replace("'", "''").Replace("%", "[%]").Replace("_", "[_]");
        var contains = new string(term.Where(ch => ch != '"' && ch != '\'').ToArray());
        var sql =
            "SELECT TOP 60 System.ItemPathDisplay, System.ItemNameDisplay FROM SystemIndex " +
            $"WHERE SCOPE='file:' AND (System.ItemNameDisplay LIKE '%{like}%'" +
            (contains.Length > 0 ? $" OR CONTAINS(*, '\"{contains}*\"')" : "") +
            ") ORDER BY System.DateAccessed DESC";
        try
        {
            using var connection = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            connection.Open();
            using var command = new OleDbCommand(sql, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read() && !token.IsCancellationRequested)
            {
                var path = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (path == null || IsNoise(path)) continue;
                var name = reader.IsDBNull(1) ? System.IO.Path.GetFileName(path) : reader.GetString(1);
                hits.Add(new Hit(name, path, false));
            }
        }
        catch
        {
            // Windows Search off or unavailable: apps still show.
        }
        return hits;
    }

    private static bool IsNoise(string path)
    {
        var blocked = new[] { @"\AppData\", @"\Windows\", @"\ProgramData\", @"\$Recycle.Bin\", @"\node_modules\", @"\.git\" };
        return blocked.Any(b => path.Contains(b, StringComparison.OrdinalIgnoreCase));
    }
}
