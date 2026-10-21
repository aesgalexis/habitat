const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const en = JSON.parse(fs.readFileSync(path.join(root, 'src/Habitat.Core/Localization/en.json'), 'utf8'));
const es = JSON.parse(fs.readFileSync(path.join(root, 'src/Habitat.Core/Localization/es.json'), 'utf8'));
assert.deepEqual(Object.keys(en).sort(), Object.keys(es).sort());
let references = 0;
function visit(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (['obj', 'bin'].includes(entry.name)) continue;
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) visit(file);
    else if (file.endsWith('.cs') && !file.endsWith('.Checks.cs')) {
      const source = fs.readFileSync(file, 'utf8');
      for (const match of source.matchAll(/UiText\.(?:Get|Format)\("([^"]+)"/g)) {
        assert.ok(Object.hasOwn(en, match[1]), `${path.relative(root, file)}: missing resource ${match[1]}`);
        references++;
      }
    }
  }
}
visit(path.join(root, 'src'));
for (const key of ['Appearance', 'Location', 'Shortcuts', 'Destinations', 'English', 'Greek', 'Spanish']) assert.ok(en[key] && es[key]);
console.log(`PASS: ${Object.keys(en).length} bilingual messages and ${references} source references; stable category and language keys.`);
