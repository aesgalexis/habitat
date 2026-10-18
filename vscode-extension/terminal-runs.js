'use strict';

// Bounded, plain text output. Escape sequences can span stream chunks; carriage
// returns replace progress lines instead of filling HABITAT with spinner frames.
class TerminalText {
  constructor(limit = 65536) { this.limit = limit; this.history = ''; this.line = []; this.column = 0; this.mode = ''; this.truncated = false; }
  append(chunk) {
    for (const char of chunk) {
      if (this.mode === 'escape') {
        this.mode = char === '[' ? 'csi' : ']PX^_'.includes(char) ? 'string' : '';
        continue;
      }
      if (this.mode === 'csi') { if (char >= '@' && char <= '~') this.mode = ''; continue; }
      if (this.mode === 'string') {
        if (char === '\x07' || char === '\x9c') this.mode = '';
        else if (char === '\x1b') this.mode = 'stringEscape';
        continue;
      }
      if (this.mode === 'stringEscape') { this.mode = char === '\\' ? '' : 'string'; continue; }
      if (char === '\x1b') { this.mode = 'escape'; continue; }
      if (char === '\x9b') { this.mode = 'csi'; continue; }
      if (char === '\x9d') { this.mode = 'string'; continue; }
      if (char === '\r') { this.column = 0; continue; }
      if (char === '\b') { this.column = Math.max(0, this.column - 1); continue; }
      if (char === '\n') { this.history += this.line.join('') + '\n'; this.line = []; this.column = 0; }
      else if (char === '\t' || char >= ' ' && !(char >= '\x7f' && char <= '\x9f')) this.line[this.column++] = char;
      if (this.line.length > this.limit) { this.line.splice(0, this.line.length - this.limit); this.column = this.line.length; this.truncated = true; }
      if (this.history.length > this.limit) { this.history = this.history.slice(-this.limit); this.truncated = true; }
    }
  }
  text() {
    const value = this.history + this.line.join('');
    if (value.length > this.limit) this.truncated = true;
    return value.slice(-this.limit);
  }
}

function createTerminalRuns(vscode, { integrationTimeoutMs = 3000, outputLimit = 65536, maxRuns = 32 } = {}) {
  const runs = new Map();
  const executions = new Map();
  let disposed = false;
  const subscriptions = [
    vscode.window.onDidChangeTerminalShellIntegration(event => {
      for (const entry of runs.values()) if (entry.terminal === event.terminal && !entry.dispatched)
        dispatch(entry, event.shellIntegration);
    }),
    vscode.window.onDidEndTerminalShellExecution(event => {
      const entry = executions.get(event.execution);
      if (!entry || entry.state === 'closed') return;
      entry.pendingEnd = { code: Number.isInteger(event.exitCode) ? event.exitCode : null, at: Date.now() };
      // The reader may still have the last error lines queued when the shell
      // reports exit. Publish a final snapshot only after draining that tail.
      if (entry.streamEnded) finish(entry);
      else entry.finishTimer = setTimeout(() => finish(entry), 1000);
    }),
    vscode.window.onDidCloseTerminal(terminal => {
      for (const entry of runs.values()) if (entry.terminal === terminal && ['starting', 'running', 'unknown'].includes(entry.state)) {
        if (entry.pendingEnd) { finish(entry); continue; }
        clearTimeout(entry.timer);
        entry.dispatched = true;
        entry.state = 'closed'; entry.endedAt ??= Date.now();
        if (entry.execution) executions.delete(entry.execution);
      }
    })
  ];
  function finish(entry) {
    if (!entry.pendingEnd || disposed || entry.evicted) return;
    clearTimeout(entry.finishTimer);
    entry.exitCode = entry.pendingEnd.code;
    entry.endedAt = entry.pendingEnd.at;
    entry.state = entry.exitCode === null ? 'unknown' : entry.exitCode === 0 ? 'completed' : 'failed';
    executions.delete(entry.execution);
    entry.pendingEnd = null;
  }
  function dispatch(entry, integration) {
    if (disposed || entry.dispatched) return;
    // Mark before invoking any API: even an uncertain failure must never cause
    // a fallback sendText or a late shell-integration event to execute twice.
    entry.dispatched = true;
    clearTimeout(entry.timer);
    try {
      if (!integration) {
        entry.terminal.sendText(entry.command, true);
        entry.state = 'unknown';
        return;
      }
      const execution = integration.executeCommand(entry.command);
      entry.execution = execution;
      executions.set(execution, entry);
      entry.state = 'running';
      const stream = execution.read();
      void (async () => {
        try {
          for await (const chunk of stream) {
            if (disposed || entry.evicted) break;
            entry.output.append(chunk);
          }
        } catch { /* Exit status remains independent of output availability. */ }
        finally { entry.streamEnded = true; finish(entry); }
      })();
    } catch { entry.state = 'unknown'; }
  }
  return {
    start(body, name) {
      if (disposed) throw new Error('Bridge disposed');
      const terminal = vscode.window.createTerminal({ name, cwd: vscode.Uri.file(body.path) });
      const entry = { id: body.requestId, command: body.command, path: body.path, name, terminal,
        state: 'starting', startedAt: Date.now(), endedAt: null, exitCode: null, dispatched: false, output: new TerminalText(outputLimit) };
      if (runs.size >= maxRuns) {
        const oldest = runs.values().next().value;
        oldest.evicted = true; clearTimeout(oldest.timer); clearTimeout(oldest.finishTimer);
        // Dispatch any still-waiting accepted command once before retiring its
        // telemetry. Eviction never cancels or kills a user's terminal.
        dispatch(oldest, oldest.terminal.shellIntegration);
        if (oldest.execution) executions.delete(oldest.execution);
        runs.delete(oldest.id);
      }
      runs.set(entry.id, entry);
      try { terminal.show(true); } catch { /* Showing is independent of dispatch. */ }
      entry.timer = setTimeout(() => dispatch(entry, terminal.shellIntegration), integrationTimeoutMs);
      if (terminal.shellIntegration) dispatch(entry, terminal.shellIntegration);
      return terminal;
    },
    get(id) {
      const entry = runs.get(id);
      if (!entry) return null;
      const output = entry.output.text();
      return { id: entry.id, command: entry.command, path: entry.path, terminal: entry.name, state: entry.state,
        startedAt: entry.startedAt, endedAt: entry.endedAt, exitCode: entry.exitCode,
        elapsedMs: (entry.endedAt ?? Date.now()) - entry.startedAt, output, truncated: entry.output.truncated };
    },
    dispose() {
      disposed = true;
      for (const item of subscriptions) item.dispose();
      for (const entry of runs.values()) { clearTimeout(entry.timer); clearTimeout(entry.finishTimer); }
      executions.clear(); runs.clear();
    }
  };
}

module.exports = { createTerminalRuns, TerminalText };
