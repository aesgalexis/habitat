# HABITAT

A native Windows desktop surface for writing to your ChatGPT conversation in Chrome and reading its text replies. HABITAT uses your Chrome session through a local browser bridge, without an OpenAI API key. Your original conversation stays in ChatGPT.

Built with **C# / WinUI 3 and .NET 10**, HABITAT includes integrated Google, YouTube and Translate results; local tasks; Gmail reading and unsent drafts; weather; Ctrl+H selected-text capture; and destinations for Notepad, Google Maps, Codex, VS Code, Terminal and Run. The interface is in English; your own content keeps its original language.

[Download Windows x64](https://github.com/aesgalexis/habitat/releases/latest) · [First-time setup](docs/GETTING_STARTED.md) · [User guide](docs/USER_GUIDE.md) · [Documentation index](docs/README.md) · [Changelog](CHANGELOG.md)

## Install

Requires Windows x64, Windows 10 version 2004 or later or Windows 11. Desktop packages include .NET and Windows App SDK; users do not need development tools or administrator privileges.

1. Download the Windows setup EXE and its `.sha256` file from GitHub Releases. Use the release assets; GitHub's Source code ZIP is not the compiled app.
2. Verify the SHA-256 and run the installer from your normal Windows session. It installs per user and adds a Start menu entry.
3. Open **Set up HABITAT…**, connect the Chrome extension with **Enable connection**, and open your signed-in ChatGPT conversation in Chrome. If the store listing is unavailable, the setup guide explains loading the bundled extension.

Google Chrome 120 or later is required for the browser bridge. Gmail, Codex and VS Code are optional and require separate setup. The desktop installer is currently unsigned; [code signing remains deferred](docs/SIGNING.md).

For ZIP installation, extract the entire release ZIP and run `Install-HABITAT.cmd`. Keep the same EXE or ZIP format when updating. See [setup, updates and uninstall](docs/GETTING_STARTED.md) for exact steps.

## Use

Select a destination, write in the editor and run its action. **Enter** submits; **Shift+Enter** inserts a line break. Each destination begins with an action prompt, centered editor and action button. Results appear after execution; clearing returns to the initial editor.

OpenAI follows the linked ChatGPT conversation between requests; an in-progress reply stays in its original chat. Chrome may remain in the background, but must stay running. Check the original conversation before retrying any send whose delivery is uncertain.

**Ctrl+H** focuses HABITAT and can import selected text without sending it. **Preferences…** controls appearance, location, text-only replies and the shortcut. See the [user guide](docs/USER_GUIDE.md) for destinations, drafts, Gmail, weather and terminal behavior, or [troubleshooting](docs/TROUBLESHOOTING.md) for recovery.

## Updates and limitations

HABITAT checks for newer stable releases and offers **Update** or **Later**. Save important editor text before updating: window text and results are cleared on restart; saved tasks, preferences and credentials remain. [UPDATES.md](docs/UPDATES.md) is the reference for timing, formats and recovery.

The browser integration depends on provider web interfaces. Rich widgets, images, files and tool interfaces are viewed in their original applications. Keep pairing codes and credentials private. Read the [desktop privacy notes](docs/PRIVACY.md) and [extension privacy policy](docs/EXTENSION_PRIVACY.md) for data and connections.

## Build

Development requires Windows x64 and .NET 10 SDK. From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-winui.ps1
```

This prepares and verifies a new build under `artifacts/staged`. To activate it, use the printed command from your normal Windows session; `HABITAT.vbs` launches the active source build. See [CONTRIBUTING.md](CONTRIBUTING.md) for installation, collaboration and hooks, including the optional automatic push on `codex/autosave`.

## Checks

With Node.js and the .NET SDK installed:

```powershell
node scripts/check-docs.cjs
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
```

The shared verification uses synthetic data and isolated outputs. [CHECKS.md](docs/CHECKS.md) explains its scope and optional native/live checks. [ARCHITECTURE.md](docs/ARCHITECTURE.md) describes the modules; [RELEASING.md](docs/RELEASING.md) covers distribution.

Source, package metadata and published assets can differ during development. [PROJECT_STATUS.md](docs/PROJECT_STATUS.md) records the current evidence and documentation awaiting reconciliation. The [documentation index](docs/README.md) separates current guides, historical reports and product ideas.
