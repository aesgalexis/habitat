# HABITAT Browser Bridge privacy policy

Effective date: 26 October 2026. Extension version: 0.10.7.

This policy covers the HABITAT Browser Bridge Chrome extension maintained by [aesgalexis](https://github.com/aesgalexis). The extension connects Chrome to the HABITAT desktop application running on your own Windows computer. It has no HABITAT cloud backend, advertising, analytics or tracking service.

## Your choice

The extension stays disconnected until you select **Enable connection** on its connection and privacy page. After enabling it, requests submitted in HABITAT can be carried out in the supported websites in Chrome. You can select **Disconnect** in the extension popup or **Keep disconnected** on that page at any time. Disabling the connection prevents subsequent jobs; it does not undo text already sent to a website.

## Information used

- **ChatGPT:** messages you submit through HABITAT, the resulting assistant replies, the linked conversation address and tab identifier, model name, and readiness or error status. The extension uses your existing signed-in ChatGPT page. It does not create or rename conversations automatically.
- **Search:** queries you submit in HABITAT and the titles, links, descriptions, channel information and pagination information returned by the Google or YouTube search page.
- **Translation:** the source text and target language you submit in HABITAT and the text returned by Google Translate.
- **Facebook (optional):** when you grant Facebook site access and open its widget in HABITAT, the extension reads names, profile photo addresses, available Facebook links and explicit visible online/offline labels in the rendered Contacts panel or All friends list. An already-open Home tab can supply presence for matching profiles; missing or ambiguous status remains unknown. If Home has no contacts, it can open All friends in a background tab. It reads only loaded rows, without scrolling the list, reading messages or opening conversations to collect data; the complete friends list is not guaranteed. Names, presence and contact snapshots are kept in memory rather than saved to extension storage. HABITAT loads the photo addresses to display avatars; Facebook's image servers receive those normal image requests. Contact cards have no profile-opening action.
- **Connection information:** a local pairing key, your connection choice, browser tab identifiers and addresses needed to route requests to the correct page. Basic tab information is used to find and validate the relevant page, not to build a browsing history or profile.

The extension does not read passwords, payment information, Gmail messages or unrelated page content. It does not access browser cookies directly or transfer a browsing history to the developer.

## Where information goes

**Optional Messenger submissions:** selecting a contact only selects a HABITAT destination. Pressing **Send message** passes the chosen recipient's link, name and your exact text to the local extension. It opens its own background Facebook tab, verifies the conversation heading and editor, inserts your text and invokes the normal Send control once using your signed-in Facebook account. It does not overwrite existing Messenger drafts, read received messages or import conversation history. Meta and your chosen recipient receive the submitted message. Temporary messaging tabs close after confirmed submissions or successful inspections. A tab with a draft, sign-in verification or uncertain submission stays open for review. Uncertain submissions are not automatically retried.

For Messenger, session storage keeps up to 100 operation identifiers and outcomes to prevent repeated submission after a worker restart. These records contain no text or recipient identity. Requests remain in memory while pending. HABITAT keeps separate recipient drafts and submitted-text views during the session; local update recovery may save drafts under opaque destination identifiers. Selecting another destination never shares a contact's draft with it.

A small local Messenger diagnostic log records operation IDs, stages, structural element counts and results, without names, URLs or message text. It keeps the current file and one previous file, rotating after 512 KiB. It remains on your computer and is never uploaded. A native diagnostic command can read it or explicitly submit one message through HABITAT after matching a unique loaded contact; reading the log does not send anything.

Your submitted requests go to the selected website through its normal interface: ChatGPT (OpenAI), Google Search or Google Translate (Google), or YouTube (Google). Those services receive the text needed for your request and process it under their own privacy policies and your account settings. The extension does not replace their sign-in, subscription, consent or verification requirements.

Requests, responses, relevant page information and connection status pass between the extension and HABITAT on the same computer using the loopback address `127.0.0.1:8765`. The local bridge uses HTTP/WebSocket and a pairing key that changes when HABITAT restarts; this local hop is not encrypted. Connections to the supported provider pages use HTTPS. No request content is sent to a HABITAT-operated server or to the developer.

## Storage and retention

Chrome extension-local storage keeps your connection choice, pairing key and linked ChatGPT tab/address until replaced, cleared or the extension is removed. This storage is not Chrome sync storage. Temporary search/translation tab identifiers are kept in session storage; in-progress requests and result caches are held in worker memory. Search tabs created for HABITAT may remain open until you clear that search or close them.

Facebook's tab identifier is kept in session storage so a later refresh can reuse it. Facebook contacts are read only for pending desktop requests while its widget is open. The widget refreshes periodically and clears its previous snapshot when refreshing or closing. You can revoke Facebook access with **Disable Facebook contacts** in the popup or Chrome's extension settings. The extension does not close or navigate your personal Facebook tabs.

Local Facebook connection diagnostics keep only the extension version, heartbeat age, read stage, error code and counts of sections, photos, rows and received contacts in memory. They contain no names, photo addresses or page text. They are available to the desktop app through its authenticated local bridge and are not sent to a server.

HABITAT displays the returned content and manages its own application state. The websites may retain messages, searches or translations according to their account settings and policies. Disconnecting or uninstalling the extension does not delete conversations, browser history or other data held by those services or by the desktop application. You can clear extension storage through Chrome's extension controls or remove the extension to remove its local storage.

## Permissions

The extension uses storage for connection settings; tabs and scripting to locate supported pages, submit your requests and read their results; alarms to restore the local connection after worker restarts; and access limited to the listed supported website origins and the local bridge. The debugger permission is used only to enable page-focus emulation on the Google, YouTube or Translate tab handling your request so it can work in the background. It is released after the request or cancellation. The extension does not use debugger access to read unrelated pages, passwords or network traffic.

Facebook access is an optional host permission requested only when you select **Enable Facebook contacts**. It is limited to facebook.com and www.facebook.com. Facebook uses its existing Chrome session; no additional Facebook API credentials, account passwords or cookies are collected. The Facebook reader does not use debugger access.

## Limited use and sharing

Information is used only to provide the disclosed connection between HABITAT and the supported websites. It is not sold, used for advertising, shared with data brokers or used for unrelated profiling. The developer does not receive conversation or search content and does not have a facility to read it. The use of information received from Google APIs adheres to the Chrome Web Store User Data Policy, including the Limited Use requirements.

## Changes and contact

Material changes to data handling will be explained to users and this policy will be updated. For questions or support, use the [HABITAT issue tracker](https://github.com/aesgalexis/habitat/issues). Issues are public: do not include passwords, pairing keys, private conversations or other sensitive information.
