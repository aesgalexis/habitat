# HABITAT Terminal

Connects each local VS Code window to HABITAT. Select **VS Code** in HABITAT,
review the project and working directory, enter a command and press **Run in VS Code**.
The command runs directly without a confirmation dialog.
Every send creates a fresh integrated terminal using the project's root directory
and VS Code's configured default shell. Existing terminals keep running.
Version 0.2.0 returns live execution state, elapsed time, exit code and a bounded
plain text output stream to HABITAT over the authenticated local bridge.
HABITAT shows a compact preview and **Show output** expands the captured text.
Full terminal output remains in VS Code. No output is sent to a cloud service.

Tracking uses VS Code shell integration. A fresh terminal waits up to three
seconds for integration before sending once with the fallback API. If integration
is unavailable, the exit code is unknown, or monitoring disconnects, HABITAT shows
**Status unavailable**. It never guesses success or reexecutes to recover status.
Development servers stay **Running** until their process ends. Output is limited
to the last 65,536 characters in each of the 32 most recent executions; terminal
escape sequences are removed and progress lines are normalized. Retiring a
capture or closing HABITAT does not stop a terminal process.

The external action icon beside the selected destination runs the same
command and then opens this VS Code window. With an empty editor it only opens
the selected project. Version 0.1.1 adds an authenticated `/focus` endpoint that
resolves a fresh window-specific URI through VS Code's `asExternalUri` API.
The URI handler only reveals the last delivered terminal; it cannot run commands.
Failed window activation never repeats an accepted command.

HABITAT shows a compact **Project** selector and the working directory from the
start, above the editor, which stays centered until the first send. It follows the project in the
last focused VS Code window. In a multi-root workspace the active editor
determines the folder. If there is no active folder, choose one explicitly.
A manual selection stays pinned when switching destinations. **Choose folder…**
opens a native folder picker for any local project, including folders not open
in VS Code. HABITAT remembers up to 20 choices in its own preferences. When
you run a command, it opens a missing project and waits for that exact
folder to connect before sending. The extension is installed once in VS Code;
no project code, configuration files or dependencies are needed. VS Code's
workspace trust checks still apply.
Project discovery refreshes automatically.
Remote windows, empty windows and untrusted workspaces cannot run commands.

Build locally with `powershell -NoProfile -File scripts/build-vscode-extension.ps1`,
then install the generated VSIX with `code --install-extension <path> --force`.
Reload existing VS Code windows after installation (**Developer: Reload Window**).
No Marketplace account or npm dependencies are required.

The extension binds only to `127.0.0.1` on a random port per window. Its discovery
file and random authentication token are stored under `%LOCALAPPDATA%\HABITAT\vscode`.
It rejects browser origins and mismatched authentication, checks workspace trust
and the exact context again before sending, and remembers request IDs for the
window's lifetime to prevent duplicate execution. It reads no source file contents,
does not edit documents, and does not send terminal output or commands to a cloud service.
