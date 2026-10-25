const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
let checks = 0;
const check = (value, message) => { assert.ok(value, message); checks++; };
const signal = () => ({listeners:[],addListener(fn){this.listeners.push(fn);}});
const source = fs.readFileSync(path.resolve(__dirname,'../extension/facebook.js'),'utf8');
const rect = {getBoundingClientRect:()=>({width:40,height:40})};
const heading = (label,order) => ({...rect,textContent:label,order,compareDocumentPosition:other=>other.order>order?4:2});
function photo(name, source, order, href=null, options={}) {
  const anchor = href ? {getAttribute:()=>href} : null;
  const row = {textContent:name,getAttribute:()=>name,matches:()=>false,querySelector:()=>anchor,querySelectorAll:()=>[image]};
  const image = {...rect,order,currentSrc:source,getAttribute:()=>'',closest:()=>options.noRow?null:row,
    compareDocumentPosition:other=>other.order>order?4:2};
  if(options.hidden)image.getBoundingClientRect=()=>({width:0,height:0});
  return image;
}
let rows=[], label='Contacts', login=false, missing=false;
const location={origin:'https://www.facebook.com',href:'https://www.facebook.com/',pathname:'/'};
const region={...rect,contains:()=>true,getAttribute:()=>'',querySelectorAll:selector=>selector==='h2,h3,[role="heading"]'?
  [heading('Sponsored',0),heading(label,10),heading('Group conversations',100)]:rows};
const document={querySelector:()=>login?{}:null,querySelectorAll:()=>missing?[]:[region]};
const consent={habitatConsentVersion:1};
let permission=false, clock=0, injections=0, queries=0, created=0, held=null;
const session={}, tabs=new Map([[1,{id:1,url:'https://www.facebook.com/some.person',status:'complete'}],
  [2,{id:2,url:'https://www.facebook.com/',status:'complete',active:true}]]);
const chrome={alarms:{onAlarm:signal(),create:async()=>{}},storage:{onChanged:signal(),
  local:{get:async()=>consent},session:{get:async()=>session,set:async values=>Object.assign(session,values)}},
  permissions:{onRemoved:signal(),onAdded:signal(),contains:async()=>permission},
  tabs:{query:async()=>{queries++;return [...tabs.values()];},get:async id=>{if(!tabs.has(id))throw Error('closed');return tabs.get(id);},
    create:async args=>{created++;check(args.active===false,'Facebook opens without taking focus.');const tab={id:10+created,status:'complete',...args};tabs.set(tab.id,tab);return tab;},
    update:()=>{throw Error('Personal Facebook tabs must never be navigated.');},remove:()=>{throw Error('Personal Facebook tabs must never be closed.');}},
  scripting:{executeScript:async args=>{injections++;const tab=tabs.get(args.target.tabId);const url=new URL(tab.url);
    Object.assign(location,{origin:url.origin,href:url.href,pathname:url.pathname});
    const result=args.func();if(held){const wait=held;held=null;await wait;}return [{result}];}}};
const sockets=[];
class Socket {static OPEN=1;constructor(url){this.url=url;this.sent=[];this.readyState=1;sockets.push(this);queueMicrotask(()=>this.onopen?.());}
  send(value){this.sent.push(JSON.parse(value));}close(){this.readyState=3;this.onclose?.();}}
const context=vm.createContext({chrome,URL,document,location,getComputedStyle:()=>({visibility:'visible'}),WebSocket:Socket,Promise,queueMicrotask,TextEncoder,AbortSignal,
  Date:{now:()=>clock+=1000},setTimeout:(fn,ms)=>{const timer=setTimeout(fn,ms===500?0:ms);if(ms!==500)timer.unref();return timer;},
  fetch:async url=>{check(url==='http://127.0.0.1:8765/pair','The Facebook worker uses HTTP only for local pairing.');return {ok:true,json:async()=>({key:'synthetic'})};}});
