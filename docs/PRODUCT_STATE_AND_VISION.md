# HABITAT — Product state and direction

_Vision and historical feedback. Documentation reorganized on 24 October 2026; current implementation evidence is maintained in [PROJECT_STATUS.md](PROJECT_STATUS.md)._

## What Habitat is

Habitat is a lightweight Windows desktop surface for conversations with ChatGPT. It began as an experiment: put a text box in a desktop window, send messages into an **existing ChatGPT conversation**, and read the answer back without moving the conversation, its history, or model selection to a separate product.

The principle is simple: **ChatGPT remains the conversation system of record; Habitat makes using it on Windows feel more natural.** Habitat is not intended to become a competing assistant, a separate model client, or a heavy control dashboard.

## Current implementation and historical feedback

Use [PROJECT_STATUS.md](PROJECT_STATUS.md) for the inspected source state and [USER_GUIDE.md](USER_GUIDE.md) for current usage. Release metadata, local implementation and published assets are distinct; this page does not certify a release or a test run.

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

### Typing quality (historical priority; revalidate before scheduling)

- Reassess native spellchecking support and actual typing behavior before planning changes.
- Support **English (US) and Spanish**, since the user naturally switches between them within conversations.
- Prefer automatic language handling if robust; otherwise a frictionless language choice. Avoid autocorrection that silently mangles technical names, model numbers, code or mixed-language phrases.
- Validate typing, selection, copy/paste, multiline composition and keyboard shortcuts before adding more UI.

### Small conveniences (ideas, not committed scope)

- Attach or drag-and-drop a document or image from Explorer into the conversation, if the bridge can handle it reliably.
- Explore user-approved screenshot import. Selected-text import is already available through Ctrl+H.
- Explore compact floating invocation; the optional global shortcut is already implemented.
- Extend project context only where it saves a demonstrated step; VS Code already supports project selection and integrated terminal execution.

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
