const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const extension = path.resolve(__dirname, '../extension');
let checks = 0;
function check(value, message) { assert.ok(value, message); checks++; }
function signal() { const listeners = []; return {listeners,addListener(fn){listeners.push(fn);},removeListener(fn){const i=listeners.indexOf(fn);if(i>=0)listeners.splice(i,1);}}; }
async function settled() { for(let i=0;i<12;i++)await new Promise(resolve=>setImmediate(resolve)); }

async function workerChecks() {
  const first={id:101,url:'https://chatgpt.com/c/first',active:true,autoDiscardable:true};
  const second={id:202,url:'https://chatgpt.com/c/second',active:false,autoDiscardable:true};
  const local={habitatTab:202,habitatUrl:second.url,habitatKey:'key',habitatSession:{messageCount:999}};
  const session={},commands=[],sockets=[],timers=new Set();
  let pageBusy=false,available=true,protocol=6,injections=0;
  const chrome={runtime:{id:'test-extension',onMessage:signal(),onInstalled:signal(),onStartup:signal(),getURL:p=>'chrome-extension://test-extension/'+p},
    alarms:{onAlarm:signal(),create:async()=>{}},storage:{onChanged:signal()},
    tabs:{onRemoved:signal(),onUpdated:signal(),
      query:async query=>query.active?[first,second].filter(t=>t.active):[],
      get:async id=>[first,second].find(t=>t.id===id),
      update:async(id,values)=>{check(Object.keys(values).every(k=>k==='autoDiscardable'),'Never navigate or activate Chrome tabs.');Object.assign([first,second].find(t=>t.id===id),values);},
      create:async()=>{throw Error('Must never create a tab');},remove:async()=>{},
      sendMessage:async(id,request)=>{commands.push({id,request});if(!available)throw Error('No content script');
        return request.kind==='habitat:probe'?{protocol,ready:true,busy:pageBusy,url:[first,second].find(t=>t.id===id).url,model:'test-model'}:{accepted:true};}},
    scripting:{executeScript:async()=>{injections++;available=true;protocol=6;}}
  };
  function storage(data,area){return {get:async keys=>Object.fromEntries((Array.isArray(keys)?keys:[keys]).map(k=>[k,data[k]])),
    set:async values=>{const changes={};for(const[k,v]of Object.entries(values)){if(data[k]!==v)changes[k]={};data[k]=v;}
      if(Object.keys(changes).length)queueMicrotask(()=>chrome.storage.onChanged.listeners.forEach(fn=>fn(changes,area)));},
    remove:async keys=>{for(const k of Array.isArray(keys)?keys:[keys])delete data[k];}};}
  chrome.storage.local=storage(local,'local');chrome.storage.session=storage(session,'session');
  class Socket{static OPEN=1;constructor(){this.readyState=0;this.sent=[];sockets.push(this);queueMicrotask(()=>{this.readyState=1;this.onopen?.();});}send(s){this.sent.push(JSON.parse(s));}close(){this.readyState=3;this.onclose?.();}}
  const context=vm.createContext({chrome,WebSocket:Socket,URL,console,Promise,queueMicrotask,
    setTimeout:(fn,ms)=>{const timer=setTimeout(fn,ms);timers.add(timer);return timer;},clearTimeout:id=>{clearTimeout(id);timers.delete(id);},
    fetch:async url=>{check(url==='http://127.0.0.1:8765/pair','Pair only with the local desktop.');return {ok:true,json:async()=>({key:'key'})};}});
  context.importScripts=(...files)=>files.forEach(file=>vm.runInContext(fs.readFileSync(path.join(extension,file),'utf8'),context));
  vm.runInContext(fs.readFileSync(path.join(extension,'background.js'),'utf8'),context);
  await settled();
  check(local.habitatTab===101,'Use the active chat instead of the previously dedicated chat.');
  async function tick(message=null){context.socket=sockets.at(-1);context.message=message;await vm.runInContext('transportTick(socket,message)',context);await settled();}
  await tick({id:'one',text:'test'});
  check(commands.at(-1).id===101 && commands.at(-1).request.kind==='habitat:deliver','Deliver only to the active existing chat.');
  check(!('chatTitle' in commands.at(-1).request),'Never rename a chat.');
  check(local.habitatSession.messageCount===999,'Ignore old rotation state; no time or message limit changes chats.');
  pageBusy=true;first.active=false;second.active=true;
  await tick();
  check(local.habitatTab===101,'Finish the current response in its original chat after a tab switch.');
  pageBusy=false;
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
  check(sockets.at(-1).sent.at(-1).ready===false && commands.filter(c=>c.request.kind==='habitat:deliver').length===sent,'Do not send to a background chat when no ChatGPT tab is active.');
  local.habitatDisabled=true;await vm.runInContext('closeTransport()',context);await settled();
  check((await vm.runInContext('startTransport()',context)).error.includes('disconnected'),'Respect Disconnect.');
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
  listeners.listeners[0]({kind:'habitat:deliver',expectedUrl:'https://chatgpt.com/c/existing',message:{id:'synthetic-one',text:'local-only text'}},
    {id:'test-extension'},result=>accepted=result);
  await settled();
  check(accepted.accepted===true && domFocus===box && clicks===1,'A background command edits and sends only through the actual composer.');
  check(events.map(e=>e.type).join(',')==='received,started,response' && events.at(-1).text==='Synthetic reply','The response returns in both directions with all page timers disabled.');
  listeners.listeners[0]({kind:'habitat:deliver',expectedUrl:'https://chatgpt.com/c/existing',message:{id:'synthetic-one',text:'local-only text'}},
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
