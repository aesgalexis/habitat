# HABITAT desktop privacy notes

This describes the implementation at version 0.10.0 for the packaging and future code-signing review. It complements the [Chrome extension privacy policy](EXTENSION_PRIVACY.md). HABITAT has no developer-operated cloud backend, advertising or analytics service. Connected providers and external applications process data under their own policies and settings.

## Local data

- Tasks, their recovery history, preferences, window placement, remembered project folders and the identities of Codex chats started through HABITAT are stored in the Windows user's HABITAT data directories. The normal desktop location is `%LOCALAPPDATA%\HABITAT`; packaged execution can resolve a separate local directory, which is why the desktop installer must run in the normal Windows session.
- Gmail client configuration and tokens are protected for the Windows account using Windows Credential Manager; legacy encrypted local storage can be migrated. They are not included in releases or sent to the developer.
- Conversation, search, translation, Gmail and terminal views keep working state in memory. Chrome, ChatGPT, Gmail, Codex and VS Code can retain their own content and history independently. Clearing a HABITAT view does not delete content held by those applications or services.
- Local diagnostics can include timestamps, exception types, operating-system error codes, startup errors and integration status. Crash diagnostics can contain exception details. Do not publish logs without reviewing them for sensitive information.

## Connections

| Feature | Data and destination | When |
| --- | --- | --- |
| Application updates | Release metadata is requested from `api.github.com`. After approval, the matching package and checksum are downloaded from GitHub and its release CDN. No task, conversation or account content is sent. | Automatically at startup and every six hours; failed checks retry after fifteen minutes. Download and installation require **Update and restart**. |
| ChatGPT, Google, YouTube and Translate | Submitted text, relevant page information and results travel through the opted-in Chrome extension and the selected provider. The pairing key and results pass through local HTTP/WebSocket on `127.0.0.1:8765`; that local hop is not encrypted. | Extension connection requires consent. A user's submitted action drives provider work. Linked page state and connection heartbeats continue while connected. |
| YouTube thumbnails | Video thumbnail requests go directly to `i.ytimg.com`. The provider receives the requested image address and normal connection metadata. | When result rows become visible in HABITAT. |
| Weather | A saved/fallback city is resolved through Open-Meteo geocoding; forecast requests send coordinates, rounded to three decimal places. Windows location access can supply device coordinates after its permission prompt. | Automatically at startup and approximately every 15 minutes while the app runs. Device-location denial can fall back to the saved city; disabling device location does not disable weather networking. |
| Gmail | The configured OAuth client signs in through Google. Authorized requests go to Google's OAuth and Gmail endpoints to read mail or save a draft. | After the user configures and authorizes Gmail. Authorized mailbox refreshes occur automatically; draft creation is a user action and does not send the email. |
| Codex | Instructions and eligible chat text pass to/from the locally installed Codex desktop application. | When the user selects and uses the integration. Codex controls its own service connections. |
| VS Code | Project context, submitted commands and terminal output pass through the local authenticated integration. | When configured and used; commands can access networks or files according to what the user runs. |
| External links, Maps, Notepad, Terminal and Run | Chrome or the selected local Windows tool receives the submitted URL, text or command. | On the corresponding user action. External programs control their own subsequent access. |
| Ctrl+H | Selected text can be captured through Windows accessibility or a temporary clipboard operation, then inserted into HABITAT. | On the shortcut, when enabled. It does not send the text automatically. |

The maintainer does not receive these requests or their content through HABITAT. Websites receive normal network metadata such as IP address and apply their own account, consent and retention rules. See the privacy policies of [OpenAI](https://openai.com/policies/privacy-policy/), [Google](https://policies.google.com/privacy) and [Open-Meteo](https://open-meteo.com/en/terms).

## Controls and removal

The Chrome extension can be disconnected from its popup. Gmail can be disconnected in the app and its Google authorization can be revoked. Location preferences control city/device-location selection; Windows controls location permission. Close HABITAT to stop its weather requests. The global shortcut can be disabled in Preferences. Remove the Chrome and VS Code extensions separately if no longer wanted.

Uninstalling the desktop app intentionally retains local user data. External-provider accounts, saved emails, browser history and remote conversations are not deleted by desktop uninstall. Local logs and task files should not be attached to public support issues without review.

## Packaging work still required

Before applying to SignPath Foundation, review all network behavior against its current requirements. In particular, add an installation/first-run privacy disclosure and a clear choice to disable automatic weather networking; the current device-location toggle is not that choice. Confirm the consent and disable/revoke paths for other integrations and document third-party components. These are prerequisites in the [signing plan](SIGNING.md), not completed compliance claims.
