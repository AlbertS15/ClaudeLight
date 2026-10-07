# Lumi для Windows

README: [English](https://github.com/AlbertS15/Lumi/blob/main/README.md) · [Русский](https://github.com/AlbertS15/Lumi/blob/main/README.ru.md) · [Deutsch](https://github.com/AlbertS15/Lumi/blob/main/README.de.md) · [Español](https://github.com/AlbertS15/Lumi/blob/main/README.es.md) · [Français](https://github.com/AlbertS15/Lumi/blob/main/README.fr.md)

> Это ветка **windows**: только код версии для Windows. Она собирается автоматически из ветки [`main`](https://github.com/AlbertS15/Lumi) при каждом изменении — правки вносите туда.

**Lumi** — строка в стиле Spotlight, которая ещё и отвечает на вопросы. Нажмите **Alt+Ё** (на английской раскладке Alt+`` ` ``) — и откройте программу или файл либо спросите Claude, ChatGPT, Gemini, локальную модель или любой подключённый ИИ.

![Lumi](docs/lumi-demo.gif)

📢 Новости — в Telegram-канале: [t.me/+3IU-_WIhrTU4MjAy](https://t.me/+3IU-_WIhrTU4MjAy)

## Установка

Со страницы [Releases](https://github.com/AlbertS15/Lumi/releases/latest): установщик `Lumi-Setup-….exe` (права администратора не нужны) или `Lumi-Portable-….exe` (без установки). Если SmartScreen предупредит о неизвестном издателе: «Подробнее» → «Выполнить в любом случае».

Удаление: «Параметры» → «Приложения» → Lumi → «Удалить».

## Сборка

Нужен .NET 8 SDK:

```powershell
dotnet publish windows/Lumi/Lumi.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/app
```

Установщик: [Inno Setup](https://jrsoftware.org/isinfo.php), скрипт `windows/installer/Lumi.iss`.

| Путь | Что там |
|---|---|
| `windows/Lumi/` | приложение (WPF, .NET 8): строка, поиск Windows Search, Claude/Codex/Gemini, OpenAI-совместимые сервисы, трей |
| `windows/Lumi/Strings.cs` | тексты на 5 языках (создаётся из `strings/strings.json`) |
| `windows/installer/Lumi.iss` | установщик |
| `windows/ci/smoke.ps1` | автопроверка запуска со скриншотами |
