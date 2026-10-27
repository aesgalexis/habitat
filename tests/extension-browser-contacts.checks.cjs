const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname,'../extension/browser-contacts.js'),'utf8');
let checks = 0;
const check = (condition,message) => { assert.ok(condition,message); checks++; };
const event = () => ({listeners:[],addListener(fn){this.listeners.push(fn);}});
const box = {getBoundingClientRect:()=>({width:80,height:20}),closest:()=>null};
function element(attributes={},text='') { return {...box,textContent:text,getAttribute:key=>attributes[key] || null}; }
let accountNodes = [element({'aria-label':'Google Account: Test Owner (owner@example.com)'})];
let senders = [element({email:'friend@example.com',name:'Friend 雪'},'Friend 雪'),element({email:'owner@example.com'},'Me')];
let contactRows = [];
let bodyReads = 0;
const document = {
  querySelector:()=>null,
  querySelectorAll(selector) {
    if (selector.includes('accounts.google.com')) return accountNodes;
    if (selector.startsWith('tr.zA')) return senders;
    if (selector.startsWith('[role="main"] [role="row"][data-id]')) return contactRows;
    throw Error('Unexpected page scan: '+selector);
  }
};
Object.defineProperty(document,'body',{get(){bodyReads++;throw Error('Mail bodies must never be accessed.');}});
const location = {origin:'https://mail.google.com',pathname:'/mail/u/0/',href:'https://mail.google.com/mail/u/0/'};
let now=1000,consent=true,queryCount=0,readCount=0;
let held=null,setupCount=0;
let grants=new Set();
let tabs=[];
const packets=[];
const socket={readyState:1,send(value){packets.push(JSON.parse(value));}};
const chrome={
  runtime:{getURL:page=>'chrome-extension://synthetic/'+page},
  tabs:{onUpdated:event(),onRemoved:event(),query:async()=>{queryCount++;return tabs;},
    create:async options=>{check(options.url.startsWith('chrome-extension://synthetic/contacts-settings.html?source='),'Only a requested extension permission page may be created.');setupCount++;},
    update:()=>{throw Error('Reading contacts must not navigate or focus tabs.');},remove:()=>{throw Error('Reading contacts must not close tabs.');}},
  permissions:{onAdded:event(),onRemoved:event(),contains:async options=>options.origins.every(origin=>grants.has(origin))},
  scripting:{executeScript:async options=>{readCount++;const tab=tabs.find(t=>t.id===options.target.tabId);if(held){const wait=held;held=null;await wait;}
    if(options.func.name==='readFacebookContacts')return [{result:tab.page || {contacts:[{name:'FB friend ☕',photoUrl:'https://scontent.fbcdn.net/p.jpg',link:'https://www.facebook.com/friend.person'}]}}];
    return [{result:tab.page || {accountId:'owner@example.com',accountName:'owner@example.com',contacts:[{name:'Google friend',email:'google@example.com',page:'Gmail'}]}}];}}
};
const context=vm.createContext({chrome,document,location,URL,TextEncoder,WebSocket:{OPEN:1},getComputedStyle:()=>({visibility:'visible'}),Date:{now:()=>now},
  habitatConnectionEnabled:async()=>consent,readFacebookContacts:function readFacebookContacts(){},console});
vm.runInContext(source,context);
function read(){return vm.runInContext('readGoogleBrowserContacts()',context);}
let result=read();
check(result.accountId==='owner@example.com'&&result.contacts.length===1&&result.contacts[0].name==='Friend 雪','Gmail reads the loaded correspondent and its account, preserving Unicode and excluding the owner.');
check(result.contacts[0].email==='friend@example.com'&&bodyReads===0,'Read only the sender address/name, never subjects, snippets or bodies.');
accountNodes=[];
check(read().error==='account-unavailable','An account position cannot substitute for the signed-in email identity.');
accountNodes=[element({'aria-label':'Google Account (one@example.com)'}),element({'aria-label':'Google Account (two@example.com)'})];
check(read().error==='account-unavailable','Ambiguous account identity must not join different accounts.');
accountNodes=[element({'aria-label':'Cuenta de Google: Owner (owner@example.com)'})];
senders=[{...element({email:'hidden@example.com'},'Hidden'),getBoundingClientRect:()=>({width:0,height:0})}];
check(read().contacts.length===0,'Hidden sender elements are not collected.');
location.origin='https://contacts.google.com';location.pathname='/';
const fields=[element({},'Amiga José'),element({},'one@example.com'),element({},'two@example.com'),element({},'+34 600 111 222')];
contactRows=[{...element({'data-id':'c123456789','data-name':'Amiga José'}),querySelector:()=>null,
  querySelectorAll:selector=>selector.includes('mailto:')?[]:fields}];
result=read();
check(result.contacts.length===2&&result.contacts.every(r=>r.name==='Amiga José'&&r.phone==='+34 600 111 222'&&r.id==='c123456789'),'Loaded Google Contacts rows preserve multiple addresses, phone and stable source ID.');
location.origin='https://example.test';
check(read().error==='page-changed','Readers reject unexpected origins.');

