# HABITAT — Product state and direction

_Last updated: 14 October 2026 · living project note_

## What Habitat is

Habitat is a lightweight Windows desktop surface for conversations with ChatGPT. It began as an experiment: put a text box in a desktop window, send messages into an **existing ChatGPT conversation**, and read the answer back without moving the conversation, its history, or model selection to a separate product.

The principle is simple: **ChatGPT remains the conversation system of record; Habitat makes using it on Windows feel more natural.** Habitat is not intended to become a competing assistant, a separate model client, or a heavy control dashboard.

## State of the project — distinguish repository evidence from hands-on feedback

### Verified in this repository at this revision

- The current desktop app is C# / WinUI 3 on .NET 10, launched with `HABITAT.vbs` after building. Python/Tkinter was the original prototype.
- The bridge uses a Chrome extension following the active ChatGPT conversation, and a local service bound to `127.0.0.1:8765` with a per-run pairing key. The background extension maintains a WebSocket connection and pairs again after app restarts. An in-flight answer finishes in its original conversation.
- The existing conversation in ChatGPT is retained; the bridge sends messages and extracts textual responses from its web interface.
- Rich content (interactive controls, images, files, confirmations and complex tool states) cannot be assumed to render faithfully in the prototype.
- This is unofficial UI automation; browser DOM changes and uncertain send/receive states are engineering risks.
- Day, Night and Hybrid appearance, background opacity and window placement are persisted. The composer is centered for an empty conversation and moves to the bottom once messages exist.
- A weather widget uses Windows location with a saved-city fallback and Open-Meteo forecasts; the forecast refreshes every 15 minutes.
- A Gmail widget rotates sender, subject and snippet every eight seconds. Icon/count open the local reader; message text opens Gmail. Hover highlights are independent, with no tooltips; narrow windows use the compact icon/count view.
- Gmail uses read-only desktop OAuth, encrypted Windows DPAPI storage and automatic access-token renewal. Refresh tokens issued in OAuth Testing remain subject to Google's seven-day lifetime. Mail content is not sent to ChatGPT.
- Automated checks cover the extension transport, local bridge, Gmail authorization and persistence, weather and appearance. They do not establish compatibility with every future ChatGPT web UI.

The README and Gmail setup guide now describe this implementation. New clones must build the application, load the extension and separately configure any Gmail account; local credentials and compiled artifacts are not included.

### Reported from real use on 13 October 2026

- Habitat is now being used to have real, extended conversations with ChatGPT, including professional discussions and information retrieval.
- The user describes the current interface as **minimal, beautiful, well done, and almost perfect**; it already feels pleasant as an everyday chat surface.
- The WinUI 3 implementation is now present in the repository; the earlier feedback referred to a build that was ahead of the default branch at the time.
- An immediate usability gap is **US English spelling correction**, ideally working alongside Spanish.
- Its strongest current quality is restraint: a small, visually polished chat interface, not a feature-packed workspace.

These observations are usage feedback, not claims verified by the code currently present in this GitHub revision.

## Product philosophy

1. **Protect the calm.** Minimalism is a defining feature, not a temporary absence of functionality. Avoid decorative panels, crowded toolbars and features that merely make the app look “more complete.”
2. **Stay close to the original ChatGPT conversation.** Preserve history, continuity, selected model and existing account behavior rather than recreating them in Habitat.
3. **Make Windows-native interaction the differentiator.** Add capabilities that remove friction when moving between a desktop task and the conversation.
4. **Add features only for a demonstrated job.** A new feature should save clicks, copying, context switching or repeated explanation. Otherwise it probably does not belong.
5. **Keep the user in control.** Context sharing and local computer actions must be deliberate, visible and permissioned.
6. **Be honest about the bridge.** Text support is useful today, but do not represent browser automation as an official, guaranteed or feature-complete ChatGPT integration.

## Near-term improvements worth considering

### First: typing quality

- Enable native spellchecking where supported in the WinUI 3 text editor.
- Support **English (US) and Spanish**, since the user naturally switches between them within conversations.
- Prefer automatic language handling if robust; otherwise a frictionless language choice. Avoid autocorrection that silently mangles technical names, model numbers, code or mixed-language phrases.
- Validate typing, selection, copy/paste, multiline composition and keyboard shortcuts before adding more UI.

### Next: small, high-value conveniences (ideas, not committed scope)

- Attach or drag-and-drop a document or image from Explorer into the conversation, if the bridge can handle it reliably.
- Quickly send selected text or a user-approved screenshot from the active Windows application.
- Optional global shortcut or compact floating invocation, only if it genuinely improves the workflow.
- Optional project context/shortcuts (e.g., active repository or folder), without duplicating ChatGPT's native project or history features.

### Longer-term direction

Habitat could become a **permissioned desktop context layer**: while using an editor, document, browser or another Windows application, the user can intentionally say “look at this” and make the relevant local context available to ChatGPT. This is a direction to explore, not a claim of existing functionality.

## Non-goals and boundaries

- Do not build a separate message history or competing model/account selector merely for symmetry with ChatGPT.
- Do not grow a generic dashboard by default.
- Do not silently monitor the desktop, read private files, or execute system actions.
- Do not sacrifice the visual simplicity that already makes the app enjoyable.
- Do not promise parity with ChatGPT widgets, uploads, streaming or tools until implemented and tested.

## Engineering follow-up

- Keep the default branch, README and setup guides synchronized with the Windows implementation and its limitations.
- Check text composition/spellcheck behavior with mixed English/Spanish and technical vocabulary.
- Test transport failures, duplicate-send prevention, session pairing, disconnected-tab recovery, and privacy/security assumptions.
- Separate **verified implemented behavior**, **observed user experience** and **proposed ideas** in future documentation.

## North star

**Habitat should feel like the most effortless place on Windows to talk to ChatGPT.**

It does not need many features. It needs the right few—and should remain beautiful after they are added.
