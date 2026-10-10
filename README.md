# Lumi

**English** · [Русский](README.ru.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md)

**A Spotlight-style bar for macOS, Windows and Linux that also answers questions.** One hotkey opens an app or file, or asks Claude, ChatGPT, Gemini, a local model or any other AI you connect. Formerly called ClaudeLight.

![Lumi: the bar, search, languages and models](docs/lumi-demo.gif)

📢 **News and updates — Telegram channel (in Russian): [t.me/+3IU-_WIhrTU4MjAy](https://t.me/+3IU-_WIhrTU4MjAy)**

| | macOS | Windows | Linux |
|---|---|---|---|
| Download | [`Lumi-macOS.dmg`](https://github.com/AlbertS15/Lumi/releases/latest) | [`Lumi-Setup.exe` or `Lumi-Portable.exe`](https://github.com/AlbertS15/Lumi/releases/latest) | [`Lumi-x86_64.AppImage`](https://github.com/AlbertS15/Lumi/releases/latest) |
| Hotkey | **⌥ + the key left of 1** (`` ` `` or §) | **Alt + the key left of 1** (`` ` ``, Ё, ^, ² — any layout) | **Ctrl+Space** (Alt + the key left of 1 is taken by GNOME and KDE) |
| Code for this platform only | [`macos`](https://github.com/AlbertS15/Lumi/tree/macos) branch | [`windows`](https://github.com/AlbertS15/Lumi/tree/windows) branch | folder [`linux/`](linux) in `main` |

The hotkey can be changed in Lumi's welcome window: “Change” under the key caps.

Interface in five languages: English, Русский, Deutsch, Español, Français (the system's by default).

> An independent project, not affiliated with Anthropic, OpenAI or Google. Claude, ChatGPT and Gemini are trademarks of their owners.

## Features

- **Search** apps and files: the Spotlight index on Mac, the Start menu and Windows Search on Windows. Lumi tells a search from a question by itself.
- **Answers right in the bar**, streamed as they are written; follow-up questions continue the conversation.
- **Subscriptions without API keys**, through each service's official tool:
  - Claude — [Claude Code](https://claude.com/claude-code) (`claude`): Haiku, Sonnet, Opus, Fable;
  - ChatGPT Plus — [Codex CLI](https://github.com/openai/codex) (`codex login`);
  - Gemini — [Gemini CLI](https://github.com/google-gemini/gemini-cli) (sign in with Google; 3.1 Pro, 3.8 Flash, 3.1 Flash-Lite).
- **Ask in ChatGPT** (⇧⌘↩ / Ctrl+Shift+Enter) opens chatgpt.com with your question.
- **Any OpenAI-compatible service**: OpenRouter, OpenAI, DeepSeek, Groq, Mistral, YandexGPT, GigaChat.
- **Local models** through Ollama and LM Studio — free, offline, work in any country.
- Keys are kept in the macOS Keychain / encrypted by Windows / in a settings file only you can read on Linux. Menu bar or tray icon, welcome window, open at login, one-click uninstall.

## macOS

**Install.** Download `Lumi-macOS.dmg` from [Releases](https://github.com/AlbertS15/Lumi/releases/latest) and drag Lumi to Applications. The app isn't signed with an Apple certificate, so the first launch is right-click → Open → Open.

**Update.** Download the new version and replace the old one. If ClaudeLight is installed, Lumi quits it, moves it to the Trash and keeps its settings.

**Uninstall.** Welcome window or the ghost's menu in the menu bar → “Uninstall Lumi…”.

**Build from source.** Needs macOS 14+ and the Xcode Command Line Tools (`xcode-select --install`):

```bash
./build.sh
```

It builds `build/Lumi.app` and installs it in `~/Applications`. Code: `Sources/` (AppKit + SwiftUI); the icon is drawn by `Icon/make_icon.swift`.

## Windows

**Install.** From [Releases](https://github.com/AlbertS15/Lumi/releases/latest), get the installer `Lumi-Setup-….exe` (no admin rights needed) or `Lumi-Portable-….exe` (no install). If SmartScreen warns about an unknown publisher: “More info” → “Run anyway”.

**Update.** Run the new installer: it replaces the old version, ClaudeLight included, and keeps your settings and start-with-Windows choice.

**Uninstall.** Settings → Apps → Lumi → Uninstall.

**Build from source.** Needs the .NET 8 SDK:

```powershell
dotnet publish windows/Lumi/Lumi.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/app
```

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) from `windows/installer/Lumi.iss`. Code: `windows/Lumi/` (WPF, .NET 8).

## Linux

**Install.** Download `Lumi-x86_64.AppImage` from [Releases](https://github.com/AlbertS15/Lumi/releases/latest), then:

```bash
chmod +x Lumi-x86_64.AppImage
./Lumi-x86_64.AppImage
```

If it complains about FUSE, run it with `--appimage-extract-and-run` or install `libfuse2`.

**Hotkey.** Ctrl+Space by default. On X11 Lumi takes it itself; on GNOME with Wayland it adds a shortcut to GNOME's keyboard settings. On other Wayland desktops the welcome window shows a command, `lumi --toggle`, to bind to a key in the system settings.

**Offline models.** “Install Lumi” unpacks Ollama into `~/.local/share/lumi` (no sudo) after checking its SHA-256, then sets up the model.

**Build from source.** Needs the .NET 8 SDK: `./linux/package.sh` builds `dist/Lumi-x86_64.AppImage`. Code: `linux/Lumi/` (Avalonia, .NET 8), sharing the AI runners, settings and strings with `windows/Lumi/`.

## Keys

| Key | Action |
|---|---|
| ↩ | open the file or ask the model |
| ⌘↩ / Ctrl+↩ | always ask the model |
| ⇧⌘↩ / Ctrl+Shift+↩ | ask in ChatGPT |
| ⌥↩ / Alt+↩ | show the file in Finder / Explorer |
| ↑ ↓ | pick a result |
| ⌘C / Ctrl+C | copy the answer |
| esc | stop the answer, go back, close |

## For developers

- `main` holds both apps; all changes go here.
- `macos` and `windows` hold one platform each. GitHub Actions rebuilds them on every change to `main`; don't edit them by hand.
- `strings/strings.json` has every interface text in every language. After editing, run `python3 strings/generate.py` to regenerate `Sources/Strings.swift` and `windows/Lumi/Strings.cs`.

**Releasing.** Write `release-notes/vX.Y.Z.html` (Telegram HTML: `<b>`, `<i>`, `<a>`, `<code>`, up to ~3900 characters), then:

```bash
git tag -a vX.Y.Z -m "Lumi X.Y.Z" && git push origin vX.Y.Z
```

GitHub Actions builds both apps, smoke-tests the Windows one with screenshots, publishes the release with the note and posts it to the Telegram channel (when the `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID` secrets are set).

## License

[MIT](LICENSE) © 2026 AlbertS15
