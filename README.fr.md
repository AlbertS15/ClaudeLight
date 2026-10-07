# Lumi

[English](README.md) · [Русский](README.ru.md) · [Deutsch](README.de.md) · [Español](README.es.md) · **Français**

**Une barre façon Spotlight pour macOS et Windows qui répond aussi aux questions.** Un raccourci ouvre une application ou un fichier, ou interroge Claude, ChatGPT, Gemini, un modèle local ou toute autre IA connectée. Anciennement ClaudeLight.

![Lumi : la barre, la recherche, les langues et les modèles](docs/lumi-demo.gif)

📢 **Actualités et mises à jour — chaîne Telegram (en russe) : [t.me/+3IU-_WIhrTU4MjAy](https://t.me/+3IU-_WIhrTU4MjAy)**

| | macOS | Windows |
|---|---|---|
| Télécharger | [`Lumi-macOS.dmg`](https://github.com/AlbertS15/Lumi/releases/latest) | [`Lumi-Setup.exe` ou `Lumi-Portable.exe`](https://github.com/AlbertS15/Lumi/releases/latest) |
| Raccourci | **⌥ + la touche à gauche du 1** | **Alt+`` ` ``** (touche à gauche du 1 sur les dispositions américaine et russe) |
| Code de cette plateforme seulement | branche [`macos`](https://github.com/AlbertS15/Lumi/tree/macos) | branche [`windows`](https://github.com/AlbertS15/Lumi/tree/windows) |

Interface en cinq langues : English, Русский, Deutsch, Español, Français (celle du système par défaut).

> Projet indépendant, sans lien avec Anthropic, OpenAI ou Google. Claude, ChatGPT et Gemini sont des marques de leurs propriétaires.

## Fonctionnalités

- **Recherche** d'applications et de fichiers : l'index Spotlight sur Mac, le menu Démarrer et Windows Search sur Windows. Lumi distingue seule une recherche d'une question.
- **Réponses directement dans la barre**, affichées au fil de l'eau ; les questions de suivi prolongent la conversation.
- **Abonnements sans clé d'API**, via l'outil officiel de chaque service :
  - Claude — [Claude Code](https://claude.com/claude-code) (`claude`) : Haiku, Sonnet, Opus, Fable ;
  - ChatGPT Plus — [Codex CLI](https://github.com/openai/codex) (`codex login`) ;
  - Gemini — [Gemini CLI](https://github.com/google-gemini/gemini-cli) (connexion avec Google ; 3.1 Pro, 3.8 Flash, 3.1 Flash-Lite).
- **Demander dans ChatGPT** (⇧⌘↩ / Ctrl+Maj+Entrée) ouvre chatgpt.com avec votre question.
- **Tout service compatible OpenAI** : OpenRouter, OpenAI, DeepSeek, Groq, Mistral, YandexGPT, GigaChat.
- **Modèles locaux** avec Ollama et LM Studio : gratuits, hors ligne, dans n'importe quel pays.
- Les clés sont conservées dans le trousseau macOS ou chiffrées par Windows. Icône dans la barre des menus ou la zone de notification, fenêtre d'accueil, ouverture à la connexion, désinstallation en un clic.

## macOS

**Installation.** Téléchargez `Lumi-macOS.dmg` depuis [Releases](https://github.com/AlbertS15/Lumi/releases/latest) et glissez Lumi dans Applications. L'app n'est pas signée par un certificat Apple : au premier lancement, clic droit → Ouvrir → Ouvrir.

**Mise à jour.** Téléchargez la nouvelle version et remplacez l'ancienne. Si ClaudeLight est installé, Lumi le ferme, le place dans la Corbeille et garde ses réglages.

**Désinstallation.** Fenêtre d'accueil ou menu du fantôme dans la barre des menus → « Désinstaller Lumi… ».

**Compiler depuis les sources.** Nécessite macOS 14+ et les Command Line Tools de Xcode (`xcode-select --install`) :

```bash
./build.sh
```

Compile `build/Lumi.app` et l'installe dans `~/Applications`. Code : `Sources/` (AppKit + SwiftUI) ; l'icône est dessinée par `Icon/make_icon.swift`.

## Windows

**Installation.** Depuis [Releases](https://github.com/AlbertS15/Lumi/releases/latest), prenez l'installateur `Lumi-Setup-….exe` (pas besoin de droits administrateur) ou `Lumi-Portable-….exe` (sans installation). Si SmartScreen signale un éditeur inconnu : « Informations complémentaires » → « Exécuter quand même ».

**Mise à jour.** Lancez le nouvel installateur : il remplace l'ancienne version, ClaudeLight compris, et garde vos réglages et le démarrage avec Windows.

**Désinstallation.** Paramètres → Applications → Lumi → Désinstaller.

**Compiler depuis les sources.** Nécessite le SDK .NET 8 :

```powershell
dotnet publish windows/Lumi/Lumi.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/app
```

L'installateur est construit avec [Inno Setup](https://jrsoftware.org/isinfo.php) à partir de `windows/installer/Lumi.iss`. Code : `windows/Lumi/` (WPF, .NET 8).

## Touches

| Touche | Action |
|---|---|
| ↩ | ouvrir le fichier ou interroger le modèle |
| ⌘↩ / Ctrl+↩ | toujours interroger le modèle |
| ⇧⌘↩ / Ctrl+Maj+↩ | demander dans ChatGPT |
| ⌥↩ / Alt+↩ | afficher le fichier dans le Finder / l'Explorateur |
| ↑ ↓ | choisir un résultat |
| ⌘C / Ctrl+C | copier la réponse |
| esc | arrêter la réponse, revenir, fermer |

## Pour les développeurs

- `main` contient les deux applications ; toutes les modifications se font ici.
- `macos` et `windows` contiennent chacune une plateforme. GitHub Actions les régénère à chaque modification de `main` ; ne les modifiez pas à la main.
- `strings/strings.json` regroupe tous les textes de l'interface dans toutes les langues. Après modification, lancez `python3 strings/generate.py`.

Publier une version : voir le [README en anglais](README.md#for-developers).
