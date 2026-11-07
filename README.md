# HABITAT

**English** | [Español](README.es.md)

**A native Windows workspace for writing and connected tools.**

Chat, search, translate, draft email and run commands from one editor. HABITAT connects your existing browser sessions and desktop apps, with tasks, contacts and useful widgets alongside your work.

[Download for Windows](https://github.com/aesgalexis/habitat/releases/latest) · [Getting started](docs/GETTING_STARTED.md) · [Documentation](docs/README.md) · [Changelog](CHANGELOG.md)

Windows 10 / 11 · x64 · C# · WinUI 3 · .NET 10 · [MIT license](LICENSE)

## Install

Requires **Windows 10 version 2004 or later, or Windows 11, on x64**. Release packages include .NET and Windows App SDK; no SDK or administrator privileges are needed.

1. Download the Windows x64 setup EXE and its SHA-256 file from [GitHub Releases](https://github.com/aesgalexis/habitat/releases/latest). Verify the checksum and run the installer from your normal Windows session.
2. Open HABITAT and follow **Set up HABITAT…**. The installer adds a Start menu entry; the setup guide remains available in the app menu.
3. For browser integrations, use **Chrome 120 or later** and install **HABITAT · browser bridge** through the guide, then choose **Enable connection**. You can also load the bundled extension through **Open extension folder** and Chrome's **Load unpacked**.
4. Sign in to ChatGPT in Chrome and open the conversation you want to use. Configure other integrations as needed.

Gmail requires your own [Google OAuth desktop client](docs/GMAIL_SETUP.md). VS Code requires the bundled [HABITAT Terminal extension](vscode-extension/README.md). Codex and Phone Link need their own installed, configured apps.

Prefer the ZIP? Extract all files and run `Install-HABITAT.cmd`. Keep the same EXE or ZIP format when updating. In-app updates download and verify the matching package, preserve drafts and user data, and reopen HABITAT. See [updates](docs/UPDATES.md).

Code signing is [deferred](docs/SIGNING.md); the installer is currently unsigned. For detailed setup and connection help, see [Getting started](docs/GETTING_STARTED.md).

## Destinations

Choose a destination on the right, write and press **Enter** to run its action; **Shift+Enter** adds a line break. Each destination starts with a centered editor and reveals results after you act. Scroll over the selector to switch destinations.

| Destination | What it does |
| --- | --- |
| **OpenAI (ChatGPT)** | Send to the linked ChatGPT conversation in Chrome and read its text replies in HABITAT. |
| **Google** | Browse web search results inside HABITAT; open links in Chrome. |
| **YouTube** | Search videos and channels with thumbnails; open them in Chrome to watch. |
| **Notepad** | Place your text in a new, unsaved Notepad tab. |
| **Translate** | Translate through Google Translate into English, Greek or Spanish. |
| **Gmail** | Save an unsent email draft in your connected account. |
| **Google Maps** | Open place searches or directions in Chrome. |
| **Codex** | Work with a persistent linked Codex chat and read its text replies. |
| **VS Code** | Run commands in fresh integrated terminals in a selected local project and track their output. |
| **Terminal** | Review and confirm a command before running it in a new PowerShell window. |
| **Run** | Launch Windows programs, commands, files and folders. |

Unsubmitted text can follow you between destinations. After an action, each destination keeps its own working state, with protection for newer edits. ChatGPT follows the active Chrome chat between requests; an in-progress reply stays in its original conversation. Codex keeps its HABITAT-linked chat across navigation and restarts.

## Make it your workspace

- **Contacts.** Create a person with phone numbers, email addresses and a Messenger profile, then select them as a destination. Supported channels use Gmail drafts, Messenger or Phone Link. The address book starts empty; **Connect Google Drive** optionally syncs your HABITAT contacts between PCs. [Contact setup and channel limits](docs/CONTACTS.md).
- **Sidebar.** Keep local tasks, unread Gmail and weather at hand. Add the optional Markets widget for quotes, charts and headlines; choose and reorder widgets in Preferences. [Sidebar](docs/SIDEBAR.md) · [Markets](docs/MARKETS.md).
- **Google Drive.** Choose which contacts and tasks to sync in **Preferences → Google Drive**. Both use the **HABITAT** folder in My Drive. Task sync starts off. [Task sync](docs/TASKS_DRIVE_SYNC.md).
- **Scheduled Codex messages.** Queue a message for a new Codex chat or choose any available local chat, including chats started in Codex. HABITAT must stay open with Windows signed in. [Scheduling](docs/CODEX_SCHEDULING.md).
- **Personal destinations and appearance.** Reorder destinations, add installed Windows apps, choose Day, Night or Hybrid themes, and use included or custom wallpapers. [Wallpapers](docs/WALLPAPERS.md).
- **Ctrl+H.** Open or focus HABITAT and bring selected text into the editor without sending it. [Global shortcut](docs/GLOBAL_SHORTCUT.md).

The product interface is in English. Your messages, tasks, contact details and other content keep their original language.

## Privacy and limits

HABITAT uses your signed-in Chrome session for ChatGPT, without an OpenAI API key. The browser bridge connects locally through an authenticated loopback connection. Gmail and optional Google Drive sync use separate Google authorizations.

Tasks, preferences and contacts are stored in your Windows account. Windows Credential Manager protects Google authorization tokens. Optional Drive sync uploads the contacts and tasks you enable to your own Google account. See [desktop privacy](docs/PRIVACY.md) and [extension privacy](docs/EXTENSION_PRIVACY.md).

Browser and desktop integrations depend on the connected apps and can need maintenance when those apps change. Open rich content, tools and approval requests in the original app. Phone Link availability and transport depend on the paired phone. If delivery is uncertain, check the original conversation before retrying.

## Build and verify

Development requires **Windows x64, .NET 10 SDK and Node.js**. From the repository root:

```powershell
dotnet restore Habitat.sln -p:Platform=x64
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-winui.ps1
```

The build is staged and verified under `artifacts`. Add `-Install` to activate it in your development copy, running from your normal Windows session; then use `HABITAT.vbs` to launch.

For documentation checks or the full verification suite:

```powershell
node scripts/check-docs.cjs
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
```

The full suite runs automated checks and a Release build with isolated test data under `artifacts`. See [Contributing](CONTRIBUTING.md), [checks](docs/CHECKS.md) and [architecture](docs/ARCHITECTURE.md) for development details.

## Documentation and support

- [User guide](docs/USER_GUIDE.md) and [troubleshooting](docs/TROUBLESHOOTING.md).
- [Project status](docs/PROJECT_STATUS.md), [changelog](CHANGELOG.md) and [release process](docs/RELEASING.md).
- [Documentation index](docs/README.md) for setup, integrations, privacy and technical guides.
- [Report an issue](https://github.com/aesgalexis/habitat/issues) with the installed version and relevant connection status.

This source tree declares version **0.20.0**. Published packages and their versions are listed in [GitHub Releases](https://github.com/aesgalexis/habitat/releases).

## License

[MIT](LICENSE) · © 2026 aesgalexis
