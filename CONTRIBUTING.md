# Contributing to Lumi

Thanks for wanting to help! Bug reports, ideas, translations and code are all welcome. You can write in English or Russian.

## Reporting a bug or suggesting an idea

Open an [issue](https://github.com/AlbertS15/Lumi/issues/new/choose) and pick a template. Mention your system, Lumi version and model — that's usually enough to find the cause. Never include API keys or tokens, including in screenshots.

## Building

- **macOS:** macOS 14+ and the Xcode Command Line Tools, then `./build.sh`. It builds `build/Lumi.app` and installs it in `~/Applications`.
- **Windows:** the .NET 8 SDK, then `dotnet publish windows/Lumi/Lumi.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/app`.

No Windows machine? Push to a branch of your fork: GitHub Actions builds both apps and runs a Windows smoke test that saves screenshots of each step (the `Windows-screenshots` artifact).

## Where things are

| Path | What |
|---|---|
| `Sources/main.swift` | the Mac app (AppKit + SwiftUI) |
| `windows/Lumi/` | the Windows app (WPF, .NET 8) |
| `strings/strings.json` | every interface text in every language |
| `windows/ci/smoke.ps1` | the Windows smoke test |
| `.github/workflows/` | builds, releases, Telegram posts, platform branches |

Make changes in `main`. The `macos` and `windows` branches are rebuilt from it automatically.

## Adding or fixing a translation

1. Edit `strings/strings.json`: each key has one entry per language, in the order of `languages` at the top. To add a language, append it to `languages`, add a value to every string and question words to `question_words`.
2. Run `python3 strings/generate.py` to regenerate `Sources/Strings.swift` and `windows/Lumi/Strings.cs`.
3. Build and check that nothing is cut off — some languages are much longer than English.

## Pull requests

- Keep each pull request to one change, and describe what it does and how you checked it.
- When a feature touches both apps, change both — they should behave the same.
- Match the style of the surrounding code.

By contributing, you agree that your code is released under the project's [MIT license](LICENSE).
