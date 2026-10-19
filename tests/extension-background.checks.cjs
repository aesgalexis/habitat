const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const extension = path.resolve(__dirname, '../extension');
const wireFixture = JSON.parse(fs.readFileSync(path.join(__dirname, "fixtures/bridge-v1.json"), "utf8"));
let checks = 0;
function check(value, message) { assert.ok(value, message); checks++; }
function signal() { const listeners = []; return {listeners,addListener(fn){listeners.push(fn);},removeListener(fn){const i=listeners.indexOf(fn);if(i>=0)listeners.splice(i,1);}}; }
async function settled() { for(let i=0;i<12;i++)await new Promise(resolve=>setImmediate(resolve)); }

async function workerChecks() {
  const first={id:101,windowId:1,url:'https://chatgpt.com/c/first',active:true,autoDiscardable:true};
  const second={id:202,windowId:2,url:'https://chatgpt.com/c/second',active:false,autoDiscardable:true};
  const local={habitatTab:202,habitatUrl:second.url,habitatKey:'key',habitatSession:{messageCount:999}};
  const session={},commands=[],sockets=[],timers=new Set(),activations=[],windowUpdates=[],setupPages=[];
  let allowActivation=false;
  let pageBusy=false,available=true,protocol=6,injections=0;
  const chrome={runtime:{id:'test-extension',onMessage:signal(),onInstalled:signal(),onStartup:signal(),getURL:p=>'chrome-extension://test-extension/'+p},
    alarms:{onAlarm:signal(),create:async()=>{}},storage:{onChanged:signal()},
    tabs:{onRemoved:signal(),onUpdated:signal(),
      query:async query=>query.active?[first,second].filter(t=>t.active):[],
      get:async id=>[first,second].find(t=>t.id===id),
      update:async(id,values)=>{check(Object.keys(values).every(k=>k==='autoDiscardable'||allowActivation&&k==='active'),'Activate only after an explicit external action; never navigate.');
        if(values.active)activations.push(id);Object.assign([first,second].find(t=>t.id===id),values);},
      create:async values=>{check(values.url==='chrome-extension://test-extension/welcome.html','Only the consent page may open automatically.');setupPages.push(values.url);return {id:999};},remove:async()=>{},
      sendMessage:async(id,request)=>{commands.push({id,request});if(!available)throw Error('No content script');
        return request.kind==='habitat:probe'?{protocol,ready:true,busy:pageBusy,url:[first,second].find(t=>t.id===id).url,model:'test-model'}:{accepted:true};}},
    windows:{get:async()=>({state:'minimized'}),update:async(id,values)=>{windowUpdates.push({id,values});}},
    scripting:{executeScript:async()=>{injections++;available=true;protocol=6;}}
  };
  function storage(data,area){return {get:async keys=>Object.fromEntries((Array.isArray(keys)?keys:[keys]).map(k=>[k,data[k]])),
    set:async values=>{const changes={};for(const[k,v]of Object.entries(values)){if(data[k]!==v)changes[k]={};data[k]=v;}
      if(Object.keys(changes).length)queueMicrotask(()=>chrome.storage.onChanged.listeners.forEach(fn=>fn(changes,area)));},
    remove:async keys=>{for(const k of Array.isArray(keys)?keys:[keys])delete data[k];}};}
  chrome.storage.local=storage(local,'local');chrome.storage.session=storage(session,'session');
  class Socket{static OPEN=1;constructor(url){this.url=url;this.readyState=0;this.sent=[];sockets.push(this);queueMicrotask(()=>{this.readyState=1;this.onopen?.();});}send(s){this.sent.push(JSON.parse(s));}close(){this.readyState=3;this.onclose?.();}}
  const context=vm.createContext({chrome,WebSocket:Socket,URL,console,Promise,queueMicrotask,
    setTimeout:(fn,ms)=>{const timer=setTimeout(fn,ms);timers.add(timer);return timer;},clearTimeout:id=>{clearTimeout(id);timers.delete(id);},
    fetch:async url=>{check(url==='http://127.0.0.1:8765/pair','Pair only with the local desktop.');return {ok:true,json:async()=>({key:'key'})};}});
  context.importScripts=(...files)=>files.forEach(file=>vm.runInContext(fs.readFileSync(path.join(extension,file),'utf8'),context));
  vm.runInContext(fs.readFileSync(path.join(extension,'background.js'),'utf8'),context);
  await settled();
  check(sockets.length===0 && commands.length===0 && injections===0 && local.habitatTab===202,
    'A fresh installation cannot connect, inspect the chat or replace its binding before consent.');
  chrome.runtime.onInstalled.listeners[0]();await settled();
  check(setupPages.length===1 && sockets.length===0,'Installation opens the disclosure page and leaves every channel disconnected.');
  await chrome.storage.local.set({habitatConsentVersion:1});await settled();
  check(local.habitatTab===101,'Use the active chat instead of the previously dedicated chat.');
  const chatSocket=()=>sockets.findLast(socket=>socket.url.endsWith('/bridge'));
  async function tick(message=null){context.socket=chatSocket();context.message=message;await vm.runInContext('transportTick(socket,message)',context);await settled();}
  await tick({id:'one',text:'test'});
  check(commands.at(-1).id===101 && commands.at(-1).request.kind==='habitat:deliver','Deliver only to the active existing chat.');
  check(!('chatTitle' in commands.at(-1).request),'Never rename a chat.');
  check(local.habitatSession.messageCount===999,'Ignore old rotation state; no time or message limit changes chats.');
  pageBusy=true;first.active=false;second.active=true;
  await tick();
  check(local.habitatTab===101,'Finish the current response in its original chat after a tab switch.');
  pageBusy=false;
  const previousDeliveries=commands.filter(c=>c.request.kind==='habitat:deliver').length;
  await tick({id:'queued-before-tab-switch',text:'test'});
  check(local.habitatTab===101 && commands.filter(c=>c.request.kind==='habitat:deliver').length===previousDeliveries+1 &&
    commands.at(-1).id===101,'A queued message keeps its original chat even if a different ChatGPT tab becomes active.');
  await tick();
  check(local.habitatTab===202,'Follow the newly active ChatGPT tab once the response has finished.');
  await tick({id:'two',text:'test'});
  check(commands.at(-1).id===202 && commands.at(-1).request.expectedUrl===second.url,'Route the next message to the newly active chat.');
  second.url='https://chatgpt.com/c/another';
  await tick();await vm.runInContext('startTransport()',context);await settled();
  await tick({id:'three',text:'test'});
  check(commands.at(-1).request.expectedUrl===second.url,'Follow a manually selected conversation in the same tab.');
  available=false;await tick();
  check(injections===1,'Recover the content script after extension reload.');
  protocol=5;await tick();
  check(injections===2,'Upgrade old page code without requiring a page reload.');
  second.active=false;
  const sent=commands.filter(c=>c.request.kind==='habitat:deliver').length;
  await tick({id:'blocked',text:'test'});
  check(chatSocket().sent.at(-1).ready===true && commands.filter(c=>c.request.kind==='habitat:deliver').length===sent+1 &&
    commands.at(-1).id===202,'Keep the linked background chat ready and send while no ChatGPT tab is active.');
  await vm.runInContext('closeTransport()',context);await settled();
  await vm.runInContext('startTransport()',context);await settled();
  check(local.habitatTab===202 && chatSocket().readyState===1,'Reconnect to the same background chat after the desktop/worker restarts.');
  await tick({id:'background-reconnected',text:'test'});
  check(commands.at(-1).id===202 && commands.at(-1).request.kind==='habitat:deliver',
    'The reconnected background binding can deliver without activation.');
  check(activations.length===0 && windowUpdates.length===0,'Regular sends keep the browser in the background.');
  allowActivation=true;
  await tick({id:'external',text:'current unsent draft',openExternally:true});
  check(activations.at(-1)===202 && windowUpdates.at(-1).id===2 &&
    windowUpdates.at(-1).values.state==='normal' && windowUpdates.at(-1).values.focused,
    'An external send restores the exact linked window and selects its tab.');
  const focusedCount=activations.length;
  await tick({id:'external',text:'current unsent draft',openExternally:true});
  check(activations.length===focusedCount,'Redelivery never refocuses the window.');
  context.openId='empty-open';context.socket=chatSocket();
  const deliveriesBeforeOpen=commands.filter(c=>c.request.kind==='habitat:deliver').length;
  await vm.runInContext('transportTick(socket,null,openId)',context);await settled();
  check(activations.length===focusedCount+1 && commands.filter(c=>c.request.kind==='habitat:deliver').length===deliveriesBeforeOpen &&
    chatSocket().sent.some(p=>p.kind==='chat-opened'&&p.id==='empty-open'&&p.opened),
    'An empty external action only opens the linked chat and acknowledges it.');
  await vm.runInContext('transportTick(socket,null,openId)',context);await settled();
  check(activations.length===focusedCount+1,'Repeated open requests are idempotent.');
  allowActivation=false;
  second.active=false;
  second.frozen=true;
  const beforeFrozen=commands.filter(c=>c.request.kind==='habitat:deliver').length;
  await tick({id:'frozen',text:'test'});
  check(chatSocket().sent.at(-1).ready===false && commands.filter(c=>c.request.kind==='habitat:deliver').length===beforeFrozen,
    'A suspended tab is reported and never receives an ambiguous delivery.');
  second.frozen=false;
  local.habitatUrl='https://chatgpt.com/c/mismatched';
  context.savedBinding=local;
  check(await vm.runInContext('chooseBoundTab(savedBinding)',context)===null,'Do not reconnect a background tab that changed conversation.');
  local.habitatUrl=second.url;
  local.habitatTab=999;
  check(await vm.runInContext('chooseBoundTab(savedBinding)',context)===null,'Do not substitute a random background chat when the linked tab is missing.');
  local.habitatTab=202;
  await chrome.storage.local.set({habitatDisabled:true});await settled();
  check((await vm.runInContext('startTransport()',context)).error.includes('disconnected'),'Respect Disconnect.');
  check(sockets.every(socket=>socket.readyState===3),'Disconnect closes ChatGPT, Google, YouTube and Translate channels.');
  check(first.autoDiscardable===true && second.autoDiscardable===true,'Restore tab protection on disconnect.');
  for(const timer of timers)clearTimeout(timer);
}

