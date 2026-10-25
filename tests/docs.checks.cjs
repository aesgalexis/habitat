'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { checkFile, checkRepository } = require('../scripts/check-docs.cjs');

const output = path.join(__dirname, '../artifacts/documentation-checks');
fs.mkdirSync(output, { recursive: true });
const root = fs.mkdtempSync(path.join(output, 'fixture-'));
fs.writeFileSync(path.join(root, 'Guide with spaces.md'), '# Guía útil\n\n## Repeated\n\n## Repeated\n');
fs.writeFileSync(path.join(root, 'source.cs'), '// fixture\n');
function check(source) {
  const file = path.join(root, 'README.md');
  fs.writeFileSync(file, source);
  return checkFile(root, file);
}
assert.deepEqual(check('[guide](Guide%20with%20spaces.md#guía-útil)\n[again](<Guide with spaces.md#repeated-1>)\n[source](source.cs)'), []);
assert.match(check('[gone](missing.md)')[0], /missing target/);
assert.match(check('[heading](Guide%20with%20spaces.md#missing)')[0], /missing Markdown heading/);
assert.match(check('[local](C:/private/file.md)')[0], /repository-relative/);
assert.match(check('[outside](../outside.md)')[0], /escapes the repository/);
assert.match(check('![image](missing.png)')[0], /missing target/);
assert.deepEqual(check('[guide][g]\n\n[g]: Guide%20with%20spaces.md'), []);
assert.match(check('[guide][missing]')[0], /undefined reference/);
assert.match(check('[guide][g]\n\n[g]: absent.cs')[0], /missing target/);
assert.deepEqual(check('```md\n[example](absent.md)\n```\n`[example](absent.md)`\n[remote](https://example.invalid/absent)'), []);
assert.match(check('[bad](%ZZ.md)')[0], /invalid URL encoding/);
assert.deepEqual(check('# Same page\n\n[here](#same-page)'), []);
assert.deepEqual(check('~~~md\n[example](absent.md)\n~~~'), []);
assert.match(check('[root](/private/file.md)')[0], /repository-relative/);
assert.match(check('[local](file:///private/file.md)')[0], /repository-relative/);
assert.deepEqual(check('\uFEFF# First heading\n\n[first](#first-heading)'), []);
fs.mkdirSync(path.join(root, 'docs'));
fs.writeFileSync(path.join(root, 'docs', 'nested.md'), '[missing](missing.cs)');
assert.ok(checkRepository(root).errors.some(error => /docs\/nested.md:1: missing target/.test(error)));
console.log('Documentation checker regression cases passed. Fixtures remain under artifacts/documentation-checks.');
