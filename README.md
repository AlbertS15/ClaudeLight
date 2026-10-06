# ClaudeLight

Строка в стиле Spotlight для **macOS и Windows**: ищет программы и файлы и отвечает на вопросы через Claude или любую другую подключённую модель.

**Языки интерфейса:** English, Русский, Deutsch, Español, Français (по умолчанию — как в системе).

## Скачать

Готовые сборки — на странице [Releases](https://github.com/AlbertS15/ClaudeLight/releases):

- **Windows:** `ClaudeLight-Setup-….exe` (установщик, права администратора не нужны) или `ClaudeLight-Portable-….exe` (без установки).
- **macOS:** `ClaudeLight-macOS.dmg`. Приложение не подписано сертификатом Apple, поэтому при первом запуске: правый клик → «Открыть», или «Системные настройки» → «Конфиденциальность и безопасность» → «Всё равно открыть».

Windows SmartScreen тоже может предупредить о неизвестном издателе: «Подробнее» → «Выполнить в любом случае».

> Неофициальный проект, не связан с Anthropic. Claude и Clawd — товарные знаки Anthropic.

## Возможности

- **⌥ Ё** на Mac / **Alt+Ё** на Windows (клавиша слева от 1, на английской раскладке `` ` ``) — открыть строку из любого места.
- **Поиск** программ и файлов: на Mac — по индексу Spotlight, на Windows — по меню «Пуск» и индексу Windows Search.
- **Вопросы Claude** через установленный [Claude Code](https://claude.com/claude-code) (`claude`): без API-ключа, по вашей подписке; ответы печатаются по ходу, уточняющие вопросы продолжают разговор.
- **Выбор модели**: Haiku, Sonnet, Opus, Fable.
- **Свои подключения**: любой OpenAI-совместимый сервис — OpenRouter, OpenAI, DeepSeek, Groq, Mistral, а также локальные Ollama и LM Studio. Ключи хранятся в Связке ключей macOS / зашифрованы средствами Windows (DPAPI).
- Иконка в строке меню (Mac) или в трее (Windows), стартовое окно, запуск при входе в систему.

## Клавиши

| Клавиша | Действие |
|---|---|
| ↩ | открыть файл или спросить модель |
| ⌘↩ / Ctrl+↩ | всегда спросить модель |
| ⌥↩ / Alt+↩ | показать файл в Finder / Проводнике |
| ↑ ↓ | выбрать результат |
| ⌘C / Ctrl+C | скопировать ответ |
| esc | остановить ответ, назад, закрыть |

## Сборка

### macOS

Нужны macOS 14+ и Xcode Command Line Tools (`xcode-select --install`).

```bash
./build.sh
```

Скрипт соберёт `build/ClaudeLight.app` и установит его в `~/Applications`.

### Windows

Нужен .NET 8 SDK:

```powershell
dotnet publish windows/ClaudeLight/ClaudeLight.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/app
```

Установщик собирается [Inno Setup](https://jrsoftware.org/isinfo.php) из `windows/installer/ClaudeLight.iss`. GitHub Actions собирает обе версии при каждом пуше, а при теге `v*` публикует релиз.

Для ответов Claude нужен вход в Claude Code: `claude`, затем `/login`.

## Устройство

- `Sources/main.swift` — приложение для Mac (AppKit + SwiftUI): окно поиска, `NSMetadataQuery`, запуск `claude -p --output-format stream-json`, клиент OpenAI-совместимого API, стартовое окно.
- `windows/ClaudeLight/` — приложение для Windows (WPF, .NET 8) с теми же возможностями.
- `strings/strings.json` — все тексты интерфейса на всех языках. После правки: `python3 strings/generate.py` — он пересоздаёт `Sources/Strings.swift` и `windows/ClaudeLight/Strings.cs`.
- `Icon/make_icon.swift` — рисует иконку при сборке.
- `build.sh` — сборка через `swiftc`, без проекта Xcode.
