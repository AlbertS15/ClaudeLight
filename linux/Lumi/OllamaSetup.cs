using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Lumi;

/// Installs Ollama into the home folder, so the offline route needs neither a website nor sudo.
public static class OllamaSetup
{
    /// The download for x86-64; GPU libraries included, hence the size.
    public const string SizeGB = "1.4";

    private static string Arch => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
    private static string Archive => $"ollama-linux-{Arch}.tar.zst";
    private const string Releases = "https://github.com/ollama/ollama/releases/latest/download/";

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lumi", "ollama");
    private static string Binary => Path.Combine(Folder, "bin", "ollama");

    /// Ollama installed by Lumi, or one already on the system.
    private static string? Installed => File.Exists(Binary) ? Binary : Tools.Find("ollama");

    /// Starts the Ollama that Lumi installed, if it isn't running yet: after a restart nothing else would.
    public static async Task StartIfInstalledAsync()
    {
        if (!File.Exists(Binary) || await Ollama.ModelsAsync() != null) return;
        Serve(Binary);
    }

    /// Downloads and unpacks Ollama when it isn't on this computer, then starts it and waits until it answers.
    public static async Task InstallAsync(Action<double> downloading, Action starting, CancellationToken cancel)
    {
        var binary = Installed;
        if (binary == null)
        {
            var archive = Path.Combine(Path.GetTempPath(), $"lumi-{Guid.NewGuid():N}-{Archive}");
            try
            {
                using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                var sums = await http.GetStringAsync(Releases + "sha256sum.txt", cancel);
                var expected = sums.Split('\n').Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .FirstOrDefault(p => p.Length == 2 && p[1].TrimStart('*', '.', '/') == Archive)?[0]
                    ?? throw new Exception("sha256sum.txt");
                await DownloadAsync(http, Releases + Archive, archive, downloading, cancel);
                starting();
                await using (var check = File.OpenRead(archive))
                {
                    var actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancel));
                    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new Exception("checksum");
                }
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(Folder);
                    using var file = File.OpenRead(archive);
                    using var zstd = new ZstdSharp.DecompressionStream(file);
                    TarFile.ExtractToDirectory(zstd, Folder, overwriteFiles: true);
                }, cancel);
                binary = Binary;
            }
            finally
            {
                try { File.Delete(archive); } catch { }
            }
        }
        starting();
        if (await Ollama.ModelsAsync() == null) Serve(binary);
        for (var i = 0; i < 90; i++)
        {
            if (await Ollama.ModelsAsync() != null) return;
            await Task.Delay(1000, cancel);
        }
        throw new Exception(S.OllamaNotStarted);
    }

    /// Runs `ollama serve` detached, so it keeps running when Lumi quits.
    private static void Serve(string binary)
    {
        var log = Path.Combine(Path.GetDirectoryName(Folder)!, "ollama.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        var psi = new ProcessStartInfo("sh") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("nohup \"$0\" serve >>\"$1\" 2>&1 &");
        psi.ArgumentList.Add(binary);
        psi.ArgumentList.Add(log);
        Process.Start(psi)?.WaitForExit(2000);
    }

    private static async Task DownloadAsync(HttpClient http, string url, string path, Action<double> progress, CancellationToken cancel)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        await using var file = File.Create(path);
        var buffer = new byte[1 << 20];
        long done = 0;
        var shown = -1;
        while (true)
        {
            // A minute without data means the download stalled.
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            stall.CancelAfter(TimeSpan.FromSeconds(60));
            var read = await source.ReadAsync(buffer, stall.Token);
            if (read == 0) break;
            await file.WriteAsync(buffer.AsMemory(0, read), cancel);
            done += read;
            var percent = total > 0 ? (int)(done * 100 / total) : 0;
            if (percent != shown) { shown = percent; progress(done / (double)Math.Max(total, 1)); }
        }
        if (total > 0 && done != total) throw new Exception("github.com");
    }
}
