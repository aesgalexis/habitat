#!/usr/bin/env node
'use strict';

const fs = require('node:fs');
const path = require('node:path');

function prose(source) {
  let fence;
  return source.replace(/^\uFEFF/, '').split(/\r?\n/).map(line => {
    const marker = line.match(/^ {0,3}(`{3,}|~{3,})/);
    if (marker && !fence) { fence = marker[1]; return ''; }
    if (fence) {
      if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length &&
          line.slice(marker[0].length).trim() === '') fence = undefined;
      return '';
    }
    return line;
  }).join('\n');
}

function anchors(source) {
  const result = new Set();
  const counts = new Map();
  for (const line of prose(source).split('\n')) {
    const heading = line.match(/^ {0,3}#{1,6}\s+(.+?)(?:\s+#+)?\s*$/);
    if (!heading) continue;
    const slug = heading[1].replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
      .replace(/<[^>]*>/g, '').toLowerCase().replace(/[^\p{L}\p{N}_\-\s]/gu, '').replace(/\s/g, '-');
    const count = counts.get(slug) || 0;
    counts.set(slug, count + 1);
    result.add(slug + (count ? `-${count}` : ''));
  }
  for (const match of prose(source).matchAll(/<(?:a|[a-z][\w-]*)\b[^>]*\b(?:id|name)=["']([^"']+)["']/gi)) result.add(match[1]);
  return result;
}

function checkFile(root, file) {
  const source = prose(fs.readFileSync(file, 'utf8')).replace(/(`+)[^`\n]*?\1/g, '');
  const errors = [];
  const references = new Map();
  const label = value => value.trim().replace(/\s+/g, ' ').toLowerCase();
  const at = index => `${path.relative(root, file).replace(/\\/g, '/')}:${source.slice(0, index).split('\n').length}`;
  function verify(raw, index) {
    let target = raw.replace(/\\([() ])/g, '$1');
    if (/^(?:[a-z]:|[\\/]|file:)/i.test(target)) {
      errors.push(`${at(index)}: use a repository-relative link: ${raw}`); return;
    }
    if (/^[a-z][a-z\d+.-]*:/i.test(target)) return;
    const hashIndex = target.indexOf('#');
    let fragment = hashIndex < 0 ? '' : target.slice(hashIndex + 1);
    target = (hashIndex < 0 ? target : target.slice(0, hashIndex)).split('?')[0];
    try { target = decodeURIComponent(target); fragment = decodeURIComponent(fragment); }
    catch { errors.push(`${at(index)}: invalid URL encoding: ${raw}`); return; }
    const resolved = target ? path.resolve(path.dirname(file), target) : file;
    const relative = path.relative(root, resolved);
    if (relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
      errors.push(`${at(index)}: link escapes the repository: ${raw}`); return;
    }
    if (!fs.existsSync(resolved)) { errors.push(`${at(index)}: missing target: ${raw}`); return; }
    if (fragment && /\.md$/i.test(resolved) && !anchors(fs.readFileSync(resolved, 'utf8')).has(fragment)) {
      errors.push(`${at(index)}: missing Markdown heading: ${raw}`);
    }
  }
  for (const match of source.matchAll(/^ {0,3}\[([^\]\n]+)\]:\s*(?:<([^>\n]+)>|(\S+))/gm)) {
    references.set(label(match[1]), match[2] || match[3]);
    verify(match[2] || match[3], match.index);
  }
  for (const match of source.matchAll(/!?\[[^\]\n]*\]\(\s*(?:<([^>\n]+)>|((?:\\.|[^()\s]|\([^()\n]*\))+))(?:\s+["'][^\n]*?["'])?\s*\)/g)) {
    verify(match[1] || match[2], match.index);
  }
  for (const match of source.matchAll(/!?\[([^\]\n]+)\]\[([^\]\n]*)\]/g)) {
    const reference = references.get(label(match[2] || match[1]));
    if (!reference) errors.push(`${at(match.index)}: undefined reference: ${match[2] || match[1]}`);
  }
  // Shortcut references are links only when a matching definition exists.
  // Definitions have already been checked, including unused ones.
  return errors;
}

function markdownFiles(root) {
  const files = fs.readdirSync(root, { withFileTypes: true })
    .filter(entry => entry.isFile() && /\.md$/i.test(entry.name)).map(entry => path.join(root, entry.name));
  function walk(directory) {
    if (!fs.existsSync(directory)) return;
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      if (entry.isSymbolicLink() || ['node_modules', 'bin', 'obj', '.git'].includes(entry.name)) continue;
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile() && /\.md$/i.test(entry.name)) files.push(full);
    }
  }
  for (const directory of ['docs', 'packaging', 'vscode-extension', '.github']) walk(path.join(root, directory));
  return files.sort();
}

function checkRepository(root) {
  const files = markdownFiles(root);
  return { files, errors: files.flatMap(file => checkFile(root, file)) };
}

if (require.main === module) {
  const result = checkRepository(path.resolve(__dirname, '..'));
  if (result.errors.length) {
    console.error(result.errors.join('\n'));
    process.exitCode = 1;
  } else console.log(`Documentation checks passed: ${result.files.length} Markdown files; local targets and headings verified. External links were not requested.`);
}

module.exports = { anchors, checkFile, checkRepository };