async function tick(config={facebook:true,google:true}) {context.socket=socket;context.config=config;await vm.runInContext('browserContacts.tick(socket,config)',context);}
const states=()=>packets.findLast(p=>p.states)?.states;
const snapshots=()=>packets.filter(p=>p.snapshots).flatMap(p=>p.snapshots);
(async()=>{
  await tick();
  check(queryCount===0&&readCount===0&&states().every(s=>s.state==='permission'),'No site is queried or injected before optional permission exists.');
  grants=new Set(['https://www.facebook.com/*','https://facebook.com/*','https://mail.google.com/*','https://contacts.google.com/*']);now+=30001;
  await tick();
  check(readCount===0&&setupCount===0&&states().every(s=>s.state==='waiting-for-tab'),'No provider tab means no reads, navigation, background tab creation or fake contacts.');
  tabs=[{id:1,url:'https://www.facebook.com/',status:'complete'},{id:2,url:'https://mail.google.com/mail/u/0/',status:'complete'},
    {id:3,url:'https://example.test',status:'complete'},{id:4,url:'https://mail.google.com/mail/u/1/',status:'complete',discarded:true}];
  now+=30001;await tick();
  check(readCount===2&&snapshots().length===2&&snapshots()[0].contacts[0].name==='FB friend ☕','Enabled open provider tabs populate the agenda without an import action, skipping unrelated/discarded tabs.');
  check(snapshots()[0].accountId==='unidentified'&&snapshots()[0].accountName.includes('not identified'),'Unknown Facebook account provenance is labelled honestly, never inferred from a visited friend.');
  const before=readCount;await tick();
  check(readCount===before,'Frequent bridge ticks do not repeatedly scan page DOM.');
  now+=30001;await tick();
  check(snapshots().length===4,'Repeated observations can recover an interrupted desktop save; desktop stable keys prevent duplicate contacts.');
  tabs.push({id:5,url:'https://contacts.google.com/',status:'complete',page:{accountId:'work@example.com',accountName:'work@example.com',contacts:[{id:'c998877665',name:'Work contact',email:'work.friend@example.com',page:'Google Contacts'}]}});
  now+=30001;await tick();
  check(snapshots().find(s=>s.accountId==='work@example.com'),'Open Google accounts retain separate provenance.');
  tabs.push({id:6,url:'https://contacts.google.com/u/0/',status:'complete',page:{accountId:'owner@example.com',accountName:'owner@example.com',contacts:[{name:'Google friend',email:'google@example.com',phone:'+34600111222',page:'Google Contacts'}]}});
  now+=30001;await tick();
  check(snapshots().at(-2).accountId==='owner@example.com'||snapshots().some(s=>s.accountId==='owner@example.com'&&s.contacts.some(c=>c.phone)),'Google Contacts can enrich Gmail people without changing their email identity.');
  const stopBefore=readCount;await tick({facebook:false,google:false});
  check(readCount===stopBefore&&states().every(s=>s.state==='disabled'),'Source Off blocks all subsequent page reads.');
  let release;held=new Promise(resolve=>{release=resolve;});now+=30001;
  const pending=tick({facebook:true,google:false});await new Promise(resolve=>setImmediate(resolve));
  const snapshotCount=snapshots().length;
  await tick({facebook:false,google:false});release();await pending;
  check(snapshots().length===snapshotCount,'An in-flight read must not leak results after the source is disabled.');
  held=new Promise(resolve=>{release=resolve;});now+=30001;
  const revoked=tick({facebook:true,google:false});await new Promise(resolve=>setImmediate(resolve));
  const revokedCount=snapshots().length;
  grants.delete('https://www.facebook.com/*');
  chrome.permissions.onRemoved.listeners.forEach(fn=>fn());release();await revoked;
  check(snapshots().length===revokedCount,'Chrome permission revocation must discard a read already in flight.');
  consent=false;now+=30001;const consentReads=readCount;await tick({facebook:true,google:true});
  check(readCount===consentReads,'Global connection withdrawal stops provider work.');
  consent=true;now+=30001;
  const setup={id:'a'.repeat(32),provider:'google'};
  await tick({facebook:false,google:false,setup});await tick({facebook:false,google:false,setup});
  check(setupCount===1&&packets.some(p=>p.setupDone===setup.id),'A native enable request opens one extension-owned permission page and acknowledges it.');
  let requested=0;
  const elements={};
  const setupContext=vm.createContext({URL,location:{href:'chrome-extension://synthetic/contacts-settings.html?source=google'},
    document:{getElementById:id=>elements[id]??={textContent:'',disabled:false}},chrome:{
      permissions:{request:async options=>{requested++;check(options.origins.length===2&&options.origins.every(o=>o.includes('google.com/')),'Google enablement requests only its two disclosed optional origins.');return true;}},
      runtime:{sendMessage:async request=>{check(request.kind==='facebook-connect'&&request.restart,'After permission, reconnect the shared browser channel.');}}}});
  vm.runInContext(fs.readFileSync(path.join(__dirname,'../extension/contacts-settings.js'),'utf8'),setupContext);
  check(requested===0,'Viewing a contact permission page must not grant access.');
  await elements.enable.onclick();
  check(requested===1&&elements.status.textContent.includes('automatically'),'An explicit Chrome action grants optional access once and explains automatic updates.');
  console.log(`Automatic browser contacts checks passed: ${checks}. Synthetic pages only; no accounts or real messages.`);
})().catch(error=>{console.error(error);process.exitCode=1;});
