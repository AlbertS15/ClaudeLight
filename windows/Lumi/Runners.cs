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

namespace Lumi;

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
                         && (string?)obj?["event"]?["delta"]?["text"] is string piece)
                {
                    streamed = true;
                    post(() => onText(piece));
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
        // The question joins the history only with its answer, so a failed try never repeats in it.
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = ClaudeRunner.SystemPrompt } };
        foreach (var (role, content) in _history) messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = question });
        var body = new JsonObject { ["model"] = c.Model, ["messages"] = messages, ["stream"] = true };
        // Thinking models in Ollama (Qwen 3.5 and the like) think first and answer 20–30 seconds later;
        // a quick bar wants the answer, so local Ollama models skip it.
        if (Ollama.IsOllama(c.BaseUrl)) body["reasoning_effort"] = "none";

        Task.Run(async () =>
        {
            var reply = new StringBuilder();
            try
            {
                // Ollama and LM Studio listen on IPv4 only; on Windows "localhost" tries IPv6 first and stalls.
                var url = c.BaseUrl.Trim().TrimEnd('/').Replace("://localhost:", "://127.0.0.1:") + "/chat/completions";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                };
                var key = c.Key ?? "";
                if (GigaChat.Handles(url))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GigaChat.TokenAsync(c, key, cts.Token));
                else if (key.Length > 0)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                // Yandex reads the folder from "gpt://<folder>/<model>"; the OpenAI SDK sends it as the project too.
                if (c.Model.StartsWith("gpt://"))
                    request.Headers.TryAddWithoutValidation("OpenAI-Project", c.Model.Substring(6).Split('/')[0]);

                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var text = await response.Content.ReadAsStringAsync(cts.Token);
                    var message = ErrorMessage(c, (int)response.StatusCode, text);
                    // Ollama answers 404 for a model name it doesn't have; say which ones it does have.
                    if ((int)response.StatusCode == 404 && Ollama.IsOllama(c.BaseUrl)
                        && await Ollama.ModelsAsync() is { Count: > 0 } installed && !installed.Contains(c.Model))
                        message = S.ErrOllamaModel(c.Model, string.Join(", ", installed));
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
                    JsonNode? obj;
                    try { obj = JsonNode.Parse(payload); }
                    catch { continue; }
                    // Services such as OpenRouter report a failure after the 200 as an "error" event in the stream.
                    if (obj?["error"] is JsonNode streamError)
                    {
                        var detail = (streamError is JsonObject o ? (string?)o["message"] : null) ?? streamError.ToJsonString();
                        post(() => onDone(S.ErrStream(c.Name, detail)));
                        return;
                    }
                    var piece = (string?)obj?["choices"]?[0]?["delta"]?["content"];
                    if (string.IsNullOrEmpty(piece)) continue;
                    reply.Append(piece);
                    post(() => onText(piece));
                }
                if (string.IsNullOrWhiteSpace(reply.ToString()))
                {
                    post(() => onDone(S.ErrEmpty(c.Name)));
                    return;
                }
                var full = reply.ToString();
                post(() =>
                {
                    _history.Add(("user", question));
                    _history.Add(("assistant", full));
                    onDone(null);
                });
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (GigaChat.KeyRefused refused)
            {
                post(() => onDone(ErrorMessage(c, refused.Status, refused.Body)));
            }
            catch (HttpRequestException e) when (e.InnerException is System.Security.Authentication.AuthenticationException)
            {
                post(() => onDone(S.ErrCert(c.Name)));
            }
            catch (Exception e)
            {
                var hint = c.BaseUrl.Contains("localhost") ? S.ErrLocalHint : "";
                post(() => onDone(S.ErrConnect(c.Name, e.Message) + hint));
            }
        });
    }

    private static bool IsBlock(string text) =>
        new[] { "security policy", "cloudflare", "blocked", "region", "country", "unsupported_country", "location is not supported" }
            .Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

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
            // A 403 from a firewall (Cloudflare, a country block) arrives before any key check.
            403 when IsBlock(detail + " " + body) => S.ErrBlocked(c.Name, detail.Contains('<') ? "Cloudflare" : detail),
            401 or 403 => S.ErrKey(c.Name, status.ToString(), detail),
            404 => S.Err404(c.Name, detail),
            429 => S.Err429(c.Name, detail),
            _ => S.ErrStatus(c.Name, status.ToString(), detail),
        };
    }
}

