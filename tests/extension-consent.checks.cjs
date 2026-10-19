const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../extension');
const source = name => fs.readFileSync(path.join(root, name), 'utf8');
const signal = () => ({listeners:[],addListener(fn){this.listeners.push(fn);}});
let checks = 0;
function check(value, message) { assert.ok(value, message); checks++; }
async function settle() { for (let i=0;i<12;i++) await new Promise(resolve=>setImmediate(resolve)); }

(async () => {
  for (const consent of [undefined, 0, 2, '1']) {
    const context = vm.createContext({chrome:{storage:{local:{get:async()=>({habitatConsentVersion:consent})}}}});
    vm.runInContext(source('connection.js'), context);
    check(!await vm.runInContext('habitatConnectionEnabled()', context), 'A missing, stale or malformed consent cannot enable the bridge.');
  }
  for (const provider of ['translation.js', 'search.js', 'youtube.js']) {
    let pairings=0, sockets=0, reads=0;
    const local={};
    const chrome={alarms:{onAlarm:signal(),create:async()=>{}},storage:{onChanged:signal(),local:{get:async()=>local}},
      tabs:{get:async()=>{reads++;},query:async()=>{reads++;}},runtime:{getManifest:()=>({version:'0.9.0'})}};
    const context=vm.createContext({chrome,URL,console,Promise,
      fetch:async()=>{pairings++;return {ok:true,json:async()=>({key:'synthetic'})};},
      WebSocket:class {constructor(){sockets++;}},setTimeout:()=>0,clearTimeout(){}});
    vm.runInContext(source('connection.js'), context);
    if (provider!=='translation.js') vm.runInContext(source('search-transport.js'), context);
    vm.runInContext(source(provider), context);
    await settle();
    check(pairings===0 && sockets===0 && reads===0, provider+' cannot contact HABITAT or inspect pages before consent.');
    Object.assign(local,{habitatConsentVersion:1,habitatDisabled:true});
    for (const listener of chrome.storage.onChanged.listeners) listener({habitatDisabled:{}},'local');
    await settle();
    check(pairings===0 && sockets===0 && reads===0, provider+' remains disconnected after explicit Disable.');
  }
  const elements = Object.fromEntries(['enable','disable','status'].map(id=>[id,{textContent:'',disabled:false,listeners:{},addEventListener(event,fn){this.listeners[event]=fn;}}]));
  const saved={};
  const context=vm.createContext({document:{getElementById:id=>elements[id]},chrome:{storage:{local:{get:async()=>saved,set:async values=>Object.assign(saved,values)}}}});
  vm.runInContext(source('welcome.js'), context);await settle();
  check(!saved.habitatConsentVersion && !elements.enable.disabled,'Viewing the disclosure does not grant consent.');
  await elements.enable.listeners.click();
  check(saved.habitatConsentVersion===1 && saved.habitatDisabled===false && elements.enable.disabled,
    'Only the explicit Enable connection action saves consent and enables the bridge.');
  await elements.disable.listeners.click();
  check(saved.habitatDisabled===true && !elements.enable.disabled,'Keep disconnected can revoke the active connection.');
  console.log('Extension consent checks passed: '+checks+'. No browser or account data was used.');
})().catch(error=>{console.error(error);process.exitCode=1;});
