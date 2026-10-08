# Lumi

[English](README.md) · **Русский** · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md)

**Строка в стиле Spotlight для macOS и Windows, которая ещё и отвечает на вопросы.** Одна горячая клавиша — и вы открываете программу или файл либо спрашиваете Claude, ChatGPT, Gemini, локальную модель или любой другой подключённый ИИ. Раньше называлась ClaudeLight.

![Lumi: строка, поиск, языки и модели](docs/lumi-demo.gif)

📢 **Новости и обновления — в Telegram-канале: [t.me/+3IU-_WIhrTU4MjAy](https://t.me/+3IU-_WIhrTU4MjAy)**

| | macOS | Windows |
|---|---|---|
| Скачать | [`Lumi-macOS.dmg`](https://github.com/AlbertS15/Lumi/releases/latest) | [`Lumi-Setup.exe` или `Lumi-Portable.exe`](https://github.com/AlbertS15/Lumi/releases/latest) |
| Горячая клавиша | **⌥ Ё** — клавиша слева от 1 (на английской раскладке `` ` ``) | **Alt+Ё** — клавиша слева от 1 (на английской раскладке `` ` ``) |
| Код только этой системы | ветка [`macos`](https://github.com/AlbertS15/Lumi/tree/macos) | ветка [`windows`](https://github.com/AlbertS15/Lumi/tree/windows) |

Интерфейс на пяти языках: English, Русский, Deutsch, Español, Français (по умолчанию — как в системе).

> Независимый проект, не связан с Anthropic, OpenAI или Google. Claude, ChatGPT и Gemini — товарные знаки их владельцев.

## Возможности

- **Поиск** программ и файлов: на Mac — по индексу Spotlight, на Windows — по меню «Пуск» и Windows Search. Lumi сама понимает, что вы ввели: поиск или вопрос.
- **Ответы прямо в строке**: печатаются по ходу, уточняющие вопросы продолжают разговор.
- **Подписки без API-ключей** — через официальные программы самих сервисов:
  - Claude — [Claude Code](https://claude.com/claude-code) (`claude`), модели Haiku, Sonnet, Opus, Fable;
  - ChatGPT Plus — [Codex CLI](https://github.com/openai/codex) (`codex login`);
  - Gemini — [Gemini CLI](https://github.com/google-gemini/gemini-cli) (вход через Google; 3.1 Pro, 3.8 Flash, 3.1 Flash-Lite).
- **Спросить в ChatGPT** (⇧⌘↩ / Ctrl+Shift+Enter) — открывает chatgpt.com с вашим вопросом.
- **Любой OpenAI-совместимый сервис**: OpenRouter, OpenAI, DeepSeek, Groq, Mistral, YandexGPT, GigaChat.
- **Локальные модели** через Ollama и LM Studio — бесплатно, без интернета, работают в любой стране.
- Ключи хранятся в Связке ключей macOS / зашифрованы средствами Windows. Иконка в строке меню или в трее, стартовое окно, запуск при входе, удаление в один клик.

## macOS

**Установка.** Скачайте `Lumi-macOS.dmg` со страницы [Releases](https://github.com/AlbertS15/Lumi/releases/latest) и перетащите Lumi в «Программы». Приложение не подписано сертификатом Apple, поэтому первый запуск — правый клик → «Открыть» → «Открыть».

**Обновление.** Скачайте новую версию и замените старую. Если стоял ClaudeLight, Lumi сама закроет его и уберёт в Корзину, а настройки перенесёт.

**Удаление.** Стартовое окно или меню призрака в строке меню → «Удалить Lumi…».

**Сборка из исходников.** Нужны macOS 14+ и Xcode Command Line Tools (`xcode-select --install`):

```bash
./build.sh
```

Скрипт соберёт `build/Lumi.app` и установит его в `~/Applications`. Код: `Sources/` (AppKit + SwiftUI), иконка рисуется `Icon/make_icon.swift`.

## Windows

**Установка.** Скачайте со страницы [Releases](https://github.com/AlbertS15/Lumi/releases/latest) установщик `Lumi-Setup-….exe` (права администратора не нужны) или `Lumi-Portable-….exe` (без установки). SmartScreen может предупредить о неизвестном издателе: «Подробнее» → «Выполнить в любом случае».

**Обновление.** Запустите новый установщик: он заменит старую версию, в том числе ClaudeLight, и перенесёт настройки и автозапуск.

**Удаление.** «Параметры» → «Приложения» → Lumi → «Удалить».

**Сборка из исходников.** Нужен .NET 8 SDK:

```powershell
dotnet publish windows/Lumi/Lumi.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/app
```

Установщик собирается [Inno Setup](https://jrsoftware.org/isinfo.php) из `windows/installer/Lumi.iss`. Код: `windows/Lumi/` (WPF, .NET 8).

## Клавиши

| Клавиша | Действие |
|---|---|
| ↩ | открыть файл или спросить модель |
| ⌘↩ / Ctrl+↩ | всегда спросить модель |
| ⇧⌘↩ / Ctrl+Shift+↩ | спросить в ChatGPT |
| ⌥↩ / Alt+↩ | показать файл в Finder / Проводнике |
| ↑ ↓ | выбрать результат |
| ⌘C / Ctrl+C | скопировать ответ |
| esc | остановить ответ, назад, закрыть |

## Для разработчиков

- `main` — основная ветка с кодом обеих версий. Все изменения вносятся сюда.
- `macos` и `windows` — только код своей системы. Их автоматически пересобирает GitHub Actions при каждом изменении `main`, вручную их не меняют.
- `strings/strings.json` — тексты интерфейса на всех языках. После правки: `python3 strings/generate.py` — пересоздаёт `Sources/Strings.swift` и `windows/Lumi/Strings.cs`.

**Выпуск версии.** Написать заметку `release-notes/vX.Y.Z.html` (по-русски, HTML Telegram: `<b>`, `<i>`, `<a>`, `<code>`, до ~3900 символов), затем:

```bash
git tag -a vX.Y.Z -m "Lumi X.Y.Z" && git push origin vX.Y.Z
```

GitHub Actions соберёт обе версии, проверит Windows-версию со скриншотами, опубликует релиз с текстом заметки и отправит её в Telegram-канал (если заданы секреты `TELEGRAM_BOT_TOKEN` и `TELEGRAM_CHAT_ID`). Повторить пост вручную: Actions → «Post release to Telegram».

## Лицензия

[MIT](LICENSE) © 2026 AlbertS15
