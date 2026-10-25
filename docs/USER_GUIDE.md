# Using HABITAT

This guide describes the source copy inspected on 24 October 2026, with local implementation in progress. See [project status](PROJECT_STATUS.md) for differences from earlier documentation. For installation, start with [GETTING_STARTED.md](GETTING_STARTED.md).

## Write and choose a destination

Select a destination on the right, write in the editor and run its action. **Enter** submits; **Shift+Enter** adds a line break. The mouse wheel over the selector moves through destinations; **Ctrl + mouse wheel** also works elsewhere while HABITAT has focus.

Each destination begins with a prompt above the centered editor and an action button underneath. Results and their actions appear after execution. Clearing returns to the initial editor and restores focus.

An unsubmitted draft can follow you between destinations. Once you run an action, that text belongs to its destination. Switching away preserves its state; an action completing later must not erase a newer draft. Successful sends and external launches clear the submitted text. Translate preserves its original, and Gmail draft creation keeps the body available.

## OpenAI

Keep Chrome running, sign in to ChatGPT and open the conversation you want to use. The browser bridge follows the active ChatGPT conversation between requests; a message or reply already in progress stays in its original chat. Text replies appear in HABITAT and remain in ChatGPT.

Chrome can remain in the background or minimized. If it discarded or froze the linked tab, open that tab once to resume. Suspending Windows or closing Chrome interrupts the connection. Complete provider consent or verification in Chrome, then retry.

**Text-only replies**, enabled by default in **Preferences… → ChatGPT**, adds an instruction asking for plain text unless your message requests rich content. View images, files, interactive widgets and complex tool states in Chrome. The local view has bounded history; **Earlier messages remain available in ChatGPT.** indicates that older messages have been removed from the local view, while the original conversation remains available.

If delivery is uncertain, inspect ChatGPT before sending again. A missing confirmation does not prove the message failed to arrive.

## Google and YouTube

Submit a search to view native lists of results. Google shows titles, addresses and snippets. YouTube shows video results and may include matching channels on the first page. Original provider text retains its language. Clicking a result opens Chrome.

**Previous page** and **Next page** navigate available pages. **Open in browser** opens the current search; **Clear search** removes it and returns to the centered editor. **Cancel** stops the wait and **Retry** repeats a failed request. Emptying the query also resets the search and requests closure of HABITAT's own search tab.

Google and YouTube keep separate results, drafts and pending work. Neither search sends a message to ChatGPT. Provider consent or blocking pages may require attention in Chrome; see [troubleshooting](TROUBLESHOOTING.md).

## Translate

Enter the original and press **Translate**. The first submission uses **English**. Once results appear, **Translate to** offers **English**, **Greek** and **Spanish**. Changing the target discards an earlier result; submit again to translate into the new language.

The original remains in the editor. **Copy translation** copies the result; **Open in browser** opens Google Translate with the submitted original and target. **Clear translation** cancels pending work, clears original and result, and restores the initial editor while keeping the chosen target. **Cancel** and **Retry** handle pending or failed work.

Translation uses the real Google Translate page in Chrome through the extension. Chrome must remain open; the extension may show its normal debugging notice while keeping that page active in the background. It does not select the tab or restore a minimized window. If the page needs consent, complete it there before retrying. The page limit is 5,000 characters and HABITAT limits the wait to 55 seconds. No ChatGPT message is created.

## Gmail

Set up your Google OAuth client and account using [GMAIL_SETUP.md](GMAIL_SETUP.md). Reading mail and saving drafts have separate authorization requirements.

The Gmail widget shows an unread count and rotates loaded sender, subject and snippet text. Icon/count open HABITAT's text reader; message text opens that email in Gmail through Chrome. The reader supports **Inbox unread**, **All unread** and additional pages. Reading locally does not mark messages as read. Remote images and attachments are not rendered.

The Gmail destination begins with **Draft a new email…** and **Draft email**. Submitting saves an unsent draft in the connected account, using the first eight words as the subject and preserving the full body. The recipient stays blank. If prompted, use **Authorize Gmail drafts** and then run the action again.

