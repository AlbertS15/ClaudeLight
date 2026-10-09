using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Lumi;

/// The Claude models the picker offers; Auto ("") leaves the choice to Claude Code's own default.
public sealed record ClaudeModel(string Id)
{
    public static readonly ClaudeModel[] All = { new(""), new("haiku"), new("sonnet"), new("opus"), new("fable") };

    public string Title => Id switch
    {
        "" => S.ModelAuto,
        _ => char.ToUpperInvariant(Id[0]) + Id.Substring(1),
    };

    public string Note => Id switch
    {
        "haiku" => S.NoteHaiku,
        "sonnet" => S.NoteSonnet,
        "opus" => S.NoteOpus,
        "fable" => S.NoteFable,
        _ => S.NoteAuto,
    };
}

/// Gemini models through the person's own Google login in Gemini CLI; "" leaves the choice to the CLI.
public sealed record GeminiModel(string Id, string Title)
{
    public static readonly GeminiModel[] All =
    {
        new("", "Gemini"),
        new("gemini-3.1-pro-preview", "Gemini 3.1 Pro"),
        new("gemini-3.8-flash", "Gemini 3.8 Flash"),
        new("gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite"),
    };

    public string Note => Id switch
    {
        "gemini-3.1-pro-preview" => S.NoteOpus,
        "gemini-3.8-flash" => S.NoteSonnet,
        "gemini-3.1-flash-lite" => S.NoteHaiku,
        _ => S.NoteGeminiAuto,
    };
}

/// A model on any OpenAI-compatible service. The key is stored encrypted for this Windows user (DPAPI).
public sealed class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ProtectedKey { get; set; } = "";

    [JsonIgnore]
    public string? Key
    {
        get
        {
            if (string.IsNullOrEmpty(ProtectedKey)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKey), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }
        set
        {
            ProtectedKey = string.IsNullOrEmpty(value)
                ? ""
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        }
    }
}

/// Ready-made addresses for the add-connection form.
public sealed record ServicePreset(string Key, string BaseUrl, string Example, bool NeedsKey)
{
    public static readonly ServicePreset[] All =
    {
        new("OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-4o", true),
        new("OpenAI", "https://api.openai.com/v1", "gpt-4o", true),
        new("DeepSeek", "https://api.deepseek.com", "deepseek-flash", true),
        new("Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", true),
        new("Mistral", "https://api.mistral.ai/v1", "mistral-large-latest", true),
        new("YandexGPT", "https://ai.api.cloud.yandex.net/v1", "gpt://b1g…/yandexgpt", true),
        new("GigaChat", "https://api.giga.chat/v1", "GigaChat-2", true),
        new("ollama", "http://127.0.0.1:11434/v1", "llama3.2", false),
        new("lmstudio", "http://localhost:1234/v1", "", false),
        new("other", "", "", true),
    };

    public string Title => Key switch
    {
        "ollama" => S.PresetOllama,
        "lmstudio" => S.PresetLmstudio,
        "other" => S.PresetOther,
        _ => Key,
    };

    public string ModelHint => Key switch
    {
        "lmstudio" => S.HintLoadedModel,
        "other" => S.HintModelName,
        _ => S.HintExample(Example),
    };

    public override string ToString() => Title;
}

/// Choices kept across launches in %APPDATA%\Lumi\settings.json.
public sealed class Settings
{
    public static Settings Shared { get; } = Load();

    /// "claude:<id>" or "custom:<guid>".
    public string Choice { get; set; } = "claude:";
    public List<Connection> Connections { get; set; } = new();
    public bool ShowWelcomeOnLaunch { get; set; } = true;
    /// Whether results offer "Ask in ChatGPT" (opens chatgpt.com on the person's own login).
    public bool ShowChatGpt { get; set; } = true;
    /// Interface language code, "" for the system's.
    public string Language { get; set; } = "";

    public event Action? Changed;

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumi");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static Settings Load()
    {
        // Lumi used to be called ClaudeLight: bring its settings (and their encrypted keys) along once.
        var oldFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeLight", "settings.json");
        try
        {
            if (!File.Exists(FilePath) && File.Exists(oldFile))
            {
                Directory.CreateDirectory(Folder);
                File.Copy(oldFile, FilePath);
            }
        }
        catch
        {
        }
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    if (s.Choice.StartsWith("custom:") && s.Connection == null) s.Choice = "claude:";
                    return s;
                }
            }
        }
        catch
        {
            // A broken file starts over with defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        Changed?.Invoke();
    }

    [JsonIgnore]
    public Connection? Connection =>
        Choice.StartsWith("custom:") && Guid.TryParse(Choice.Substring(7), out var id)
            ? Connections.FirstOrDefault(c => c.Id == id)
            : null;

    [JsonIgnore]
    public ClaudeModel ClaudeModel =>
        System.Array.Find(ClaudeModel.All, m => "claude:" + m.Id == Choice) ?? ClaudeModel.All[0];

    [JsonIgnore]
    public string ChoiceTitle => Connection?.Name ?? GeminiModel?.Title ?? (IsCodex ? S.CodexTitle : ClaudeModel.Title);

    /// "codex:" = ChatGPT through Codex CLI on the person's own login.
    [JsonIgnore]
    public bool IsCodex => Choice == "codex:";

    /// The Gemini model when the choice is "gemini:<id>", else null.
    [JsonIgnore]
    public GeminiModel? GeminiModel =>
        Choice.StartsWith("gemini:") ? System.Array.Find(GeminiModel.All, m => "gemini:" + m.Id == Choice) ?? GeminiModel.All[0] : null;

    public void Select(string choice)
    {
        Choice = choice;
        Save();
    }

    public void Add(Connection c)
    {
        Connections.Add(c);
        Choice = "custom:" + c.Id;
        Save();
    }

    public void Remove(Connection c)
    {
        Connections.RemoveAll(x => x.Id == c.Id);
        if (Choice == "custom:" + c.Id) Choice = "claude:";
        Save();
    }

    /// Moves a ClaudeLight start-with-Windows entry over to Lumi, once.
    public static void MigrateLaunchAtLogin()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue("ClaudeLight") != null) LaunchesAtLogin = true;
    }

    // Start with Windows: a value under the current user's Run key.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool LaunchesAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Lumi") != null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Lumi", $"\"{Environment.ProcessPath}\" --startup");
            else key.DeleteValue("Lumi", false);
            key.DeleteValue("ClaudeLight", false);
        }
    }
}

/// Ollama's own API on this computer.
public static class Ollama
{
    /// Whether an address points at Ollama on this computer.
    public static bool IsOllama(string address) => address.Contains("localhost:11434") || address.Contains("127.0.0.1:11434");

    /// The installed models, or null when Ollama isn't running. 127.0.0.1 rather than localhost: Ollama listens on IPv4
    /// only, and Windows spends about two seconds on a refused IPv6 connection before it tries IPv4.
    public static async System.Threading.Tasks.Task<List<string>?> ModelsAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var json = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:11434/api/tags"));
            var models = new List<string>();
            if (json.RootElement.TryGetProperty("models", out var list))
                foreach (var m in list.EnumerateArray())
                    if (m.TryGetProperty("name", out var name) && name.GetString() is { } n) models.Add(n);
            return models;
        }
        catch
        {
            return null;
        }
    }
}
