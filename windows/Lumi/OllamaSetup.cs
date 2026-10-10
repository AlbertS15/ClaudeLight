using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Lumi;

/// Installs Ollama itself, so the offline route needs no trip to a website.
public static class OllamaSetup
{
    private const string Installer = "https://ollama.com/download/OllamaSetup.exe";

    /// The installer carries the GPU libraries, hence the size.
    public const string SizeGB = "1.6";

    /// Ollama installs per user, without admin rights, into this folder.
    private static string App => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Ollama", "ollama app.exe");

    /// Downloads and silently runs Ollama's installer when it isn't on this PC, then starts Ollama and waits until it answers.
    public static async Task InstallAsync(Action<double> downloading, Action starting, CancellationToken cancel)
    {
        if (!File.Exists(App))
        {
            var setup = Path.Combine(Path.GetTempPath(), $"OllamaSetup-{Guid.NewGuid():N}.exe");
            try
            {
                await DownloadAsync(setup, downloading, cancel);
                starting();
                if (!IsSignedByOllama(setup)) throw new Exception("signature");
                using var install = Process.Start(new ProcessStartInfo(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-")
                {
                    UseShellExecute = false,
                }) ?? throw new Exception("Ollama");
                await install.WaitForExitAsync(cancel);
                if (install.ExitCode != 0) throw new Exception($"Ollama ({install.ExitCode})");
            }
            finally
            {
                try { File.Delete(setup); } catch { }
            }
        }
        starting();
        // The installer may already have started it; give it a moment before starting another copy.
        for (var i = 0; i < 90; i++)
        {
            if (await Ollama.ModelsAsync() != null) return;
            if (i == 3 && File.Exists(App)) Process.Start(new ProcessStartInfo(App) { UseShellExecute = true });
            await Task.Delay(1000, cancel);
        }
        throw new Exception(S.OllamaNotStarted);
    }

    private static async Task DownloadAsync(string path, Action<double> progress, CancellationToken cancel)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var response = await http.GetAsync(Installer, HttpCompletionOption.ResponseHeadersRead, cancel);
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
        if (total > 0 && done != total) throw new Exception("ollama.com");
    }

    /// Whether Windows trusts the file's Authenticode signature and the signer is Ollama's company.
    private static bool IsSignedByOllama(string path)
    {
        if (!Trusted(path)) return false;
        try
        {
            var subject = X509Certificate.CreateFromSignedFile(path).Subject;
            return subject.Contains("Ollama", StringComparison.OrdinalIgnoreCase)
                || subject.Contains("Infra Technologies", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool Trusted(string path)
    {
        var fileInfo = new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = path };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, false);
            var data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UIChoice = 2, // no UI
                UnionChoice = 1, // a file
                File = filePtr,
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public string FilePath;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SIPClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr URLReference;
        public uint ProvFlags;
        public uint UIContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);
}
