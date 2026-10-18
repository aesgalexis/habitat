const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync('extension/transport.js', 'utf8');
const identity = source.slice(source.indexOf('async function habitatBrowser()'), source.indexOf('chrome.alarms.onAlarm'));
(async () => {
  const cases = [
    [{userAgent:'Mozilla Chrome/130'}, 'chrome'],
    [{userAgent:'Mozilla Chrome/130 Edg/130'}, 'edge'],
    [{userAgent:'Mozilla Chrome/130',brave:{isBrave:async()=>true}}, 'brave'],
    [{userAgent:'Mozilla Chrome/130',userAgentData:{brands:[{brand:'Vivaldi'}]}}, 'vivaldi'],
    [{userAgent:'Mozilla Chrome/130 OPR/130'}, 'opera'],
    [{userAgent:'Mozilla Firefox/130'}, 'firefox']
  ];
  for (const [navigator, expected] of cases) {
    const context = vm.createContext({navigator});
    vm.runInContext(identity, context);
    assert.equal(await vm.runInContext('habitatBrowser()', context), expected);
  }
  console.log('Browser identity checks passed: 6.');
})().catch(error=>{console.error(error);process.exitCode=1;});