context.habitatConnectionEnabled=async()=>consent.habitatConsentVersion===1&&!consent.habitatDisabled;
let number=0;
async function settle(){for(let i=0;i<10;i++)await new Promise(resolve=>setImmediate(resolve));}
async function dispatch(){context.request={id:(++number).toString(16).padStart(32,'0')};
  await vm.runInContext('facebookTransport.dispatch(socket,request)',context);return context.socket.sent.at(-1);}
(async()=>{
  vm.runInContext(source,context);await settle();context.socket=sockets[0];
  check(context.socket.url.endsWith('/facebook-bridge')&&context.socket.sent[0].facebookProtocol===1,'Facebook has an independent authenticated channel.');
  let result=await dispatch();
  check(result.error==='permission'&&queries===0&&injections===0,'Optional access must be granted before inspecting any Facebook tab.');
  permission=true;
  rows=[photo('Ad','https://scontent.fbcdn.net/ad.jpg',1),photo('Ana ☕','https://scontent.fbcdn.net/ana.jpg',20,'/ana.person?tracking=1'),
    photo('Pepe','https://scontent.fbcdn.net/pepe.jpg',21),photo('Hidden','https://scontent.fbcdn.net/hide.jpg',22,null,{hidden:true}),
    photo('Unsafe','https://fbcdn.net.evil.test/a.jpg',23),photo('Unsafe link','https://scontent.fbcdn.net/ok.jpg',24,'https://evil.test/profile'),
    photo('Group','https://scontent.fbcdn.net/group.jpg',101),photo('Ana ☕','https://scontent.fbcdn.net/ana.jpg',25,'/ana.person')];
  result=await dispatch();
  check(result.contacts.length===3&&result.contacts[0].name==='Ana ☕'&&result.contacts[0].link==='https://www.facebook.com/ana.person',
    'Read only contact-section avatars, preserve names, canonicalize safe links and exclude ads, group conversations, hidden rows, unsafe photos and duplicates.');
  check(result.contacts[1].link===null&&result.contacts[2].link===null,'Contacts without a real Facebook link remain avatars without invented destinations.');
  check(result.diagnostic.stage==='section-read'&&result.diagnostic.photos===rows.length&&result.diagnostic.rows===4,
    'A rendered section reports only structural counts, independently from the returned contact data.');
  check(created===0&&tabs.get(1).url.endsWith('some.person'),'Use Home without navigating personal tabs.');
  const reads=injections;await vm.runInContext('facebookTransport.dispatch(socket,request)',context);
  check(injections===reads,'Repeated bridge ticks do not read the same request twice.');
  label='Contactos';result=await dispatch();check(result.contacts.length===3,'The Contacts panel can use its Spanish heading.');
  login=true;result=await dispatch();check(result.error==='login','A login page is surfaced without reading contacts or filling credentials.');login=false;
  tabs.delete(2);tabs.set(3,{id:3,url:'https://www.facebook.com/login/',status:'complete'});
  result=await dispatch();check(result.error==='login'&&created===0,'Reuse an existing login tab instead of opening another on every refresh.');
  tabs.delete(3);rows=[];result=await dispatch();
  check(created===1&&result.contacts.length===0,'An explicit read may open one background Home tab and return a genuinely empty panel.');
  await dispatch();check(created===1,'Subsequent reads reuse the Facebook tab.');
  missing=true;result=await dispatch();check(result.error==='contacts-unavailable','An unrecognized layout must not fabricate contacts or scan the feed.');
  check(result.diagnostic.stage==='section-missing'&&result.diagnostic.sections===0&&
    vm.runInContext('facebookTransport.status().lastRead.error',context)==='contacts-unavailable',
    'Missing sections are distinguishable from a disconnected transport and a successfully empty panel.');missing=false;
  let release;held=new Promise(resolve=>release=resolve);const before=context.socket.sent.length;
  const pending=dispatch();await settle();context.socket.onmessage({data:JSON.stringify({kind:'facebook-tick',request:null})});release();await pending;
  check(!context.socket.sent.slice(before).some(packet=>packet.kind==='facebook-result'),'Closing the sidebar discards an in-flight snapshot.');
  permission=false;result=await dispatch();check(result.error==='permission','Revoked access is checked again for each read.');
  const stalled=context.socket;
  clock+=11000;
  await vm.runInContext('facebookTransport.start()',context);await settle();
  context.socket=sockets.at(-1);
  check(stalled.readyState===3&&context.socket!==stalled,'An open socket without desktop ticks is replaced rather than treated as connected forever.');
  context.socket.onmessage({data:JSON.stringify({kind:'facebook-tick',request:null})});
  check(vm.runInContext('facebookTransport.status().connected',context),'Only a real Facebook desktop tick marks the reader connected.');
  const beforePermission=context.socket,readsBeforePermission=injections;
  permission=true;
  for(const listener of chrome.permissions.onAdded.listeners)listener({origins:['https://www.facebook.com/*']});
  await settle();context.socket=sockets.at(-1);
  context.socket.onmessage({data:JSON.stringify({kind:'facebook-tick',request:null})});await settle();
  check(context.socket!==beforePermission&&beforePermission.readyState===3&&injections===readsBeforePermission,
    'Granting Facebook access refreshes the channel immediately without reading any tab until the widget requests it.');
  consent.habitatDisabled=true;
  for(const listener of chrome.storage.onChanged.listeners)listener({habitatDisabled:{}},'local');await settle();
  check(context.socket.readyState===3,'Disconnecting HABITAT closes the independent Facebook channel.');
  const elements={};let grants=0,revocations=0;
  let workerResult={facebookProtocol:1,connected:true},reloaded=0,reconnects=0;
  const popup=vm.createContext({URL,document:{getElementById:id=>elements[id]??={textContent:'',disabled:false}},
    chrome:{storage:{local:{get:(keys,callback)=>{const state={habitatConsentVersion:1};callback?.(state);return Promise.resolve(state);}}},
      runtime:{sendMessage:async request=>{check(request.kind==='facebook-connect','Facebook reconnects its independent reader.');reconnects++;return workerResult;},reload:()=>reloaded++},
      permissions:{contains:async()=>false,request:async()=>{grants++;return true;},remove:async()=>{revocations++;return true;}}}});
  vm.runInContext(fs.readFileSync(path.resolve(__dirname,'../extension/popup.js'),'utf8'),popup);await settle();
  check(grants===0&&elements.facebook.textContent==='Enable Facebook contacts','Opening the popup cannot grant Facebook access.');
  await elements.facebook.onclick();
  check(grants===1&&elements.facebook.textContent==='Disable Facebook contacts','An explicit enable action requests optional access.');
  await settle();
  check(reconnects===1&&elements['facebook-status'].textContent.includes('connected to HABITAT'),'Granting access also connects the reader and reports the actual desktop connection separately.');
  workerResult={error:'Not a ChatGPT tab'};
  await vm.runInContext('connectFacebook()',popup);
  check(elements['facebook-reconnect'].textContent==='Reload extension for Facebook'&&reloaded===0,
    'An outdated running worker is detected without assuming that granted site access means a connected reader.');
  elements['facebook-reconnect'].onclick();
  check(reloaded===1,'The explicit recovery action reloads the extension rather than revoking existing permissions.');
  workerResult={facebookProtocol:1,connected:true,lastRead:{error:'contacts-unavailable',contactCount:null}};
  await vm.runInContext('connectFacebook()',popup);
  check(elements['facebook-status'].textContent.includes('cannot find')&&!elements['facebook-status'].textContent.includes('Open its widget'),
    'A connected transport must report its failed contact read instead of presenting it as ready.');
  workerResult={facebookProtocol:1,connected:true,lastRead:{error:null,contactCount:3}};
  await vm.runInContext('connectFacebook()',popup);
  check(elements['facebook-status'].textContent.includes('3 contacts received'),
    'A received contact count is reported separately from transport readiness.');
  await elements.facebook.onclick();
  check(revocations===1&&elements.facebook.textContent==='Enable Facebook contacts','The popup can revoke Facebook access independently.');
  console.log('Facebook extension checks passed: '+checks+'. Synthetic DOM and tabs only.');
})().catch(error=>{console.error(error);process.exitCode=1;});
