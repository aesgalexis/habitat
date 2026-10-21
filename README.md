# HABITAT — WinUI 3

**English** | [Español](README.es.md)

[Download HABITAT for Windows x64](https://github.com/aesgalexis/habitat/releases/latest) · [First-time setup guide (Spanish)](docs/GETTING_STARTED.md) · [Changelog](CHANGELOG.md)

A native Windows desktop application that lets you write to the active ChatGPT conversation in Chrome and receive its replies inside HABITAT. It does not create separate ChatGPT chats, rotate conversations, or rename them. It uses your actual Chrome session, without calling the OpenAI API. A reply in progress finishes in its original chat before HABITAT follows the active chat.

The current application is built with **C# and WinUI 3 on .NET 10**. It includes eleven destinations, configurable appearance, persistent tasks, weather forecasts, Gmail, a [Markets sidebar widget](docs/MARKETS.md), and the Ctrl+H global shortcut. The Python/Tkinter prototype belongs to an earlier stage; the current entry point is `HABITAT.vbs`.

## First-time installation

WinGet distribution is being prepared. The installer, manifests, and publishing process are documented in [docs/WINGET.md](docs/WINGET.md). HABITAT is not yet available in the public WinGet catalog. The package submission is open at [microsoft/winget-pkgs#455544](https://github.com/microsoft/winget-pkgs/pull/455544). While it is under review, you can download the installer directly from GitHub Releases.

1. Download `HABITAT-0.14.0-x64-setup.exe` and its SHA-256 from [GitHub Releases](https://github.com/aesgalexis/habitat/releases/latest), verify the hash, and run the installer from your normal Windows session. Use the release assets; the **Source code** ZIP does not contain the compiled application.
2. The installer installs to `%LOCALAPPDATA%\Programs\HABITAT`, adds HABITAT to the Start menu, and registers its uninstaller. It includes .NET and Windows App SDK; no SDK or administrator privileges are required. Supports Windows x64, Windows 10 version 2004 or later, and Windows 11.
3. Open **Set up HABITAT…** (shown on first launch and always available in the menu). It detects Chrome, opens the extension in the [Chrome Web Store](https://chromewebstore.google.com/detail/ennnldolgdbbkgmpjjpkafeajihpimii), and shows each connection's status. Approve installation in Chrome and click **Enable connection**.
4. At the time of this release, extension 0.9.0 has been submitted as Unlisted and is awaiting Google's review. If the link is not available yet, the setup wizard opens the included folder and `chrome://extensions` so you can load it unpacked. Chrome is not installed and extensions are not added silently.
5. For ChatGPT, sign in and open a conversation in Chrome. Google, YouTube, and Translate do not require a ChatGPT account. Gmail, VS Code, and Codex are optional; the wizard explains their setup.

The package includes browser bridge **0.9.0** and the **HABITAT Terminal 0.2.0** VSIX. The installer is not yet code-signed. See the [guide for another PC](docs/GETTING_STARTED.md) and [distribution details](docs/RELEASING.md). To work from source, clone the repository and follow [Build](#build).

Alternatively, extract all of `HABITAT-0.14.0-win-x64.zip` and run `Install-HABITAT.cmd`. Always use the same format when updating: EXE and ZIP installations use different layouts and refuse to overwrite each other. Both preserve user data; the ZIP also keeps the previous release for recovery. WinGet preparation is documented in [WINGET.md](docs/WINGET.md).

HABITAT checks for newer stable Windows releases at startup and every six hours while open. When an update is available, an **Update HABITAT** icon appears at the bottom of the sidebar and opens its official download page. Downloading and installing remain manual. Failed checks retry after one hour.

## Run the application

When you switch to another application or the desktop, HABITAT stays visible and lets clicks, scrolling, and drags reach whatever is underneath. You can move or drop a desktop icon within the area occupied by its window. To write in HABITAT again, activate it from the taskbar, Alt+Tab, or Ctrl+H if the global shortcut is enabled.

1. Open `HABITAT.vbs`. It starts the application without showing a console.
2. In Chrome, select the ChatGPT tab and conversation you want to use.
3. If the extension is not installed yet, load `extension` from `chrome://extensions` using **Developer mode → Load unpacked**.
4. Wait until HABITAT shows **Extension connected**.

The extension pairs again automatically when HABITAT changes its key on restart. The manual code remains available as a recovery option in the three-line menu on the top bar, next to minimize.

Chrome can remain in the background, minimized, or on another tab. The extension keeps the last linked chat when you select a tab on another site, and reconnects to that same chat even in the background. It uses a local WebSocket from its background process (Chrome 120 or later), rather than timers in a hidden page. Connection checks continue while ChatGPT is replying. The linked tab is temporarily excluded from Chrome's automatic tab discarding; its previous setting is restored when HABITAT disconnects. Global settings, profiles, and cookies are not changed.

If Chrome has already discarded or completely frozen that tab, HABITAT reports it and needs you to open the tab once to resume; it does not reload it on its own or erase a possible draft. Closing Chrome or suspending Windows also interrupts the connection. When you select another ChatGPT chat, the link follows it between requests; an already queued message or reply in progress stays in its original chat.

In the editor, Enter sends the message and Shift+Enter inserts a line break. Text replies return to HABITAT and also remain in the original ChatGPT conversation.

The destinations appear separately on the right side of the window, vertically centered: **OpenAI**, **Google**, **YouTube**, **Notepad**, **Translate**, **Gmail**, **Google Maps**, **Codex**, **VS Code**, **Terminal**, and **Run**. Select one by clicking or using **the mouse wheel over the selector area**, including the gaps between options, without holding Ctrl. **Ctrl + mouse wheel** also works anywhere in the window while HABITAT has focus. Scrolling up moves backward through the circular list; scrolling down moves forward. The active destination is highlighted and saved. Switching preserves the draft and updates the prompt and button action. Each mode's prompt appears 16 pixels above the empty editor, both in the center of the window and in the bottom editor during a conversation. It disappears when you type.

Every destination starts with the same layout: an action prompt above the editor, a centered editor with a cursor, and an action/send button underneath. Only the destination labels change. Usage explanations, results, and result actions do not appear until an action runs. For Gmail, these are **Draft a new email…**, the cursor, and **Draft email**. **Clear** restores that initial screen and focuses the editor.

An unsubmitted draft follows you between modes. When an action starts, that text becomes associated with the mode used and no longer appears in the others. Translate keeps its original text and result when you return; successful sends and external launches continue to clear the submitted text. A new draft can move between unused modes without replacing another mode's own text. Clearing text that has been used lets you start another shared draft if none is pending. A pending action never clears a newer revision, even if the text is identical. Canceling Terminal's confirmation or rejecting a send before it starts preserves the shared draft; a later failure keeps the text in its mode for retrying.

**Codex** shows messages and replies only from chats started through HABITAT. It saves their identifiers in `%LOCALAPPDATA%\HABITAT\codex-chats.json` to preserve this restriction after restarting. It does not import or continue conversations created directly in Codex: if one is visible, HABITAT shows its normal starting screen, with **Write an instruction for Codex…**, the centered editor, and **Send to Codex**, without notices or a conversation panel; the next send opens a new chat. When you switch conversations in Codex, HABITAT follows the selection only if its identifier is registered; a pending reply stays in its original chat and never appears in another one. Older chats without a recorded origin are also ignored. Its history is independent of **OpenAI**. **Open in Codex** opens the linked chat and **Reload chat** refreshes it. If Codex is on its home screen or closed, the next message creates a new chat. With multiple windows, activate the one you want to use once. Chats with duplicate titles must be renamed so they can be identified unambiguously.

The **Send to Codex** button changes to **Sending to Codex…** during delivery and **Waiting for Codex…** while awaiting a reply. HABITAT verifies the selection, editor, complete instruction, and focus before pressing Enter exactly once. It preserves spaces, Unicode, line breaks, and literal Markdown. It protects drafts written directly in Codex and avoids sending while a turn is running. It clears its editor only after confirming delivery; if confirmation is lost, it keeps the text and asks you to check Codex before retrying. Editing during preparation cancels that attempt without erasing a later revision. Editing after confirmed delivery keeps the reply wait active. **Stop waiting** stops the local wait; the turn remains under Codex's control. Reading uses the local server included in Codex, with query operations only: it does not resume chats or start additional turns. The last twenty turns are shown, including user messages and final text replies. Tools, files, and approval requests are viewed in Codex.

Normal checks use a synthetic desktop and a simulated JSON-RPC server. **--codex-ui-only** verifies the interface, chat switching, late replies, and separation from OpenAI history using isolated data. The explicit **--codex-conversation-smoke** test in the WinUI build sends two synthetic messages to a real new chat and checks that HABITAT retrieves both replies and preserves context. **--codex-desktop-smoke** and **--codex-multiline** retain the specific literal-delivery check. These live tests request replies with markers, without tools or file changes.

**OpenAI** keeps its usual sending behavior. **Google** and **YouTube** show search results inside HABITAT. **Notepad** reuses an open window (even if minimized) and creates a new unnamed tab in it. It starts the application only if no Notepad window is open. It inserts the full content, including spaces, Unicode, and line breaks, without creating or saving files. The tab is created using the native button identified through accessibility, without relying on shortcuts that vary by Notepad language (for example, Ctrl+N toggles bold in Spanish). On a classic version without tabs, it opens another window to preserve the previous document. HABITAT's editor is cleared only after verifying that Notepad received the text. **Gmail** saves a draft directly in the connected account when you click **Draft email**, without opening the browser. It uses the first eight words as the subject, preserves the full body and the text in the editor, leaves the recipient blank, and does not send it. **Clear** clears the editor without deleting saved drafts; **Open in browser** opens the last draft or the drafts folder without creating another. With read-only access, **Authorize Gmail drafts** lets you authorize saving once through Google; then click **Draft email** again. **Google Maps** searches for places or addresses in Chrome. If you enter a route, such as `distancia de Madrid a Barcelona`, `distancia entre Palma de Mallorca y Sóller`, `ruta de Sevilla a Cádiz`, or `cómo llegar de Valencia a Alicante`, it opens directions with the origin and destination so you can see the route, distance, and time in Google Maps. It also recognizes `de Madrid a Barcelona`, `desde Madrid hasta Barcelona`, and their English equivalents (`distance from London to Oxford`, `directions from London to Oxford`). It supports names, addresses, and coordinates; searches without a complete route preserve the original text. The editor prompt is **Search places or get directions…**. **Terminal** shows **Run PowerShell command**, with the full command and working directory (your Windows home folder). Only **Run command** executes it in a new PowerShell window, which stays open to show the output. **Cancel** is the default option and keeps the text without executing anything. Enter or the button runs the action and clears the editor after a successful launch; failures preserve the draft for retrying. External destinations do not add results to history or require the extension connection. Google, YouTube, and Translate use their integrated sections and the extension.

**Translate** keeps the usual writing view until you press Enter or **Translate**. The first submission translates into **English** and shows the result and **Translate to** selector. Only **English**, **Greek**, and **Spanish** are available; each new session starts in English. The original stays in the editor, and the **Copy translation** icon below the delivered translation copies only the result. **Open in browser** opens Google Translate with the submitted original and selected language. **Clear translation** cancels the wait, clears the original and result, and restores the centered starting editor; it keeps the selected language. You can switch modes during a translation and retrieve it when you return; OpenAI history stays separate. **Cancel** stops the wait and **Retry** repeats the last text after a failure. Changing the language discards earlier replies; click **Translate** again to translate into the chosen language.

Translation uses the actual Google Translate page in Chrome through the **HABITAT · browser bridge** extension (version 0.5.3 or later). It reuses an open translator tab, including one opened manually or in another Chrome window, keeps only the translation page active through focus emulation, fills in the text, and returns the result to HABITAT. It does not select the tab, activate or restore the window, or change its minimized state. Focus emulation is released before returning the result, including on cancellation or failure. It creates a tab only if none is available and reuses it for later translations, even after losing its session identifier. Overlapping jobs share the same tab. It does not replace other user pages or send messages to ChatGPT. No translation API, embedded browser, or global clipboard is used to transport the result. Chrome must stay open, and the extension needs access to `translate.google.com` and the `debugger` permission to keep that page active without showing it. Automation attaches only to the translator tab during each job; Chrome may show its usual debugging notice if you open the browser. If that tab's DevTools is open, close it before translating. Reload the extension from `chrome://extensions` after updating its files. If Google shows consent or another blocking screen, open its tab and complete it before retrying. The page's 5,000-character limit applies. The desktop application limits the wait to 55 seconds; **Cancel** releases the interface immediately, even if the browser does not respond. **Copy** is the only action that writes the result to the system clipboard.

**Text-only replies** is enabled by default. It adds a visible instruction to each send asking ChatGPT to use plain text and avoid tables, images, files, and other rich content unless the message explicitly requests them. You can disable it in **Preferences…**, and the setting is saved.

To bound memory usage, the local OpenAI view keeps up to 200 messages within a 2,000,000-character budget; the last message is kept in full. If older messages are removed, it shows **Earlier messages remain available in ChatGPT.** The original conversation remains complete in Chrome. Codex keeps the last twenty turns per chat and up to 32 visited chats in memory, protecting the selected chat and any pending reply; the others are queried again when opened.

## Run

**Run**, at the end of the selector below **Terminal**, opens Windows programs and commands such as `calc`, `msconfig`, `control`, or `services.msc`. Type the command and press Enter or **Run**. The editor prompt is **Run a program or a command…**. It supports arguments, quoted program paths, and variables such as `%USERPROFILE%`; it also opens existing files and folders. It uses Windows shell launching from your home folder, without adding a console. Windows may request its usual permissions if the tool requires them. An accepted launch clears the editor; on failure it keeps the command and shows **Could not run this command. Check the name or path and try again.** Only one line per submission is supported. It does not require the browser extension or add messages to the chat.

## VS Code

**VS Code** shows a compact **Project** selector and folder path above the editor from the start; the editor stays centered until the first submission. By default, it follows the active editor's folder in the last VS Code window that had focus. **Choose folder…**, inside the selector, lets you select any local folder even if it is not open in VS Code. HABITAT remembers up to 20 folders and keeps your manual selection when switching destinations. Choosing a folder does not execute anything. Pressing Enter or **Run in VS Code** runs the command directly without a confirmation dialog. It opens the project in VS Code if needed and waits up to 30 seconds for its connection before creating the terminal. It reuses connected windows and never replaces the selected folder with another. Accepted delivery clears the draft; failure preserves it. The section shows notices only when there is a problem.

Install the extension once in VS Code: projects need no added code, dependencies, or configuration files. Remembered folders are stored in HABITAT's preferences. VS Code keeps its usual trust check when opening a new project.

You can run `npm run dev`, `npm run site:publish`, or other commands available in the project. It uses VS Code's default shell, and each submission creates another terminal without reusing or stopping earlier ones. HABITAT shows results only for the selected folder: switching projects hides the previous output, and returning restores its updated history. Commands in other projects remain monitored in the background. A folder without submissions keeps the editor centered. Each result shows **Starting**, **Running**, **Completed**, or **Failed**, its duration, and its exit code on failure. The progress indicator stays active while the command runs without blocking new submissions. A development server remains **Running** even after printing its URL. The result includes the latest lines and HTTP/HTTPS links from the output; **Show output** expands the captured text and **Hide output** collapses it. Capture is limited to the last 65,536 characters per command; exceeding this shows **Output truncated**. Full output remains in VS Code.

Tracking requires **HABITAT Terminal 0.2.0** and VS Code shell integration. If unavailable, if no exit code is received, or if the connection is lost, it shows **Status unavailable**, without assuming success or repeating the command. Closing the terminal shows **Closed** when no completion was confirmed. If delivery confirmation is lost, check the terminal before sending again. Only trusted local folders are supported; an empty or remote window is not automatically replaced with a previous project. Output travels only through the local bridge and is not sent to an AI service.

The integration needs the local **HABITAT Terminal** extension, separate from Chrome's extension. It is packaged without npm dependencies:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-vscode-extension.ps1
code --install-extension artifacts\vscode\habitat-terminal-0.2.0.vsix --force
```

After installation, reload open VS Code windows with **Developer: Reload Window**. `node tests/vscode-extension.checks.cjs` and the destination suite verify new terminals, exact text, multiple projects, context switching, trust, local authentication, and the absence of automatic retries. WinUI controls are tested with synthetic data in `scripts\check-tasks-ui.ps1`, including the initial centered writing view and the compact section without explanations or auxiliary buttons.

## Appearance and writing

**Preferences… → Appearance → Wallpaper** offers three color backgrounds, five included illustrations (Graphite, Dunes, Tide, Olive and Orbit) and a library of your own JPG/PNG images, with a preview, fill/fit and attenuation. **Restore original · Hybrid, 15%** returns to the default transparent appearance. See [wallpaper behavior and storage](docs/WALLPAPERS.md).

Choose **English** or **Español** in **Preferences… → Appearance → Interface language**. The language applies when you close Preferences and persists between runs. Drafts, tasks, results and the translation target are preserved; existing installations keep English. See [interface localization](docs/LOCALIZATION.md).

**About HABITAT…**, at the bottom of the menu, shows the installed version, platform, author, MIT license, project and support links, privacy information, and credits. **Copy app details** copies the version and system details for issue reports.

In **Preferences…**, choose **Day**, **Night**, or **Hybrid** and adjust background opacity. The default is **Hybrid with 15% opacity**. Hybrid adapts contrast to the window background. Appearance, window position, and window size persist between runs; existing saved preferences are preserved.

Preferences are organized using a sidebar with **Appearance**, **Location**, **ChatGPT**, and **Shortcuts**. Each category preserves changes when switching to another; they are saved when **Preferences** closes. As before, the global shortcut takes effect when its switch changes.

Web destinations, search results, Gmail, Google sign-in, and VS Code terminal links always open in Google Chrome, where the extension is installed. There is no browser selector or dependency on Windows' default browser. Application protocols keep their Windows handler. Old browser preferences are ignored without losing other settings.

ChatGPT, Google, YouTube, and Translate inside HABITAT require **HABITAT · browser bridge 0.9.0** in Chrome and its **Enable connection** consent. The connection authenticates with the local key and also works with older packages, without filters tied to a browser preference.

The editor is centered when the selected destination has no content. Once a conversation starts, it moves to the bottom to leave room for text history. OpenAI history appears only in its own destination: switching hides it, including replies arriving while another destination is active, and returning restores it in full. Clicking the window background focuses the editor again.

## Google Search

The **Google** destination shows web results inside HABITAT as a list with clickable titles, addresses, and descriptions, in the order of the retrieved page. **Previous page** and **Next page** move through available pages. The query stays available for editing, and results persist when switching destinations. Links open in Chrome when clicked; **Open in browser** opens the current search. **Open in browser** and **Clear search** appear together in the top-right header, with **Clear search** at the far right. **Cancel** stops the wait, and **Retry** repeats the failed query or page. Error, loading, and zero-result states are in English; titles and snippets keep Google's original language.

Emptying the query resets Google: it clears results, cancels the pending search, restores the centered editor, and closes HABITAT's search tab. **Clear search**, at the top right of the list, does the same and also empties the editor. Switching destinations preserves the search; clearing another destination does not reset it. Only the application's own search tab is closed, even if it shows Google consent or verification; if you have navigated it to another website, it is preserved. Tabs opened by clicking results and other Chrome windows stay open. A pending close is delivered when the extension reconnects and confirmed before another search starts.

Requires Chrome to be open and **HABITAT · browser bridge 0.6.1** or later, with access to `www.google.com`. Reload the extension in `chrome://extensions` after updating its files, and accept search access if Chrome requests it. The extension creates and reuses its own inactive tab for Google's web view; it does not replace personal searches or activate windows or tabs. It uses neither a search API nor WebView, does not send queries to ChatGPT, and does not save queries or results to disk. Google's section is independent of YouTube; Google Maps keeps its existing behavior. Maps, ads, and other special modules are not displayed.

If Google requires consent or verification, complete it in the Chrome tab and click **Retry**. An incompatible page change produces a reading error. Normal checks use synthetic data; `node tests/extension-search.checks.cjs` checks the reader and dedicated tab, and `--search-browser-smoke` verifies the real page with a test query while HABITAT is closed.

## YouTube

The **YouTube** destination shows videos inside HABITAT as a list with thumbnails, clickable titles, and links, including channel, duration, views, age, and description when provided by YouTube. If a channel name or handle matches the query, ignoring case and extra spaces, it appears before the videos on the first page with the **Channel** label and its link; handles can be entered with or without `@`. **Previous page** and **Next page** move through blocks of 20 videos in page order, loading continuation results when needed; channels do not count toward those page positions. Clicking a video's thumbnail or title opens it in the browser; a channel title opens its page. Content is not played inside HABITAT. **Open in browser** opens the full query in YouTube and appears next to **Clear search** at the top right, using the same layout as Google.

Thumbnails are 320 × 180 JPEGs, loaded directly from YouTube's CDN when they enter the viewport and displayed in an area up to 160 × 90. Native controls are used, without extra packages or images traveling through the extension bridge. Clearing, changing pages, or leaving YouTube releases that view's image references; if an image fails, its space keeps an icon and the video remains available.

The query, results, and tab are independent of Google. Consecutive queries update the same search tab in the same window. When the editor is emptied, YouTube waits 750 ms before resetting so you can replace a query without closing the tab between them; starting to type another cancels that close. **Clear search**, at the top right, resets immediately: it cancels the search, clears the list, centers the editor, and closes the dedicated results tab. It preserves personal tabs and tabs opened to watch videos. **Cancel** and **Retry** stop or recover a search; switching destinations preserves its state.

Requires Chrome to be open and **HABITAT · browser bridge 0.7.0** or later, with access to `www.youtube.com`. Reload the extension from `chrome://extensions` after updating it. Search uses its own background tab and page focus emulation through the existing `debugger` permission; it does not select tabs, restore windows, or change Windows focus. The site may show consent or verification: complete it in Chrome and click **Retry**. If the tab's DevTools is open, close it to allow background reading. Search does not use the YouTube API, an embedded player, or ChatGPT. Queries and results are not saved to disk.

`node tests/extension-youtube.checks.cjs` verifies the reader, continuation, and closing using synthetic pages. The optional `--youtube-browser-smoke` test, with HABITAT closed, checks the real page using a test query, including the second page and reset, and closes the tab afterward.

## Weather and location

Weather appears at the very top of the left sidebar, above Gmail and Tasks, without a frame. With the sidebar collapsed, it shows the icon with the temperature in °C underneath; expanding adds the location and rain forecast for the next six hours. It keeps its position when changing state, and the task list opens below all three widgets. The main content keeps a 100 px clear strip at the top, matching the space reserved by the former standalone widget. **Use current location (GPS / Windows)** is enabled by default in Preferences: it uses the Windows location service, with GPS when available. It requests permission from HABITAT and updates the position along with the weather every 15 minutes, without continuous tracking. Coordinates are rounded to three decimal places and sent to [Open-Meteo](https://open-meteo.com/en/docs) to retrieve the forecast; they are not saved to disk or sent to the chat.

If Windows cannot obtain a position or denies access, HABITAT uses the saved city (initially `Palma, ES`) and shows **saved city**. The widget has no hover tooltips. In **Preferences…**, you can disable automatic location, change **Fallback city / Weather location**, or open **Windows location settings**. In Windows: **Settings → Privacy & security → Location → Location services**, including permission for desktop applications. The displayed percentage is the highest hourly precipitation probability over the next six hours. Connection failures are reported and retried automatically.

The **Open in new window** icon appears only to the left of the selected destination's bar. It runs that destination's action using the editor's current text, even before the main button has been pressed, and shows the external window: Gmail opens a draft without sending it; Google and YouTube open the search; Maps preserves routes; Translate opens Google Translate with the selected language and keeps the original. Notepad, Codex, Terminal, and Run use their usual launchers. Terminal keeps its confirmation step; VS Code runs directly in the selected project. With an empty editor, it opens a home page, new document or draft, empty shell, or Run dialog. VS Code opens the selected project; without a project, it opens the application. Help labels describe the specific action in English. Both buttons prevent duplicate actions during delivery, and a failure preserves the draft; a later revision is never cleared by an earlier delivery.

**Send and open ChatGPT** requires browser extension **0.7.1** or later: it selects the linked conversation and restores its window before sending once. The main button keeps sending in the background. Without text, the icon only opens that conversation; without a browser connection, it opens ChatGPT's home page. **Run command and open VS Code** requires **HABITAT Terminal 0.1.1** or later to show the exact window after delivering the command. If opening fails after accepted delivery, it reports the problem and does not repeat the command. Reload extensions after updating them.

## Personal destinations

The window status updates at startup and when switching destinations. Idle destinations show **Ready**. ChatGPT bridge connection and progress messages appear in OpenAI; background replies remain in its history without replacing another destination's status.

Use **Preferences… → Destinations** to add, remove and reorder destinations. **On startup** selects a fixed destination or **Last used destination**. The mouse wheel cycles only through your selected destinations. **Remove** preserves settings and session content; restore a destination with **Available destinations → Add destination**. Removing every destination leaves an **Add destination** action that opens these preferences and keeps your draft. New catalog entries stay optional.

**Add Windows application…** lists installed Windows applications, including Microsoft Store apps. **Browse…** also accepts executable files (`.exe`) and shortcuts (`.lnk`). Adding an application never launches it. Changes apply when Preferences closes and persist across restarts.

By default, **Open [application]** opens the application, including from an empty editor, and keeps your draft. Adding Paint provides a launcher; it does not insert text or draw automatically. For executables that support text arguments, enable **Pass editor text as one argument**. HABITAT passes the complete text as a single literal argument and clears only the submitted draft after Windows accepts the launch. Acceptance does not confirm that the application processed the text. Store applications and shortcuts open without text arguments. Launch failures keep the draft.

Stable destination IDs migrate previous numeric preferences. Destination checks use synthetic application files and intercepted launchers. The explicit `--windows-apps-smoke` option in `tests/Habitat.Destination.Checks` verifies installed app discovery without launching applications. **--preferences-ui-only** checks management, persistence, Windows app creation and the common initial editor layout with isolated storage under `artifacts`.

## Tasks

The widgets form a left sidebar: **Weather** at the top, **Gmail** in the middle, and **Tasks** below, all with the same width. The sidebar starts collapsed to an icon strip. Clicking its empty background shows previews; starting a conversation collapses it again to make room for chat. Returning to a destination without a conversation keeps it collapsed by default. Narrow windows also use this compact mode. Clicking the sidebar's empty background manually collapses or expands it, including during a conversation. Widget icons and controls keep their own actions; there is no extra button. The collapsed strip is 40 px wide; its icons are centered and keep exactly the same horizontal and vertical positions when expanded. The outer left margin is 12 px in both states. Expanded widgets keep their compact layout, with an icon on the left and text on the right inside uniform margins; the sidebar's left edge stays in place when its state changes. Clicking the Tasks widget expands the creation field and list directly over HABITAT's background, always below the three widgets, including when opened from the icon with the sidebar collapsed or when changing sidebar state with the list open. The panel reserves space to avoid overlapping chat; in very narrow windows it temporarily hides the chat and restores it when the list closes. Add tasks with **+** or Enter, mark them complete, and restore them by unchecking their boxes in the collapsible **Completed** section. With the list closed, the preview rotates through pending tasks every 8 seconds; it pauses when hovered, when the widget has focus, or when the list opens. Clicking the widget again collapses the list. It also opens from **☰ → Tasks…**.

The list is saved automatically to `%LOCALAPPDATA%\HABITAT\tasks.json` and persists across restarts and updates. Every change keeps verified copies in `tasks.json.history`. An index with a checksum speeds up subsequent opens; if the history changes or the index is damaged, original revisions are verified again. No revision is deleted. Reads and saves are serialized off the UI thread. If the main file is missing, damaged, or missing tasks present in history, tasks and their states are recovered from backups and a notice appears; the previous file is preserved. An older session cannot overwrite newer changes or delete tasks: **Reload tasks** is offered and the draft is kept. Read or save errors block further changes until reloading. HABITAT prevents use of a temporary list when launched inside the Codex sandbox: open the application from its Windows shortcut. Updates run outside that sandbox. Weather stays above Gmail in the sidebar, including while the task list is open.

Storage and distribution checks run with `dotnet run --project tests\Habitat.Tasks.Checks`.

`powershell -ExecutionPolicy Bypass -File scripts\check-tasks-ui.ps1` uses real WinUI controls to check the panel integrated into the background, creation-field focus, expansion of completed tasks in light and dark themes, and reopening. It uses synthetic tasks and an isolated file under `artifacts`, opens and closes its own window, and never reads or changes user tasks or connects email or chat. It also checks saving both lists, preserving drafts on conflicts, starting with the sidebar collapsed, and showing OpenAI history and late replies only in their own destination. This test build is generated separately from the normal executable.

## Gmail

The sidebar's Gmail widget shows the unread count and three lines containing the sender, subject, and email snippet. It rotates through loaded messages every **8 seconds**, pausing when hovered, when the message has keyboard focus, or when the reader opens. During a conversation or in narrow windows, it collapses to the icon; the reader opens to the right of the sidebar.

- **Icon and count:** open HABITAT's reader, also available from **☰ → Gmail…**. It lets you read text without marking messages as read, choose **Inbox unread** or **All unread**, and load more pages.
- **Sender, subject, and snippet:** open Gmail in Chrome in the connected account, searching for that email by its original identifier. If unavailable, the conversation link is used.

Both areas have their own hover outline, without tooltips. The list refreshes every minute, and rotation adds no Google requests. Each page loads up to 25 messages. The **All unread** count is an estimate and is displayed with `≈`.

The connection requires a Google OAuth client of type **Desktop app** with Gmail API enabled. Standard use is free within Google's quotas; you do not need to host a server or pay intermediaries. Keep the project without linked billing.

HABITAT retrieves authorization from Windows Credential Manager on startup and refreshes access automatically. Reimporting the same client preserves the session. In OAuth **Testing** mode, Google expires refresh grants after seven days. **Disconnect** removes the local refresh grant and keeps the client for reconnection. Windows protects the client and refresh token in the `HABITAT/Gmail/DesktopAccount/v1` entry, outside the repository. Older installations automatically migrate their encrypted account file; temporary access tokens stay in memory.

HABITAT does not send, delete, or archive email, or share its content with ChatGPT. Reading inside HABITAT does not mark messages as read; opening them in Gmail follows Gmail's normal behavior. HABITAT's view uses text, without remote images or attachments.

Full instructions are in [docs/GMAIL_SETUP.md](docs/GMAIL_SETUP.md). Checks without a real account run with `dotnet run --project tests\Habitat.Gmail.Checks`.

## Global shortcut

**Ctrl+H** opens or focuses HABITAT and places the selected text in the editor without sending it. **Preferences… → Global shortcut (Ctrl+H)** enables or disables the resident process and its Windows startup entry. Existing drafts require confirmation before replacement. If the selection or clipboard backup cannot be verified safely, it only focuses the application. See [implementation, limits, and tests](docs/GLOBAL_SHORTCUT.md).

## Build

Development requirements:

- Windows 10 version 2004 or later.
- .NET 10 SDK.

Project dependencies are restored from NuGet during the build.

Generate the self-contained x64 build with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-winui.ps1 -Install
```

Run `-Install` from your normal Windows session. The build is prepared in a new directory under `artifacts\staged`, verifies every file's hash and size, and installs to `artifacts\releases`. `HABITAT.vbs` resolves the active release through `artifacts\current-release.txt`; the older installation in `artifacts\win-x64` remains supported. The previous release is kept and automatically restored if startup fails. The application waits for ChatGPT's reply to finish before closing, checking bridge status rather than the window title.

Without `-Install`, the script only prepares and verifies the build; it prints the command to activate it later. It does not overwrite binaries currently in use. This is an unpackaged WinUI 3 application, without Developer Mode or a console. `Habitat.sln` contains the application, shared library, checks, and Gmail utility.

Forecast checks, without network access or additional test dependencies, run with `dotnet run --project tests\Habitat.Weather.Checks`. To check the actual weather service connection for Palma: `dotnet run --project tests\Habitat.Weather.Checks -- --live`.

## Checks

`dotnet run --project tests\Habitat.Destination.Checks --property:BaseOutputPath=C:/proyectos/habitat/artifacts/destination-checks/` verifies circular navigation, search URLs, drafts and Google Maps routes (origin and destination, Spanish and English variants, Unicode, and normal searches), preferences, Notepad, and PowerShell. Translate checks the three languages, English as the default, exact text, browser identifiers and connection, cancellation, and pending consent. `node tests/extension-translation.checks.cjs` uses a synthetic DOM to verify page text insertion, result reading, tab reuse, absence of focus changes, and rejection of unsupported languages. `scripts\check-tasks-ui.ps1` checks the initial view and selector hidden until Enter, native controls without WebView, isolated copying, mode switching, retries, and stale-reply rejection. Normal tests do not contact Google or use user text. The optional `--notepad-smoke` check, run from a normal Windows session, creates two tabs with synthetic text in real Notepad and verifies complete insertion before reporting success. The optional `--translation-browser-smoke` check requires HABITAT to be closed and the Chrome extension updated: it starts a test bridge on port 8765 and translates synthetic phrases into all three languages through the real page. It never creates ChatGPT messages or reads user tasks.

With Node.js and the .NET SDK installed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
```

The shared command runs extension, bridge, core, destination, Gmail, tasks, weather, and appearance checks, checks updates with simulated failures, and builds the entire solution in Release. Its output and task data remain under `artifacts\verification`. `-NoRestore` reuses restored dependencies; `-SkipBuild` skips the final build. It also runs from the push hook and GitHub Actions. WinUI control checks run separately with `scripts\check-tasks-ui.ps1`; they use in-memory credentials, blocked network access and launchers, and explicit test paths.

Verification also includes the Ctrl+H resident process and `tests\package.checks.ps1`: it checks versions and PowerShell syntax, installation to a path with spaces, updates with locked files, preservation of previous releases, and rejection of corruption or overlapping paths. The test installation uses `-NoLaunch -NoShortcuts` under `artifacts`, without opening the application or registering automatic startup.

Transport and Gmail checks use synthetic pages, responses, and accounts, with random local ports where needed. They do not open your conversation or read your real email. Gmail checks include encryption, reopening sessions, automatic refresh, and email-link construction. The local `/status` endpoint, with the pairing key, provides read-only diagnostics: querying it does not create a heartbeat or consume messages.

## Security and limitations

- The server listens exclusively on `127.0.0.1:8765` and requires a new random key on every run.
- Do not publish the pairing code.
- New messages go to the active Chrome chat; select the conversation you want before sending.
- Extension selectors depend on ChatGPT's web interface and may need maintenance when it changes.
- Widgets, files, images, and other complex interfaces are not reproduced in HABITAT; view them in Chrome.
- Do not retry a failed send without checking Chrome first: the message may have arrived even if confirmation did not return.

## Automatic GitHub checkpoints

The versioned hooks in `.githooks` work on the `codex/autosave` branch. Before pushing, they run `scripts/verify.ps1` with all suites, a Release build, and diff review. The `post-commit` hook automatically pushes the commit to `origin/codex/autosave`. `main` contains the integrated state; releases are pinned with `vMAJOR.MINOR.PATCH` tags and GitHub Releases assets.

Enable the hooks in a new clone with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\instalar-autopush.ps1
```

## Documentation and structure

The guides below and the changelog are currently in Spanish. This README is the primary English entry point; the full Spanish version is available in [README.es.md](README.es.md).

- [Releases and distribution](docs/RELEASING.md): SemVer, installation, updates, and automated publishing.
- [Changelog](CHANGELOG.md): contents of each release.
- [WinGet preparation](docs/WINGET.md): EXE installer, manifests, and remaining public-release requirements.
- [Gmail setup](docs/GMAIL_SETUP.md): OAuth, persistent sessions, cost, privacy, and the terminal utility.
- [Product state and direction](docs/PRODUCT_STATE_AND_VISION.md): implemented functionality, user experience, and pending ideas.
- [Internal architecture](docs/ARCHITECTURE.md): composition, state, shutdown, storage, and updates.
- [October 18, 2026 audit](docs/ARCHITECTURE_AUDIT_2026-10-18.md): corrected issues, verification, and prioritized architectural debt.
- [Bridge contract](docs/BRIDGE_PROTOCOL.md): messages, compatibility, and transitions.
- `src/Habitat.App`: window, WinUI controls, composition, and Windows-specific integration.
- `src/Habitat.Core`: models, feature coordinators, services, and storage shared with tests.
- `src/Habitat.Activation` and `src/Habitat.Hotkey`: local activation and the Ctrl+H resident process.
- `extension`: bridge to the ChatGPT session in Chrome.
- `tests`: automated checks without dependencies on real conversations or mailboxes.
- `tools/Habitat.Gmail.Setup`: OAuth import and account checks from the terminal.
- `scripts`: building, update installation, and checkpoints.
