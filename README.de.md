# Lumi

[English](README.md) · [Русский](README.ru.md) · **Deutsch** · [Español](README.es.md) · [Français](README.fr.md)

**Eine Leiste im Stil von Spotlight für macOS und Windows, die auch Fragen beantwortet.** Ein Tastenkürzel öffnet ein Programm oder eine Datei – oder fragt Claude, ChatGPT, Gemini, ein lokales Modell oder jede andere verbundene KI. Hieß früher ClaudeLight.

![Lumi: Leiste, Suche, Sprachen und Modelle](docs/lumi-demo.gif)

📢 **Neuigkeiten und Updates – Telegram-Kanal (auf Russisch): [t.me/+3IU-_WIhrTU4MjAy](https://t.me/+3IU-_WIhrTU4MjAy)**

| | macOS | Windows |
|---|---|---|
| Download | [`Lumi-macOS.dmg`](https://github.com/AlbertS15/Lumi/releases/latest) | [`Lumi-Setup.exe` oder `Lumi-Portable.exe`](https://github.com/AlbertS15/Lumi/releases/latest) |
| Tastenkürzel | **⌥ + Taste links neben der 1** (auf deutscher Tastatur ⌥ ^) | **Alt + Taste links neben der 1** (`` ` ``, ^, Ё, ² – jedes Layout) |
| Nur der Code dieser Plattform | Branch [`macos`](https://github.com/AlbertS15/Lumi/tree/macos) | Branch [`windows`](https://github.com/AlbertS15/Lumi/tree/windows) |

Oberfläche in fünf Sprachen: English, Русский, Deutsch, Español, Français (standardmäßig die Systemsprache).

> Ein unabhängiges Projekt, nicht mit Anthropic, OpenAI oder Google verbunden. Claude, ChatGPT und Gemini sind Marken ihrer Inhaber.

## Funktionen

- **Suche** nach Programmen und Dateien: über den Spotlight-Index auf dem Mac, über das Startmenü und Windows Search unter Windows. Lumi erkennt selbst, ob Sie suchen oder fragen.
- **Antworten direkt in der Leiste**, Wort für Wort; Rückfragen setzen das Gespräch fort.
- **Abos ohne API-Schlüssel** – über die offiziellen Programme der Dienste:
  - Claude – [Claude Code](https://claude.com/claude-code) (`claude`): Haiku, Sonnet, Opus, Fable;
  - ChatGPT Plus – [Codex CLI](https://github.com/openai/codex) (`codex login`);
  - Gemini – [Gemini CLI](https://github.com/google-gemini/gemini-cli) (Anmeldung mit Google; 3.1 Pro, 3.8 Flash, 3.1 Flash-Lite).
- **In ChatGPT fragen** (⇧⌘↩ / Strg+Umschalt+Enter) öffnet chatgpt.com mit Ihrer Frage.
- **Jeder OpenAI-kompatible Dienst**: OpenRouter, OpenAI, DeepSeek, Groq, Mistral, YandexGPT, GigaChat.
- **Lokale Modelle** über Ollama und LM Studio – kostenlos, offline, in jedem Land.
- Schlüssel liegen im macOS-Schlüsselbund bzw. werden von Windows verschlüsselt. Symbol in Menüleiste oder Infobereich, Startfenster, Start bei Anmeldung, Deinstallation per Klick.

## macOS

**Installation.** `Lumi-macOS.dmg` unter [Releases](https://github.com/AlbertS15/Lumi/releases/latest) laden und Lumi in „Programme“ ziehen. Die App ist nicht mit einem Apple-Zertifikat signiert, daher beim ersten Start: Rechtsklick → „Öffnen“ → „Öffnen“.

**Update.** Neue Version laden und die alte ersetzen. Ist ClaudeLight installiert, beendet Lumi es, legt es in den Papierkorb und übernimmt die Einstellungen.

**Deinstallation.** Startfenster oder Menü des Geistes in der Menüleiste → „Lumi deinstallieren…“.

**Aus dem Quellcode bauen.** Benötigt macOS 14+ und die Xcode Command Line Tools (`xcode-select --install`):

```bash
./build.sh
```

Baut `build/Lumi.app` und installiert es in `~/Applications`. Code: `Sources/` (AppKit + SwiftUI); das Symbol zeichnet `Icon/make_icon.swift`.

## Windows

**Installation.** Unter [Releases](https://github.com/AlbertS15/Lumi/releases/latest) das Installationsprogramm `Lumi-Setup-….exe` (keine Administratorrechte nötig) oder `Lumi-Portable-….exe` (ohne Installation) laden. Warnt SmartScreen vor einem unbekannten Herausgeber: „Weitere Informationen“ → „Trotzdem ausführen“.

**Update.** Neues Installationsprogramm starten: Es ersetzt die alte Version, auch ClaudeLight, und übernimmt Einstellungen und Autostart.

**Deinstallation.** Einstellungen → Apps → Lumi → Deinstallieren.

**Aus dem Quellcode bauen.** Benötigt das .NET 8 SDK:

```powershell
dotnet publish windows/Lumi/Lumi.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/app
```

Das Installationsprogramm entsteht mit [Inno Setup](https://jrsoftware.org/isinfo.php) aus `windows/installer/Lumi.iss`. Code: `windows/Lumi/` (WPF, .NET 8).

## Tasten

| Taste | Aktion |
|---|---|
| ↩ | Datei öffnen oder Modell fragen |
| ⌘↩ / Strg+↩ | immer das Modell fragen |
| ⇧⌘↩ / Strg+Umschalt+↩ | in ChatGPT fragen |
| ⌥↩ / Alt+↩ | Datei im Finder / Explorer zeigen |
| ↑ ↓ | Ergebnis wählen |
| ⌘C / Strg+C | Antwort kopieren |
| esc | Antwort stoppen, zurück, schließen |

## Für Entwickler

- `main` enthält beide Apps; alle Änderungen gehören hierher.
- `macos` und `windows` enthalten je eine Plattform. GitHub Actions baut sie bei jeder Änderung an `main` neu – bitte nicht von Hand bearbeiten.
- `strings/strings.json` enthält alle Oberflächentexte in allen Sprachen. Nach Änderungen `python3 strings/generate.py` ausführen.

Details zum Veröffentlichen einer Version: siehe [englische README](README.md#for-developers).

## Lizenz

[MIT](LICENSE) © 2026 AlbertS15
