using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ClaudeLight;

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
        new("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", true),
        new("Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", true),
        new("Mistral", "https://api.mistral.ai/v1", "mistral-large-latest", true),
        new("ollama", "http://localhost:11434/v1", "llama3.2", false),
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

/// Choices kept across launches in %APPDATA%\ClaudeLight\settings.json.
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

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeLight");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static Settings Load()
    {
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
    public string ChoiceTitle => Connection?.Name ?? ClaudeModel.Title;

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

    // Start with Windows: a value under the current user's Run key.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool LaunchesAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("ClaudeLight") != null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("ClaudeLight", $"\"{Environment.ProcessPath}\" --startup");
            else key.DeleteValue("ClaudeLight", false);
        }
    }
}
