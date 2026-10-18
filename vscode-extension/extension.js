'use strict';

const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const crypto = require('node:crypto');
const { createTerminalRuns } = require('./terminal-runs');

// One authenticated loopback endpoint per VS Code window. Nothing is registered
// in workspace settings, and commands never enter an existing terminal.
async function startBridge(vscode, directory, options) {
  const sessionId = crypto.randomUUID();
  const token = crypto.randomBytes(32).toString('hex');
  let lastFocused = vscode.window.state.focused ? Date.now() : 0;
  const focus = vscode.window.onDidChangeWindowState(state => {
    if (state.focused) lastFocused = Date.now();
  });
  const requests = new Map();
  const runs = createTerminalRuns(vscode, options);
  let terminalNumber = 0;
  let lastTerminal;
  const uriHandler = vscode.window.registerUriHandler({ handleUri(uri) {
    if (uri.path === '/focus' && new URLSearchParams(uri.query).get('session') === sessionId)
      lastTerminal?.show(false);
  } });
  function context() {
    const folders = (vscode.workspace.workspaceFolders || [])
      .filter(folder => folder.uri.scheme === 'file' && !vscode.env.remoteName)
      .map(folder => ({ name: folder.name, path: folder.uri.fsPath }));
    const editor = vscode.window.activeTextEditor;
    const active = editor && vscode.workspace.getWorkspaceFolder(editor.document.uri);
    const activePath = folders.find(folder => folder.path === active?.uri.fsPath)?.path ||
      (folders.length === 1 ? folders[0].path : null);
    const state = { workspace: vscode.workspace.name || 'VS Code', folders, activePath,
      trusted: vscode.workspace.isTrusted, remote: !!vscode.env.remoteName };
    return { ...state, sessionId, lastFocused,
      contextId: crypto.createHash('sha256').update(JSON.stringify(state)).digest('hex') };
  }
  function run(body) {
    if (typeof body?.requestId !== 'string' || !/^[a-f0-9-]{36}$/.test(body.requestId) ||
        typeof body.command !== 'string' || !body.command.trim() || body.command.length > 32768 ||
        /[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/.test(body.command)) {
      return { status: 400, value: { error: 'Invalid terminal command.' } };
    }
    const fingerprint = JSON.stringify(body);
    if (requests.has(body.requestId)) {
      const previous = requests.get(body.requestId);
      return previous.fingerprint === fingerprint ? previous.result :
        { status: 409, value: { error: 'Request ID already used.' } };
    }
    // Keep all IDs for this session: evicting IDs could execute a retry twice.
    if (requests.size >= 10000) return { status: 409, value: { error: 'Reload VS Code before sending more commands.' } };
    const current = context();
    if (!current.trusted || current.remote) return { status: 403, value: { error: 'Open a trusted local project in VS Code.' } };
    if (body.sessionId !== sessionId || body.contextId !== current.contextId ||
        !current.folders.some(folder => folder.path === body.path)) {
      return { status: 409, value: { error: 'The VS Code project changed. Review the project and send again.' } };
    }
    try {
      if (!fs.statSync(body.path).isDirectory()) throw new Error('Missing directory');
    } catch { return { status: 409, value: { error: 'The project directory is no longer available.' } }; }
    const name = `HABITAT ${++terminalNumber}`;
    // Record uncertainty before calling the terminal API. Never automatically
    // repeat a command after an exception or lost response.
    const entry = { fingerprint, result: { status: 500,
      value: { error: 'Could not confirm command delivery. Check VS Code before sending again.' } } };
    requests.set(body.requestId, entry);
    try {
      const terminal = runs.start(body, name);
      lastTerminal = terminal;
      entry.result = { status: 200, value: { accepted: true, terminal: name, executionId: body.requestId } };
    } catch { /* Keep the uncertain result and the request ID. */ }
    return entry.result;
  }
  const server = http.createServer(async (req, res) => {
    const reply = (status, value) => {
      res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
      res.end(JSON.stringify(value));
    };
    const supplied = req.headers['x-habitat-key'];
    if (req.headers.origin || req.headers.host !== `127.0.0.1:${server.address().port}` ||
        typeof supplied !== 'string' || !/^[a-f0-9]{64}$/.test(supplied) ||
        !crypto.timingSafeEqual(Buffer.from(supplied), Buffer.from(token))) {
      req.resume(); reply(403, { error: 'Forbidden.' }); return;
    }
    if (req.method === 'GET' && req.url === '/context') { reply(200, context()); return; }
    if (req.method === 'GET' && /^\/runs\/[a-f0-9-]{36}$/.test(req.url)) {
      const snapshot = runs.get(req.url.slice(6));
      reply(snapshot ? 200 : 404, snapshot || { error: 'Execution unavailable.' });
      return;
    }
    if (req.method === 'GET' && req.url === '/focus') {
      try {
        const uri = await vscode.env.asExternalUri(vscode.Uri.parse(
          `${vscode.env.uriScheme}://habitat-local.habitat-terminal/focus?session=${sessionId}`));
        reply(200, { uri: uri.toString() });
      } catch { reply(500, { error: 'Could not open this VS Code window.' }); }
      return;
    }
    if (req.method !== 'POST' || req.url !== '/run') { req.resume(); reply(404, { error: 'Not found.' }); return; }
    if (req.headers['content-type'] !== 'application/json') { req.resume(); reply(415, { error: 'Expected JSON.' }); return; }
    try {
      const chunks = [];
      let size = 0;
      for await (const chunk of req) {
        size += chunk.length;
        if (size > 200000) { reply(413, { error: 'Command too large.' }); return; }
        chunks.push(chunk);
      }
      const result = run(JSON.parse(Buffer.concat(chunks).toString('utf8')));
      reply(result.status, result.value);
    } catch { reply(400, { error: 'Invalid request.' }); }
  });
  server.requestTimeout = 5000;
  server.headersTimeout = 5000;
  server.timeout = 5000;
  server.maxConnections = 16;
  const descriptor = path.join(directory, `${sessionId}.json`);
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    fs.mkdirSync(directory, { recursive: true });
    fs.writeFileSync(descriptor, JSON.stringify({ sessionId, token, port: server.address().port }), { mode: 0o600 });
  } catch (error) { focus.dispose(); uriHandler.dispose(); runs.dispose(); server.close(); throw error; }
  return { dispose() {
    focus.dispose();
    uriHandler.dispose();
    runs.dispose();
    server.close();
    server.closeAllConnections();
    try { fs.unlinkSync(descriptor); } catch { /* A crashed host may leave an inert descriptor. */ }
  }, descriptor };
}

async function activate(context) {
  const vscode = require('vscode');
  if (process.platform !== 'win32' || !process.env.LOCALAPPDATA) return;
  context.subscriptions.push(await startBridge(vscode, path.join(process.env.LOCALAPPDATA, 'HABITAT', 'vscode')));
}

module.exports = { activate, startBridge };
