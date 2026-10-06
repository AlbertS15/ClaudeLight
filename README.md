# ClaudeLight

Строка в стиле Spotlight для macOS: ищет программы и файлы на Mac и отвечает на вопросы через Claude или любую другую подключённую модель.

> Неофициальный проект, не связан с Anthropic. Claude и Clawd — товарные знаки Anthropic.

## Возможности

- **⌥ Ё** — открыть строку из любого места.
- **Поиск** программ и файлов по тому же индексу, что и Spotlight.
- **Вопросы Claude** через установленный [Claude Code](https://claude.com/claude-code) (`claude`): без API-ключа, по вашей подписке; ответы печатаются по ходу, уточняющие вопросы продолжают разговор.
- **Выбор модели**: Haiku, Sonnet, Opus, Fable.
- **Свои подключения**: любой OpenAI-совместимый сервис — OpenRouter, OpenAI, DeepSeek, Groq, Mistral, а также локальные Ollama и LM Studio. Ключи хранятся в Связке ключей macOS.
- Иконка в строке меню, стартовое окно, запуск при входе в систему.

## Клавиши

| Клавиша | Действие |
|---|---|
| ↩ | открыть файл или спросить модель |
| ⌘↩ | всегда спросить модель |
| ⌥↩ | показать файл в Finder |
| ↑ ↓ | выбрать результат |
| ⌘C | скопировать ответ |
| esc | остановить ответ, назад, закрыть |

## Сборка

Нужны macOS 14+ и Xcode Command Line Tools (`xcode-select --install`).

```bash
./build.sh
```

Скрипт соберёт `build/ClaudeLight.app` и установит его в `~/Applications`.

Для ответов Claude нужен вход в Claude Code: `claude`, затем `/login`.

## Устройство

- `Sources/main.swift` — всё приложение (AppKit + SwiftUI): окно поиска, `NSMetadataQuery`, запуск `claude -p --output-format stream-json`, клиент OpenAI-совместимого API, стартовое окно.
- `Icon/make_icon.swift` — рисует иконку при сборке.
- `build.sh` — сборка через `swiftc`, без проекта Xcode.
