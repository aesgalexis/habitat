'use strict';

// Opt-in VS Code extension-host test. Run in a dedicated profile and synthetic
// workspace under artifacts; never reloads or changes a user's editor window.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { createTerminalRuns } = require('../vscode-extension/terminal-runs');

exports.run = async function () {
  const vscode = require('vscode');
  const root = path.resolve(__dirname, '../artifacts/vscode-terminal-live');
  const workspace = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  assert.equal(path.resolve(workspace), path.join(root, 'workspace'));
  const runs = createTerminalRuns(vscode, { integrationTimeoutMs: 10000 });
  const terminals = [];
  const results = [];
  const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
  const start = script => {
    const id = crypto.randomUUID();
    const command = `node -e "eval(Buffer.from('${Buffer.from(script).toString('base64')}','base64').toString('utf8'))"`;
    terminals.push(runs.start({ requestId: id, command, path: workspace }, 'HABITAT isolated check'));
    return id;
  };
  const wait = async (id, predicate) => {
    const deadline = Date.now() + 25000;
    while (Date.now() < deadline) {
      const state = runs.get(id);
      if (predicate(state)) return state;
      if (state.state === 'unknown') throw new Error('Real shell integration is unavailable');
      await sleep(100);
    }
    throw new Error('Timed out: ' + JSON.stringify(runs.get(id)));
  };
  try {
    const first = start("console.log('HABITAT real output café ☕');setTimeout(()=>process.exit(0),1500)");
    await wait(first, value => value.state === 'running' && value.output.includes('HABITAT real output'));
    const success = await wait(first, value => value.state === 'completed');
    assert.equal(success.exitCode, 0); assert(success.output.includes('café ☕')); assert(success.elapsedMs >= 1500);
    results.push('PASS: real fresh terminal returns live Unicode output, running state, duration and exit code 0');
    const second = start("console.error('HABITAT synthetic failure');process.exit(7)");
    const failure = await wait(second, value => value.state === 'failed');
    assert.equal(failure.exitCode, 7); assert(failure.output.includes('HABITAT synthetic failure'));
    assert.equal(runs.get(first).state, 'completed');
    results.push('PASS: second fresh terminal returns error text and exit code 7 independently');
    const third = start("console.log('http://localhost:5173/');setInterval(()=>{},1000)");
    await wait(third, value => value.state === 'running' && value.output.includes('http://localhost:5173/'));
    await sleep(500);
    assert.equal(runs.get(third).state, 'running');
    terminals[2].dispose();
    await wait(third, value => value.state === 'closed' || value.endedAt !== null);
    results.push('PASS: long-running server stays running after URL output; closing its synthetic terminal ends monitoring');
    fs.writeFileSync(path.join(root, 'result.json'), JSON.stringify({ passed: true, results }, null, 2));
  } catch (error) {
    fs.writeFileSync(path.join(root, 'result.json'), JSON.stringify({ passed: false, results, error: error.stack }, null, 2));
    throw error;
  } finally {
    for (const terminal of terminals) terminal.dispose();
    runs.dispose();
  }
};