**Open in browser** opens the last draft or drafts folder without creating another. **Clear** clears the editor without deleting saved drafts. HABITAT does not send the draft or share Gmail contents with ChatGPT.

## Codex

Install and sign in to the Codex desktop app separately. This copy reads conversations registered as created through HABITAT. **Reload chat** refreshes the selection and history; **Open in Codex** lets you inspect its original conversation, tools, files and approval requests.

Sending checks the selected conversation and its editor before delivery. An existing draft or running turn can prevent a send. The editor clears only after confirmed delivery; if confirmation is uncertain, inspect Codex before retrying. With multiple Codex windows, activate the intended one once. Duplicate titles can prevent unambiguous identification.

**Stop waiting** stops HABITAT's local wait. Codex retains control of its turn. HABITAT displays user messages and final text replies from the last twenty turns; its history is separate from OpenAI. Earlier descriptions of scheduling and persistent selection require reconciliation; consult [project status](PROJECT_STATUS.md).

## VS Code

Install the separate **HABITAT Terminal** VSIX, then use **Developer: Reload Window** in open VS Code windows. See [setup](GETTING_STARTED.md) and the [extension README](../vscode-extension/README.md).

The destination uses the selected local project, normally following the active editor's folder in the most recently focused VS Code window. Its section provides a **Project** selector with **Choose folder…** and remembers manually selected folders. Selecting a folder does not run a command.

**Run in VS Code** submits directly, without a confirmation dialog, and creates a fresh integrated terminal using VS Code's default shell. Each submission uses another terminal; existing commands keep running. Only trusted local folders are supported, and VS Code retains its workspace trust checks.

Results are associated with their project. They show status, duration and captured output; **Show output** expands it. A development server remains **Running** while its process continues. Capture is bounded and full output remains in VS Code. If tracking or delivery confirmation is unavailable, inspect the real terminal before retrying; HABITAT does not repeat it automatically.

## Other destinations

| Destination | Action and result |
| --- | --- |
| **Notepad** | Inserts the full text into a new unnamed tab in an existing Notepad window, or opens Notepad if needed. It does not save a file. The editor clears after text delivery is verified. |
| **Google Maps** | Searches places and addresses in Chrome. Complete routes such as `de Madrid a Barcelona` or `directions from London to Oxford` open directions with origin and destination. |
| **Terminal** | Shows **Run PowerShell command** with the command and working directory. **Run command** executes in a new PowerShell window; **Cancel**, the default, preserves the text. |
| **Run** | Opens a Windows program, command, file or folder through the shell, for example `calc` or `control`. Supports arguments, quoted paths and environment variables, with one line per submission. Windows applies its normal permissions. |

These external launches do not require the Chrome bridge, except that web destinations need Chrome installed. Successful launches clear the submitted text; failures preserve it for retrying.

## Tasks, weather and appearance

Tasks are stored locally with recovery history. Keep task text in its original language. Do not use real task files as input for tests; development guidance is in [CHECKS.md](CHECKS.md).

Weather appears at the top of the sidebar. **Preferences… → Location** controls **Use current location (GPS / Windows)** and the saved city used as fallback. Windows may ask for location permission. Forecast requests use Open-Meteo; disabling device location uses the saved city and does not disable weather network requests. See [privacy notes](PRIVACY.md).

**Preferences… → Appearance** offers **Day**, **Night**, **Hybrid** and background opacity. The default is Hybrid at 15% opacity. Appearance and window placement persist. The interface stays in English; this does not change the language of your content or the Translate target. See [LOCALIZATION.md](LOCALIZATION.md).

## Global shortcut, updates and recovery

**Ctrl+H** focuses HABITAT and can import selected text without sending it. Configure it in **Preferences… → Shortcuts**. Replacing an existing draft requires confirmation. If selection or clipboard backup cannot be verified, the shortcut only focuses HABITAT. See [GLOBAL_SHORTCUT.md](GLOBAL_SHORTCUT.md).

Before an update, save important window text and results: they are cleared on restart. Saved tasks, preferences and credentials remain. [UPDATES.md](UPDATES.md) is the reference for detection, confirmation and recovery. For connection failures or uncertain sends, use [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
