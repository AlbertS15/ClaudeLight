using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ClaudeLight;

/// Runs the local `claude` CLI in print mode and streams the reply text.
public sealed class ClaudeRunner
{
    public const string SystemPrompt =
        "You were called from a Spotlight-style quick bar on Windows. Answer briefly and to the point, " +
        "in the language of the question. Use Markdown only for lists, bold and code.";

    private Process? _process;
    private string? _sessionId;

    /// claude.exe from the native installer, else whatever `claude` PATH finds (npm installs a .cmd).
    public static string? Binary { get; } = Find();

    private static string? Find()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string> { Path.Combine(home, ".local", "bin", "claude.exe") };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            candidates.Add(Path.Combine(dir.Trim(), "claude.exe"));
            candidates.Add(Path.Combine(dir.Trim(), "claude.cmd"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    public void Reset()
    {
        Cancel();
        _sessionId = null;
    }

    public void Cancel()
    {
        try { _process?.Kill(true); } catch { }
        _process = null;
    }

    /// The callbacks run on the UI thread through `post`.
    public void Ask(string question, Action<Action> post, Action<string> onText, Action<string?> onDone)
    {
        Cancel();
        if (Binary == null)
        {
            onDone(S.ErrNoClaude);
            return;
        }

        var psi = new ProcessStartInfo(Binary)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        // The question goes in on stdin, so no shell (claude.cmd) ever parses it.
        foreach (var a in new[]
                 {
                     "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
                     "--append-system-prompt", SystemPrompt,
                     "--tools", "WebSearch,WebFetch", "--allowedTools", "WebSearch,WebFetch",
                 })
            psi.ArgumentList.Add(a);
        var model = Settings.Shared.ClaudeModel;
        if (model.Id != "") { psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(model.Id); }
        if (_sessionId != null) { psi.ArgumentList.Add("--resume"); psi.ArgumentList.Add(_sessionId); }

        Process p;
        try
        {
            p = Process.Start(psi)!;
        }
        catch (Exception e)
        {
            onDone(S.ErrStart(e.Message));
            return;
        }
        _process = p;
        p.StandardInput.Write(question);
        p.StandardInput.Close();
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();

        Task.Run(() =>
        {
            var streamed = false;
            string? error = null;
            string? line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                JsonNode? obj;
                try { obj = JsonNode.Parse(line); } catch { continue; }
                var type = (string?)obj?["type"];
                if (type == "system" && obj?["session_id"] is JsonNode sid)
                {
                    var id = (string?)sid;
                    post(() => _sessionId = id);
                }
                else if (type == "stream_event"
                         && (string?)obj?["event"]?["delta"]?["type"] == "text_delta"
                         && (string?)obj?["event"]?["delta"]?["text"] is string text)
                {
                    streamed = true;
                    post(() => onText(text));
                }
                else if (type == "assistant" && obj?["message"]?["content"] is JsonArray content)
                {
                    var text = string.Concat(content.Select(c => (string?)c?["text"] ?? ""));
                    if (obj?["error"] != null) error = text;
                    else if (!streamed && text.Length > 0) post(() => onText(text));
                    else if (streamed && text.Length > 0) post(() => onText("\n\n"));
                }
                else if (type == "result" && (bool?)obj?["is_error"] == true && error == null)
                {
                    error = (string?)obj?["result"] ?? S.ErrClaude;
                }
            }
            p.WaitForExit();
            post(() =>
            {
                if (_process != p) return; // cancelled or replaced
                _process = null;
                if (error != null && (error.Contains("authenticate", StringComparison.OrdinalIgnoreCase) || error.Contains("login", StringComparison.OrdinalIgnoreCase)))
                    onDone(S.ErrNotLoggedIn);
                else
                    onDone(error);
            });
        });
    }
}

/// Streams a chat completion from an OpenAI-compatible endpoint, keeping the conversation for follow-ups.
public sealed class ApiRunner
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private CancellationTokenSource? _cts;
    private readonly List<(string Role, string Content)> _history = new();

    public void Reset()
    {
        Cancel();
        _history.Clear();
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Ask(string question, Connection c, Action<Action> post, Action<string> onText, Action<string?> onDone)
    {
        Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _history.Add(("user", question));

        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = ClaudeRunner.SystemPrompt } };
        foreach (var (role, content) in _history) messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
        var body = new JsonObject { ["model"] = c.Model, ["messages"] = messages, ["stream"] = true };

        Task.Run(async () =>
        {
            var reply = new StringBuilder();
            try
            {
                var url = c.BaseUrl.Trim().TrimEnd('/') + "/chat/completions";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                };
                var key = c.Key;
                if (!string.IsNullOrEmpty(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var text = await response.Content.ReadAsStringAsync(cts.Token);
                    var message = ErrorMessage(c, (int)response.StatusCode, text);
                    post(() => onDone(message));
                    return;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync(cts.Token)) != null)
                {
                    if (!line.StartsWith("data:")) continue;
                    var payload = line.Substring(5).Trim();
                    if (payload == "[DONE]") break;
                    string? piece;
                    try { piece = (string?)JsonNode.Parse(payload)?["choices"]?[0]?["delta"]?["content"]; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(piece)) continue;
                    reply.Append(piece);
                    post(() => onText(piece));
                }
                var full = reply.ToString();
                post(() =>
                {
                    _history.Add(("assistant", full));
                    onDone(null);
                });
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                var hint = c.BaseUrl.Contains("localhost") ? S.ErrLocalHint : "";
                post(() => onDone(S.ErrConnect(c.Name, e.Message) + hint));
            }
        });
    }

    private static string ErrorMessage(Connection c, int status, string body)
    {
        string detail;
        try
        {
            var json = JsonNode.Parse(body);
            detail = (string?)json?["error"]?["message"] ?? (json?["error"] is JsonValue v ? v.ToString() : body);
        }
        catch
        {
            detail = body.Length > 300 ? body.Substring(0, 300) : body;
        }
        return status switch
        {
            401 or 403 => S.ErrKey(c.Name, status.ToString(), detail),
            404 => S.Err404(c.Name, detail),
            429 => S.Err429(c.Name, detail),
            _ => S.ErrStatus(c.Name, status.ToString(), detail),
        };
    }
}

/// Asks `claude auth status`, which answers from local credentials without a model call.
public static class ClaudeAuth
{
    public enum State { Checking, SignedIn, SignedOut, Missing }

    public static Task<State> CheckAsync() => Task.Run(() =>
    {
        if (ClaudeRunner.Binary == null) return State.Missing;
        try
        {
            var psi = new ProcessStartInfo(ClaudeRunner.Binary, "auth status")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return (bool?)JsonNode.Parse(output)?["loggedIn"] == true ? State.SignedIn : State.SignedOut;
        }
        catch
        {
            return State.SignedOut;
        }
    });

    /// Opens a console on `claude auth login`.
    public static void SignIn()
    {
        if (ClaudeRunner.Binary == null) return;
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k \"\"{ClaudeRunner.Binary}\" auth login\"") { UseShellExecute = true });
    }
}