/// GigaChat trades the authorization key for a 30-minute access token before each chat; tokens are reused until they expire.
public static class GigaChat
{
    private const string AuthUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Dictionary<Guid, (string Token, DateTimeOffset Expires)> Tokens = new();

    public sealed class KeyRefused : Exception
    {
        public int Status { get; }
        public string Body { get; }
        public KeyRefused(int status, string body) { Status = status; Body = body; }
    }

    public static bool Handles(string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";
        return host.EndsWith("giga.chat") || host.EndsWith("sberbank.ru");
    }

    public static async Task<string> TokenAsync(Connection c, string key, CancellationToken token)
    {
        lock (Tokens)
        {
            if (Tokens.TryGetValue(c.Id, out var cached) && cached.Expires - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(1))
                return cached.Token;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthUrl)
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("scope", "GIGACHAT_API_PERS") }),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Basic " + key);
        request.Headers.Add("RqUID", Guid.NewGuid().ToString());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await Http.SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        string? access = null;
        long expiresMs = 0;
        try
        {
            var json = JsonNode.Parse(body);
            access = (string?)json?["access_token"];
            expiresMs = (long?)json?["expires_at"] ?? 0;
        }
        catch
        {
        }
        if (!response.IsSuccessStatusCode || access == null)
            throw new KeyRefused(response.IsSuccessStatusCode ? 401 : (int)response.StatusCode, body);
        var expires = expiresMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(expiresMs) : DateTimeOffset.UtcNow.AddMinutes(25);
        lock (Tokens) Tokens[c.Id] = (access, expires);
        return access;
    }
}

/// Runs Codex CLI (`codex exec`) read-only on the person's own ChatGPT login and returns its answer.
/// Codex sends the answer whole; follow-ups carry the conversation in the prompt.
public sealed class CodexRunner
{
    private Process? _process;
    private readonly List<(string Question, string Answer)> _history = new();

    /// npm puts codex.cmd in %APPDATA%\npm; otherwise whatever PATH finds.
    public static string? Binary
    {
        get
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd"),
            };
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                candidates.Add(Path.Combine(dir.Trim(), "codex.exe"));
                candidates.Add(Path.Combine(dir.Trim(), "codex.cmd"));
            }
            return candidates.FirstOrDefault(File.Exists);
        }
    }

    private static string WorkFolder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumi", "codex");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public void Reset()
    {
        Cancel();
        _history.Clear();
    }

    public void Cancel()
    {
        try { _process?.Kill(true); } catch { }
        _process = null;
    }

    public void Ask(string question, Action<Action> post, Action<string> onText, Action<string?> onDone)
    {
        Cancel();
        var binary = Binary;
        if (binary == null)
        {
            onDone(S.ErrNoCodex);
            return;
        }

        var input = new StringBuilder(ClaudeRunner.SystemPrompt + " Do not run commands or read files; just answer.\n\n");
        foreach (var (q, a) in _history) input.Append($"User: {q}\n\nAssistant: {a}\n\n");
        input.Append(_history.Count == 0 ? question : "User: " + question);

        var psi = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = WorkFolder,
        };
        foreach (var a in new[] { "exec", "--json", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "--cd", WorkFolder, "-" })
            psi.ArgumentList.Add(a);

        Process p;
        try
        {
            p = Process.Start(psi)!;
        }
        catch (Exception e)
        {
            onDone(S.ErrCodex(e.Message));
            return;
        }
        _process = p;
        p.StandardInput.Write(input.ToString());
        p.StandardInput.Close();
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();

        Task.Run(() =>
        {
            var reply = new StringBuilder();
            string? error = null;
            string? line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                JsonNode? obj;
                try { obj = JsonNode.Parse(line); } catch { continue; }
                switch ((string?)obj?["type"])
                {
                    case "item.completed" when ((string?)obj?["item"]?["type"] == "agent_message"):
                        var text = (string?)obj?["item"]?["text"];
                        if (string.IsNullOrEmpty(text)) break;
                        var piece = reply.Length == 0 ? text : "\n\n" + text;
                        reply.Append(piece);
                        post(() => onText(piece));
                        break;
                    case "turn.failed":
                        error = (obj?["error"] is JsonObject e ? (string?)e["message"] : null) ?? error;
                        break;
                    case "error":
                        error = (string?)obj?["message"] ?? error;
                        break;
                }
            }
            p.WaitForExit();
            var full = reply.ToString();
            post(() =>
            {
                if (_process != p) return; // cancelled or replaced
                _process = null;
                var message = error ?? "";
                if (new[] { "login", "logged in", "auth", "401", "unauthorized" }.Any(k => message.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    onDone(S.ErrCodexLogin);
                else if (error != null && full.Length == 0)
                    onDone(S.ErrCodex(error));
                else if (p.ExitCode != 0 && full.Length == 0)
                    onDone(S.ErrCodex("exit " + p.ExitCode));
                else
                {
                    _history.Add((question, full));
                    onDone(null);
                }
            });
        });
    }
}

