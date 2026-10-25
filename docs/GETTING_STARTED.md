# Set up HABITAT on a new PC

HABITAT runs on Windows 10 2004 or later and Windows 11, on an x64 PC. The desktop packages include .NET and Windows App SDK. You do not need an SDK, Python, Node.js or administrator access to run the app.

## Install the desktop app

1. Open [GitHub Releases](https://github.com/aesgalexis/habitat/releases/latest) and download the Windows x64 setup EXE and its `.sha256` file. Use the release assets, rather than GitHub's Source code ZIP.
2. Check the download with `Get-FileHash .\HABITAT-0.16.2-x64-setup.exe -Algorithm SHA256` and compare it with the hash file.
3. Run the setup EXE from your normal Windows account. It installs in `%LOCALAPPDATA%\Programs\HABITAT`, adds a Start menu entry and registers an uninstaller. The optional desktop shortcut is off by default.
4. Open HABITAT. The setup guide appears once and is always available from **HABITAT menu > Set up HABITAT…**.

The desktop app currently has no code-signing certificate. Windows may display an unknown publisher warning. Only use the release downloaded from this repository and verify its hash.

For the ZIP alternative, download `HABITAT-0.16.2-win-x64.zip`, extract **all** its files to a separate folder and run `Install-HABITAT.cmd`. It uses the same default installation directory. Use the same package format for updates: EXE and ZIP installations cannot overwrite each other. To switch formats, close HABITAT, uninstall or remove its program files, then install the other format; user data is separate.

## Connect Chrome

1. Install [Google Chrome](https://www.google.com/chrome/) 120 or later if needed. HABITAT opens web destinations in Chrome regardless of the Windows default browser.
2. Open [HABITAT · browser bridge in Chrome Web Store](https://chromewebstore.google.com/detail/ennnldolgdbbkgmpjjpkafeajihpimii), choose **Add to Chrome** and approve the permissions.
3. Choose **Enable connection** on the extension's welcome page. Keep Chrome and HABITAT running. Pairing and reconnection are automatic; no API key is needed.
4. Return to **Set up HABITAT…**. Google, YouTube and Translate each show **Connected** when their channel is ready. They do not need a ChatGPT account.
5. For ChatGPT, sign in at [chatgpt.com](https://chatgpt.com/) in Chrome and open the conversation you want HABITAT to use. The guide then shows **ChatGPT connected**. Your ChatGPT account and plan remain separate from HABITAT.

The store submission is pending Google's review as of 19 October 2026. The unlisted link becomes available after approval. Chrome requires the user's approval; installing the desktop app does not silently install its extension.

If the listing is unavailable, use the bundled extension:

1. In the HABITAT guide, choose **Open extension folder**. Copy that folder's path.
2. Choose **Open Chrome extensions**, enable **Developer mode**, select **Load unpacked**, and select that folder.
3. Open the extension and choose **Connect automatically**, or **Enable connection** if its welcome page opened.

The EXE includes `browser-extension` next to `HABITAT.exe`. The ZIP includes `extension` at the installation root. Always load the installed folder, so it remains available after deleting the download. When the store version is available, remove the unpacked copy before installing it to avoid two bridge extensions.

If a provider requests consent or verification, complete it in its Chrome tab and retry in HABITAT. If the guide still shows Waiting, check that the extension is enabled in the same Chrome profile. Open its popup and choose **Connect automatically**. **Pairing code…** is available in HABITAT's menu for manual recovery. Do not share that code.

## Optional integrations

- **Tasks, Notepad, Terminal and Run:** available without the Chrome extension. Notepad uses the Windows app; Terminal and Run execute locally.
- **VS Code:** install VS Code first. In the HABITAT guide, choose **Open VS Code extension folder**. In VS Code, select **Extensions > … > Install from VSIX…**, select `habitat-terminal-0.2.0.vsix`, then run **Developer: Reload Window** in open windows. This is installed once per VS Code profile, outside your projects. VS Code keeps its normal workspace trust checks.
- **Codex:** install the Codex desktop app and sign in separately. HABITAT uses its local desktop integration.
- **Gmail:** choose **Set up Gmail…** in the guide. Enable Gmail API in your own Google Cloud project, configure its OAuth consent screen, add your account as a test user if needed, and create an OAuth client of type **Desktop app**. Import its JSON into HABITAT and sign in through Google. Read access and draft creation have separate authorizations. No account credentials or OAuth client are bundled in the download.
- **Weather:** choose your city or device location in **Preferences… > Location**. Windows may request location permission.
- **Ctrl+H:** enabled by default. Change it in **Preferences… > Shortcuts**. It activates HABITAT and captures selected text; it does not send it automatically.

## Update and uninstall

HABITAT checks GitHub for a newer stable Windows release at startup and every six hours while open. When one is available, click **Update HABITAT** at the bottom of the sidebar to download, verify, install and reopen the app. It keeps your existing EXE or ZIP format. You can cancel the download; active actions finish before closing, and unsent drafts are restored after restarting. Versions before 0.15.0 need one manual upgrade to gain this feature. Failed checks retry after one hour without interrupting your work. See [in-app updates](AUTO_UPDATES.md).

Install a newer EXE over the EXE installation, or run the newer ZIP installer over the ZIP installation. Local tasks, preferences and Gmail credentials stay in your Windows account. Reload an unpacked Chrome extension after updating it; the store edition receives Chrome's normal extension updates. Update the VSIX if its version changes.

Uninstall the EXE through **Windows Settings > Apps > Installed apps > HABITAT**. For the ZIP, first disable the global shortcut in Preferences, close HABITAT, then remove its program folder and Start menu shortcut. Remove the Chrome and VS Code extensions separately if you no longer want them. Uninstalling the desktop app retains your user data.

For help, [open an issue](https://github.com/aesgalexis/habitat/issues) with the app version and the connection states in the setup guide. Do not attach credentials, pairing codes, private conversations or your task files.