async function hiddenPageChecks() {
  const listeners=signal(),events=[],assistant=[],users=[];
  let clicks=0,domFocus=null,previewOpen=false,previewHeading='Vista previa de imagen',previewClosed=0;
  class TextArea {constructor(){this._value='';this.disabled=false;this.isConnected=true;this.isContentEditable=false;}
    get value(){return this._value;}set value(text){this._value=text;}
    getBoundingClientRect(){return {width:400,height:60};}getAttribute(){return null;}
    closest(selector){return selector.includes('unified-composer')?root:null;}focus(){domFocus=this;}dispatchEvent(){} }
  const box=new TextArea();
  const misleading=new TextArea();misleading.closest=()=>({});
  const send={disabled:false,tagName:'BUTTON',id:'send',getBoundingClientRect:()=>({width:30,height:30}),getAttribute:()=>null,
    click:()=>{clicks++;box.value='';users.push({});assistant.push({innerText:'Synthetic reply',matches:()=>false,querySelector:()=>null,
      getAttribute:name=>name==='data-content-search-unit-key'?'reply:assistant':null,
      closest:()=>({matches:()=>true,getAttribute:()=> 'completed'})});}};
  const root={querySelector:()=>send};
  const preview={getBoundingClientRect:()=>({width:400,height:400}),
    querySelector:()=>({innerText:previewHeading}),
    querySelectorAll:()=>[{getAttribute:()=> 'Cerrar',click:()=>{previewOpen=false;previewClosed++;}}]};
  const document={visibilityState:'hidden',hasFocus:()=>false,
    querySelectorAll:selector=>{
      if(selector==='#prompt-textarea')return previewOpen?[]:[misleading,box];
      if(selector==='[role="dialog"]')return previewOpen?[preview]:[];
      if(selector==='[data-content-search-unit-key$=":assistant"]')return assistant;
      if(selector==='[data-content-search-unit-key$=":user"]')return users;
      return [];
    },querySelector:()=>null};
  const chrome={runtime:{id:'test-extension',onMessage:listeners,sendMessage:async request=>{
    if(request.kind==='event')events.push(request.event);
    if(request.kind==='delay')check(request.ms<=5000,'Response/editing delays are delegated to the extension worker.');
    return {ok:true};}}};
  const context=vm.createContext({window:{addEventListener(){}},document,chrome,location:{origin:'https://chatgpt.com',pathname:'/c/existing',href:'https://chatgpt.com/c/existing'},
    HTMLTextAreaElement:TextArea,Event,InputEvent:Event,KeyboardEvent:Event,getComputedStyle:()=>({display:'block',visibility:'visible'}),
    setInterval:()=>{throw Error('A hidden page must never own the bridge polling timer');},setTimeout:()=>{throw Error('A hidden page must never own response timers');},console,Promise});
  const source=fs.readFileSync(path.join(extension,'content.js'),'utf8');
  vm.runInContext('window.__habitatBridge={protocol:4,alive(){throw Error("Extension context invalidated");},stop(){throw Error("Extension context invalidated");}}',context);
  vm.runInContext(source,context,{filename:'content.js'});
  let probe;
  listeners.listeners[0]({kind:'habitat:probe'},{id:'test-extension'},result=>probe=result);
  check(probe.ready===true && probe.url.endsWith('/c/existing'),'A hidden and unfocused page can acknowledge readiness.');
  previewOpen=true;
  listeners.listeners[0]({kind:'habitat:probe'},{id:'test-extension'},result=>probe=result);
  check(probe.ready===true && previewClosed===1,'Close a blocking image preview and recover the real composer.');
  previewOpen=true;previewHeading='Account settings';
  listeners.listeners[0]({kind:'habitat:probe'},{id:'test-extension'},result=>probe=result);
  check(probe.ready===false && previewClosed===1,'Never dismiss unrelated account or permission dialogs during recovery.');
  previewOpen=false;
  let accepted;
  listeners.listeners[0]({kind:'habitat:deliver',expectedUrl:'https://chatgpt.com/c/existing',message:wireFixture.message},
    {id:'test-extension'},result=>accepted=result);
  await settled();
  check(accepted.accepted===true && domFocus===box && clicks===1,'A background command edits and sends only through the actual composer.');
  check(events.map(e=>e.type).join(',')==='received,started,response' && events.at(-1).text==='Synthetic reply','The response returns in both directions with all page timers disabled.');
  assert.deepEqual(JSON.parse(JSON.stringify(events)), wireFixture.events, 'Desktop and extension event fixtures agree.');
  check(wireFixture.protocolVersion === 1, 'Desktop transport fixture remains compatible.');
  listeners.listeners[0]({kind:'habitat:deliver',expectedUrl:'https://chatgpt.com/c/existing',message:wireFixture.message},
    {id:'test-extension'},result=>accepted=result);
  await settled();check(accepted.accepted===false && clicks===1,'Deduplicate commands across transport retries.');
  vm.runInContext(source,context,{filename:'content.js'});
  check(listeners.listeners.length===1,'Idempotent reinjection must not create a second sender or interrupt an active controller.');
  listeners.listeners[0]({kind:'habitat:deliver',expectedUrl:'https://chatgpt.com/c/wrong',message:{id:'synthetic-two',text:'never send'}},
    {id:'test-extension'},result=>accepted=result);
  check(accepted.error && clicks===1,'Reject commands if the conversation no longer matches.');
}

(async()=>{
  await workerChecks();await hiddenPageChecks();
  console.log(`Extension background checks passed: ${checks}. Synthetic pages only; no real ChatGPT message was sent.`);
})().catch(error=>{console.error(error);process.exitCode=1;});