/// Runs Gemini CLI headless on the person's own Google login and streams the reply.
/// Headless runs keep no conversation here, so follow-ups carry the history in the prompt.
public sealed class GeminiRunner
{
    private Process? _process;
    private readonly List<(string Question, string Answer)> _history = new();

    /// npm puts gemini.cmd in %APPDATA%\npm; otherwise whatever PATH finds.
    public static string? Binary
    {
        get
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "gemini.cmd"),
            };
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                candidates.Add(Path.Combine(dir.Trim(), "gemini.cmd"));
                candidates.Add(Path.Combine(dir.Trim(), "gemini.exe"));
            }
            return candidates.FirstOrDefault(File.Exists);
        }
    }

    /// An empty folder to run in, so Gemini never reads or indexes the home folder.
    private static string WorkFolder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumi", "gemini");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public void Reset()
    {
        Cancel();
        _history.Clear();
    }

    public void Cancel()
    {
        try { _process?.Kill(true); } catch { }
        _process = null;
    }

    public void Ask(string question, GeminiModel model, Action<Action> post, Action<string> onText, Action<string?> onDone)
    {
        Cancel();
        var binary = Binary;
        if (binary == null)
        {
            onDone(S.ErrNoGemini);
            return;
        }

        // The conversation goes in on stdin, so no shell (gemini.cmd) parses it; -p adds the instructions after it.
        var input = new StringBuilder();
        foreach (var (q, a) in _history) input.Append($"User: {q}\n\nAssistant: {a}\n\n");
        input.Append(_history.Count == 0 ? question : "User: " + question);

        var psi = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = WorkFolder,
        };
        foreach (var a in new[] { "-p", "Answer briefly, in the language of the question.", "--output-format", "stream-json", "--approval-mode", "plan" })
            psi.ArgumentList.Add(a);
        if (model.Id != "") { psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(model.Id); }

        Process p;
        try
        {
            p = Process.Start(psi)!;
        }
        catch (Exception e)
        {
            onDone(S.ErrGemini(e.Message));
            return;
        }
        _process = p;
        p.StandardInput.Write(input.ToString());
        p.StandardInput.Close();
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();

        Task.Run(() =>
        {
            var reply = new StringBuilder();
            string? error = null;
            string? line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                JsonNode? obj;
                try { obj = JsonNode.Parse(line); } catch { continue; }
                switch ((string?)obj?["type"])
                {
                    case "message" when ((string?)obj?["role"] == "assistant"):
                        var piece = (string?)obj?["content"];
                        if (string.IsNullOrEmpty(piece)) break;
                        reply.Append(piece);
                        post(() => onText(piece));
                        break;
                    case "error" when ((string?)obj?["severity"] == "error"):
                        error = (string?)obj?["message"];
                        break;
                    case "result" when ((string?)obj?["status"] == "error"):
                        error = (obj?["error"] is JsonObject e ? (string?)e["message"] : null) ?? error ?? "";
                        break;
                }
            }
            p.WaitForExit();
            var full = reply.ToString();
            post(() =>
            {
                if (_process != p) return; // cancelled or replaced
                _process = null;
                var text = error ?? "";
                if (new[] { "auth", "login", "credential", "sign in" }.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    onDone(S.ErrGeminiLogin);
                else if (error != null)
                    onDone(S.ErrGemini(error));
                else if (p.ExitCode != 0 && full.Length == 0)
                    onDone(S.ErrGemini("exit " + p.ExitCode));
                else
                {
                    _history.Add((question, full));
                    onDone(null);
                }
            });
        });
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
