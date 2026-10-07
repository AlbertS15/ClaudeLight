# Lumi для macOS

> Это ветка **macos**: только код версии для Mac. Она собирается автоматически из ветки [`main`](https://github.com/AlbertS15/Lumi) при каждом изменении — правки вносите туда.

**Lumi** — строка в стиле Spotlight, которая ещё и отвечает на вопросы. Нажмите **⌥ Ё** (на английской раскладке ⌥ `` ` ``) — и откройте программу или файл либо спросите Claude, ChatGPT, Gemini, локальную модель или любой подключённый ИИ.

![Lumi](docs/lumi-demo.gif)

📢 Новости — в Telegram-канале: [t.me/+3IU-_WIhrTU4MjAy](https://t.me/+3IU-_WIhrTU4MjAy)

## Установка

Скачайте `Lumi-macOS.dmg` со страницы [Releases](https://github.com/AlbertS15/Lumi/releases/latest) и перетащите Lumi в «Программы». Первый запуск — правый клик → «Открыть» → «Открыть» (приложение не подписано сертификатом Apple).

Удаление: стартовое окно или меню призрака в строке меню → «Удалить Lumi…».

## Сборка

Нужны macOS 14+ и Xcode Command Line Tools (`xcode-select --install`):

```bash
./build.sh
```

Соберёт `build/Lumi.app` и установит в `~/Applications`.

| Путь | Что там |
|---|---|
| `Sources/main.swift` | приложение: строка поиска, поиск Spotlight, Claude/Codex/Gemini, OpenAI-совместимые сервисы, стартовое окно |
| `Sources/Strings.swift` | тексты на 5 языках (создаётся из `strings/strings.json`) |
| `Icon/make_icon.swift` | рисует иконку при сборке |
| `build.sh` | сборка через `swiftc`, без проекта Xcode |
