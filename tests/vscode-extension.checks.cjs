'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const http = require('node:http');
const { startBridge } = require('../vscode-extension/extension');
const { TerminalText } = require('../vscode-extension/terminal-runs');

function event() {
  const handlers = new Set();
  return { subscribe(callback) { handlers.add(callback); return { dispose() { handlers.delete(callback); } }; },
    fire(value) { for (const handler of handlers) handler(value); } };
}
function execution() {
  const chunks = [];
  let resume, done = false;
  return {
    push(text) { chunks.push(text); resume?.(); },
    finish() { done = true; resume?.(); },
    async *read() {
      while (!done || chunks.length) {
        if (chunks.length) yield chunks.shift();
        else await new Promise(resolve => { resume = resolve; });
      }
    }
  };
}

(async () => {
  const root = fs.mkdtempSync(path.resolve(__dirname, '../artifacts/vscode-checks-'));
  const project = path.join(root, 'café project');
  const other = path.join(root, 'other');
  fs.mkdirSync(project); fs.mkdirSync(other);
  let focusChanged;
  const folder = value => ({ name: path.basename(value), uri: { scheme: 'file', fsPath: value } });
  const terminals = [];
  const integrationChanged = event(), executionEnded = event(), terminalClosed = event();
  let integrated = true, throwDispatch = false;
  let uriHandler;
  const vscode = {
    env: {uriScheme:'vscode',asExternalUri:async uri=>({...uri,toString:()=>`vscode://habitat-local.habitat-terminal/focus?${uri.query}&windowId=17`})},
    Uri: { file: fsPath => ({ scheme: 'file', fsPath }),parse:text=>({path:'/focus',query:new URL(text).search.slice(1)}) },
    workspace: { name: 'Workspace', isTrusted: true, workspaceFolders: [folder(project), folder(other)],
      getWorkspaceFolder: uri => vscode.workspace.workspaceFolders.find(value => value.uri.fsPath === uri.fsPath) },
    window: { state: { focused: true }, activeTextEditor: { document: { uri: { fsPath: project } } },
      registerUriHandler:handler=>{uriHandler=handler;return{dispose(){}};},
      onDidChangeWindowState: callback => { focusChanged = callback; return { dispose() {} }; },
      onDidChangeTerminalShellIntegration: integrationChanged.subscribe,
      onDidEndTerminalShellExecution: executionEnded.subscribe,
      onDidCloseTerminal: terminalClosed.subscribe,
      createTerminal: options => {
        const terminal = { options, sent: [], show(focus) { this.preserveFocus = focus; },
          sendText(text, execute) { this.sent.push({ text, execute }); } };
        if (integrated) terminal.shellIntegration = { executeCommand(text) {
          if (throwDispatch) throw new Error('Uncertain dispatch');
          terminal.sent.push({ text, execute: true }); terminal.execution = execution(); return terminal.execution;
        } };
        terminals.push(terminal); return terminal;
      } }
  };
  const bridge = await startBridge(vscode, root, { integrationTimeoutMs: 250 });
  try {
    const descriptor = JSON.parse(fs.readFileSync(bridge.descriptor, 'utf8'));
    const url = `http://127.0.0.1:${descriptor.port}`;
    const headers = { 'X-Habitat-Key': descriptor.token, 'Content-Type': 'application/json' };
    const get = () => fetch(url + '/context', { headers }).then(response => response.json());
    const post = body => fetch(url + '/run', { method: 'POST', headers, body: JSON.stringify(body) });
    const status = async id => {
      await new Promise(resolve => setTimeout(resolve, 5));
      return fetch(url + '/runs/' + id, { headers }).then(response => response.json());
    };
    const context = await get();
    assert.equal(context.activePath, project);
    assert.equal(context.folders.length, 2);
    assert(context.lastFocused > 0);
    const command = 'npm run dev # café ☕\r\nWrite-Output "quotes & $HOME"';
    const body = { requestId: crypto.randomUUID(), sessionId: descriptor.sessionId,
      contextId: context.contextId, path: project, command };
    assert.equal((await post(body)).status, 200);
    assert.equal((await post(body)).status, 200);
    assert.equal(terminals.length, 1, 'Duplicate ID must not create another terminal');
    assert.deepEqual(terminals[0].sent, [{ text: command, execute: true }]);
    assert.equal(terminals[0].options.cwd.fsPath, project);
    assert.equal(terminals[0].preserveFocus, true);
    let snapshot = await status(body.requestId);
    assert.equal(snapshot.state, 'running');
    assert.equal(snapshot.command, command); assert.equal(snapshot.path, project);
    terminals[0].execution.push('\x1b[3');
    terminals[0].execution.push('2mReady café ☕\x1b[0m\r\nhttp://localhost:5173/\r\n');
    snapshot = await status(body.requestId);
    assert.equal(snapshot.output, 'Ready café ☕\nhttp://localhost:5173/\n');
    assert.equal(snapshot.state, 'running', 'A long-running server does not complete when it prints a URL');
    assert.equal((await fetch(url + '/runs/' + body.requestId)).status, 403);
    assert.equal((await fetch(url + '/runs/' + crypto.randomUUID(), { headers })).status, 404);
    executionEnded.fire({ execution: terminals[0].execution, exitCode: 0 });
    assert.equal((await status(body.requestId)).state, 'running', 'Exit waits for the last output chunk before publishing completion');
    terminals[0].execution.push('Final output tail\n');
    terminals[0].execution.finish();
    snapshot = await status(body.requestId);
    assert.equal(snapshot.state, 'completed'); assert.equal(snapshot.exitCode, 0); assert(snapshot.endedAt);
    assert(snapshot.output.endsWith('Final output tail\n'));
    const focusResponse=await fetch(url+'/focus',{headers});
    assert.equal(focusResponse.status,200);
    const focusUri=new URL((await focusResponse.json()).uri);
    assert.equal(focusUri.searchParams.get('windowId'),'17');
    uriHandler.handleUri({path:focusUri.pathname,query:focusUri.search.slice(1)});
    assert.equal(terminals[0].preserveFocus,false,'Explicit open focuses the existing terminal');
    assert.equal(terminals.length,1,'Opening a window must never execute another command');
    assert.equal((await fetch(url+'/focus')).status,403,'Window focus also requires authentication');
    assert.equal((await post({ ...body, command: 'different' })).status, 409);
    const publish = { ...body, requestId: crypto.randomUUID(), command: 'npm run site:publish' };
    assert.equal((await post(publish)).status, 200);
    assert.equal(terminals.length, 2);
    assert.notEqual(terminals[0].options.name, terminals[1].options.name);
    terminals[1].execution.push('x'.repeat(70000) + '\nError: synthetic failure\n');
    terminals[1].execution.finish(); executionEnded.fire({ execution: terminals[1].execution, exitCode: 7 });
    snapshot = await status(publish.requestId);
    assert.equal(snapshot.state, 'failed'); assert.equal(snapshot.exitCode, 7);
    assert(snapshot.truncated); assert(snapshot.output.length <= 65536); assert(snapshot.output.endsWith('Error: synthetic failure\n'));
    assert.equal((await status(body.requestId)).state, 'completed', 'Separate runs keep their own output and exit code');
    assert.equal((await fetch(url + '/run', { method: 'POST', headers: { ...headers, Origin: 'https://example.com' }, body: JSON.stringify(body) })).status, 403);
    assert.equal((await fetch(url + '/context')).status, 403);
    assert.equal((await fetch(url + '/context', { headers: { ...headers, 'X-Habitat-Key': 'f'.repeat(64) } })).status, 403);
    const wrongHost = await new Promise((resolve, reject) => {
      http.get(url + '/context', { headers: { ...headers, Host: 'evil.example' } }, response => {
        response.resume(); resolve(response.statusCode);
      }).on('error', reject);
    });
    assert.equal(wrongHost, 403);
    assert.equal((await post({ ...body, requestId: crypto.randomUUID(), path: root })).status, 409);
    assert.equal((await post({ ...body, requestId: crypto.randomUUID(), sessionId: crypto.randomUUID() })).status, 409);
    assert.equal((await post({ ...body, requestId: crypto.randomUUID(), command: 'bad\u001btext' })).status, 400);
    vscode.window.activeTextEditor.document.uri.fsPath = other;
    assert.equal((await post({ ...body, requestId: crypto.randomUUID() })).status, 409);
    vscode.workspace.isTrusted = false;
    assert.equal((await post({ ...body, requestId: crypto.randomUUID() })).status, 403);
    vscode.workspace.isTrusted = true;
    vscode.env.remoteName = 'ssh-remote';
    const remote = await get();
    assert.equal(remote.remote, true); assert.deepEqual(remote.folders, []);
    assert.equal((await post({ ...body, requestId: crypto.randomUUID() })).status, 403);
    vscode.env.remoteName = undefined;
    vscode.workspace.workspaceFolders = [];
    assert.equal((await get()).activePath, null);
    assert.equal((await post({ ...body, requestId: crypto.randomUUID() })).status, 409);
    vscode.workspace.workspaceFolders = [folder(project), folder(other)];
    vscode.window.activeTextEditor = undefined;
    assert.equal((await get()).activePath, null, 'Multi-root without editor requires folder selection');
    vscode.workspace.workspaceFolders = [folder(project)];
    assert.equal((await get()).activePath, project);
    focusChanged({ focused: true });
    assert((await get()).lastFocused >= context.lastFocused);
    assert.equal(terminals.length, 2, 'Rejected requests never create terminals');
    integrated = false;
    const fallback = { ...body, requestId: crypto.randomUUID(), contextId: (await get()).contextId };
    assert.equal((await post(fallback)).status, 200);
    const waiting = await status(fallback.requestId);
    assert.equal(waiting.state, 'starting');
    await new Promise(resolve => setTimeout(resolve, 275));
    assert.equal((await status(fallback.requestId)).state, 'unknown');
    assert.equal(terminals[2].sent.length, 1);
    integrationChanged.fire({ terminal: terminals[2], shellIntegration: { executeCommand() { throw new Error('Late integration executed twice'); } } });
    assert.equal(terminals[2].sent.length, 1);
    terminalClosed.fire(terminals[2]);
    assert.equal((await status(fallback.requestId)).state, 'closed');
    integrated = true; throwDispatch = true;
    const uncertain = { ...fallback, requestId: crypto.randomUUID() };
    assert.equal((await post(uncertain)).status, 200);
    assert.equal((await status(uncertain.requestId)).state, 'unknown');
    assert.equal(terminals[3].sent.length, 0, 'An uncertain executeCommand never falls back to sending again');
    throwDispatch = false;
    const noExit = { ...fallback, requestId: crypto.randomUUID() };
    assert.equal((await post(noExit)).status, 200);
    terminals[4].execution.finish(); executionEnded.fire({ execution: terminals[4].execution, exitCode: undefined });
    assert.equal((await status(noExit.requestId)).state, 'unknown', 'An absent exit code is not success');
    vscode.window.createTerminal = () => { throw new Error('Terminal unavailable'); };
    const failed = { ...body, requestId: crypto.randomUUID(), contextId: (await get()).contextId };
    assert.equal((await post(failed)).status, 500);
    assert.equal((await post(failed)).status, 500);
    const plain = new TerminalText(100);
    plain.append('\x1b]0;hidden'); plain.append(' title\x07abc\rXY\nUnicode 雪\x1b['); plain.append('31m!\x1b[0m');
    assert.equal(plain.text(), 'XYc\nUnicode 雪!');
    console.log('PASS: VS Code execution lifecycle, live output, exit codes, bounded ANSI-free Unicode output, independent terminals, unknown states, authentication and no duplicate dispatch');
  } finally {
    bridge.dispose();
    assert(!fs.existsSync(bridge.descriptor));
    // Explicit, resolved test directory under artifacts only.
    assert(root.startsWith(path.resolve(__dirname, '../artifacts') + path.sep));
    fs.rmSync(root, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
