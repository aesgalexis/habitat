# HABITAT Browser Bridge privacy policy

Effective date: 19 October 2026. Extension version: 0.9.0.

This policy covers the HABITAT Browser Bridge Chrome extension maintained by [aesgalexis](https://github.com/aesgalexis). The extension connects Chrome to the HABITAT desktop application running on your own Windows computer. It has no HABITAT cloud backend, advertising, analytics or tracking service.

## Your choice

The extension stays disconnected until you select **Enable connection** on its connection and privacy page. After enabling it, requests submitted in HABITAT can be carried out in the supported websites in Chrome. You can select **Disconnect** in the extension popup or **Keep disconnected** on that page at any time. Disabling the connection prevents subsequent jobs; it does not undo text already sent to a website.

## Information used

- **ChatGPT:** messages you submit through HABITAT, the resulting assistant replies, the linked conversation address and tab identifier, model name, and readiness or error status. The extension uses your existing signed-in ChatGPT page. It does not create or rename conversations automatically.
- **Search:** queries you submit in HABITAT and the titles, links, descriptions, channel information and pagination information returned by the Google or YouTube search page.
- **Translation:** the source text and target language you submit in HABITAT and the text returned by Google Translate.
- **Connection information:** a local pairing key, your connection choice, browser tab identifiers and addresses needed to route requests to the correct page. Basic tab information is used to find and validate the relevant page, not to build a browsing history or profile.

The extension does not read passwords, payment information, Gmail messages or unrelated page content. It does not access browser cookies directly or transfer a browsing history to the developer.

## Where information goes

Your submitted requests go to the selected website through its normal interface: ChatGPT (OpenAI), Google Search or Google Translate (Google), or YouTube (Google). Those services receive the text needed for your request and process it under their own privacy policies and your account settings. The extension does not replace their sign-in, subscription, consent or verification requirements.

Requests, responses, relevant page information and connection status pass between the extension and HABITAT on the same computer using the loopback address `127.0.0.1:8765`. The local bridge uses HTTP/WebSocket and a pairing key that changes when HABITAT restarts; this local hop is not encrypted. Connections to the supported provider pages use HTTPS. No request content is sent to a HABITAT-operated server or to the developer.

## Storage and retention

Chrome extension-local storage keeps your connection choice, pairing key and linked ChatGPT tab/address until replaced, cleared or the extension is removed. This storage is not Chrome sync storage. Temporary search/translation tab identifiers are kept in session storage; in-progress requests and result caches are held in worker memory. Search tabs created for HABITAT may remain open until you clear that search or close them.

HABITAT displays the returned content and manages its own application state. The websites may retain messages, searches or translations according to their account settings and policies. Disconnecting or uninstalling the extension does not delete conversations, browser history or other data held by those services or by the desktop application. You can clear extension storage through Chrome's extension controls or remove the extension to remove its local storage.

## Permissions

The extension uses storage for connection settings; tabs and scripting to locate supported pages, submit your requests and read their results; alarms to restore the local connection after worker restarts; and access limited to the listed supported website origins and the local bridge. The debugger permission is used only to enable page-focus emulation on the Google, YouTube or Translate tab handling your request so it can work in the background. It is released after the request or cancellation. The extension does not use debugger access to read unrelated pages, passwords or network traffic.

## Limited use and sharing

Information is used only to provide the disclosed connection between HABITAT and the supported websites. It is not sold, used for advertising, shared with data brokers or used for unrelated profiling. The developer does not receive conversation or search content and does not have a facility to read it. The use of information received from Google APIs adheres to the Chrome Web Store User Data Policy, including the Limited Use requirements.

## Changes and contact

Material changes to data handling will be explained to users and this policy will be updated. For questions or support, use the [HABITAT issue tracker](https://github.com/aesgalexis/habitat/issues). Issues are public: do not include passwords, pairing keys, private conversations or other sensitive information.
