const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
let checks=0;
function check(value,label){assert.ok(value,label);checks++;}
function signal(){return {addListener(){}};}
const tabs=new Map([[50,{id:50,windowId:1,url:'https://translate.google.com/',status:'complete',active:false}],
  [99,{id:99,windowId:1,url:'https://example.com/',status:'complete',active:true}]]), session={};
const sockets=[],updates=[],creates=[],timers=new Set();
const windows=new Map([[1,{id:1,state:'minimized',focused:false}],[2,{id:2,state:'normal',focused:false}]]), windowUpdates=[];
const attached=new Set(),emulated=new Set(),debuggerCalls=[];
let clock=0,injections=0,output='',target='en',nextTabId=100,holdCreation=null;
let translatingWindow=1,translatingTab=50,attachFailure=false,commandFailure=false,holdInjection=null;
class TextArea {get value(){return this._value||'';}set value(text){this._value=text;}
  focus(){}dispatchEvent(){output=windows.get(translatingWindow).state==='minimized'&&!emulated.has(translatingTab)?'':
    target==='el'?'Γεια σου κόσμε\nΚαλημέρα':target==='es'?'Hola mundo\nBuenos días':'Hello world\nGood morning';}}
const source=new TextArea();
const span={get textContent(){return output;},getBoundingClientRect:()=>({width:200,height:30}),closest:()=>({getAttribute:()=>target})};
const copy={disabled:false,getBoundingClientRect:()=>({width:30,height:30})};
const chrome={runtime:{getManifest:()=>({version:'0.5.3'})},alarms:{onAlarm:signal(),create:async()=>{}},storage:{onChanged:signal(),
  local:{get:async()=>({habitatDisabled:false})},session:{get:async()=>session,set:async values=>Object.assign(session,values)}},
  debugger:{attach:async(target,version)=>{if(attachFailure)throw Error('Another debugger is attached');
      assert.equal(version,'1.3');assert.ok(tabs.get(target.tabId).url.startsWith('https://translate.google.com/'));
      debuggerCalls.push({method:'attach',tabId:target.tabId});attached.add(target.tabId);},
    sendCommand:async(target,method,params)=>{assert.equal(method,'Emulation.setFocusEmulationEnabled');
      if(commandFailure&&params.enabled)throw Error('Unsupported command');
      assert.ok(attached.has(target.tabId));debuggerCalls.push({method,tabId:target.tabId,enabled:params.enabled});
      if(params.enabled)emulated.add(target.tabId);else emulated.delete(target.tabId);},
    detach:async target=>{debuggerCalls.push({method:'detach',tabId:target.tabId});attached.delete(target.tabId);emulated.delete(target.tabId);}},
  windows:{get:async id=>({...windows.get(id)}),update:async(id,values)=>{windowUpdates.push({id,values});Object.assign(windows.get(id),values);return {...windows.get(id)};}},
  tabs:{get:async id=>{if(!tabs.has(id))throw Error('Tab closed');return {...tabs.get(id)};},
    query:async query=>[...tabs.values()].filter(tab=>(!query.url||tab.url.startsWith('https://translate.google.com/'))&&
      (query.windowId===undefined||tab.windowId===query.windowId)&&(!query.active||tab.active)).map(tab=>({...tab})),
    update:async(id,values)=>{updates.push({id,values});const tab=tabs.get(id);if(!tab)throw Error('Tab closed');
      if(values.active)for(const other of tabs.values())if(other.windowId===tab.windowId)other.active=false;
      Object.assign(tab,values);if(values.url)delete tab.pendingUrl;navigate(values.url);return {...tab};},
    create:async values=>{creates.push(values);if(holdCreation)await holdCreation;const tab={id:nextTabId++,windowId:1,url:values.url,status:'complete',active:values.active};
      tabs.set(tab.id,tab);navigate(values.url);return {...tab};}},
  scripting:{executeScript:async options=>{injections++;translatingTab=options.target.tabId;translatingWindow=tabs.get(translatingTab).windowId;
    const result=options.func(...options.args);if(holdInjection){const wait=holdInjection;holdInjection=null;await wait;}return [{result}];}}};
class Socket {static OPEN=1;constructor(url){this.url=url;this.readyState=1;this.sent=[];sockets.push(this);queueMicrotask(()=>this.onopen?.());}
  send(packet){this.sent.push(JSON.parse(packet));}close(){this.readyState=3;this.onclose?.();}}
const location={origin:'https://translate.google.com',href:'https://translate.google.com/?tl=en'};
function navigate(url){if(!url)return;const parsed=new URL(url);Object.assign(location,{origin:parsed.origin,href:url});target=parsed.searchParams.get('tl');source._value='';output='';}
const context=vm.createContext({chrome,WebSocket:Socket,URL,console,Promise,queueMicrotask,location,HTMLTextAreaElement:TextArea,Event,InputEvent:Event,
  Date:{now:()=>clock+=300},getComputedStyle:()=>({visibility:'visible'}),
  document:{querySelector:selector=>selector==='textarea[aria-label="Source text"]'?source:selector==='button[aria-label="Copy translation"]'?copy:null,
    querySelectorAll:selector=>selector==='span[jsname="W297wb"]'&&output?[span]:[]},
  setTimeout:(fn,ms)=>{const timer=setTimeout(fn,ms===300?0:ms);timers.add(timer);return timer;},
  clearTimeout:timer=>{clearTimeout(timer);timers.delete(timer);},
  fetch:async url=>{check(url==='http://127.0.0.1:8765/pair','Only local pairing may use fetch; translation is performed through the page.');return {ok:true,json:async()=>({key:'synthetic'})};}});

(async()=>{
  vm.runInContext(fs.readFileSync(path.resolve(__dirname,'../extension/translation.js'),'utf8'),context);
  for(let i=0;i<10;i++)await new Promise(resolve=>setImmediate(resolve));
  const socket=sockets[0];
  check(socket.url==='ws://127.0.0.1:8765/translate-bridge'&&socket.sent[0].translationProtocol===1,'Translation connects without choosing a ChatGPT tab.');
  context.socket=socket;
  for(const [index,language] of ['en','el','es'].entries()){
    context.job={id:String(index+1).padStart(32,'0'),text:'Hola mundo\nBuenos días',target:language};
    await vm.runInContext('dispatchTranslation(socket,job)',context);
    const result=socket.sent.at(-1);
    check(result.kind==='translation-result'&&result.id===context.job.id&&result.text===output,'Read the visible page result for '+language+' and return only its text.');
    check(source.value===context.job.text&&updates.some(update=>update.values.url?.includes('tl='+language)),'Paste the exact source and choose the target through the page URL.');
  }
  check(creates.length===0&&session.habitatTranslateTab===50&&tabs.get(99).active&&updates.every(update=>update.values.active===undefined),
    'Discover and reuse an open translator without changing the selected Chrome tab.');
  check(windows.get(1).state==='minimized'&&windowUpdates.length===0&&attached.size===0&&emulated.size===0&&
    debuggerCalls.filter(call=>call.enabled===true).length===3&&debuggerCalls.filter(call=>call.enabled===false).length===3,
    'Translate while Chrome stays minimized, without any window changes, then release every page override.');
  const before=injections;
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  check(injections===before,'Repeated delivery of a completed job resends its cached result without translating twice.');
  delete session.habitatTranslateTab;
  context.job={id:'4'.repeat(32),text:'Hola mundo',target:'en'};
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  check(creates.length===0&&session.habitatTranslateTab===50,'Losing session storage still reuses the open translator.');
  session.habitatTranslateTab=42;
  context.job={id:'5'.repeat(32),text:'Hola mundo',target:'el'};
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  check(creates.length===0&&session.habitatTranslateTab===50,'A closed saved tab falls back to another open translator.');
  tabs.get(50).pendingUrl='https://example.com/user-page';
  tabs.set(52,{id:52,windowId:2,url:'https://translate.google.com/',status:'complete',active:true});
  context.job={id:'a'.repeat(32),text:'Hola mundo',target:'en'};
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  check(creates.length===0&&session.habitatTranslateTab===52&&tabs.get(50).pendingUrl==='https://example.com/user-page',
    'If the saved tab is navigating away, reuse a translator in another window and preserve the unrelated page.');
  tabs.get(50).url='https://example.com/user-page';delete tabs.get(50).pendingUrl;tabs.delete(52);
  context.job={id:'6'.repeat(32),text:'Hola mundo',target:'en'};
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  const createdId=session.habitatTranslateTab;
  context.job={id:'7'.repeat(32),text:'Hola mundo',target:'es'};
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  check(creates.length===1&&creates[0].active===false&&session.habitatTranslateTab===createdId&&tabs.get(50).url==='https://example.com/user-page',
    'Create only when no translator remains open, then reuse the new tab on the next request.');
  tabs.delete(createdId);
  let releaseCreation;holdCreation=new Promise(resolve=>{releaseCreation=resolve;});
  context.job={id:'8'.repeat(32),text:'Old request',target:'en'};
  const oldRequest=vm.runInContext('dispatchTranslation(socket,job)',context);
  while(creates.length<2)await new Promise(resolve=>setImmediate(resolve));
  context.job={id:'9'.repeat(32),text:'New request',target:'el'};
  const newRequest=vm.runInContext('dispatchTranslation(socket,job)',context);
  releaseCreation();holdCreation=null;
  await Promise.all([oldRequest,newRequest]);
  check(creates.length===2&&socket.sent.at(-1).id===context.job.id&&socket.sent.at(-1).text===output,
    'Overlapping requests share a tab even when the first request is canceled while Chrome is opening it.');
  attachFailure=true;
  context.job={id:'c'.repeat(32),text:'Hola mundo',target:'en'};
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  check(socket.sent.at(-1).error==='background-unavailable'&&windowUpdates.length===0&&attached.size===0,
    'An existing debugger reports a recoverable failure without restoring or focusing Chrome.');
  attachFailure=false;commandFailure=true;
  context.job={id:'d'.repeat(32),text:'Hola mundo',target:'en'};
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  check(socket.sent.at(-1).error==='background-unavailable'&&attached.size===0&&emulated.size===0,
    'A failed focus emulation command detaches the partially opened session.');
  commandFailure=false;
  const debuggerApi=chrome.debugger;delete chrome.debugger;
  context.job={id:'e'.repeat(32),text:'Hola mundo',target:'en'};
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  check(socket.sent.at(-1).error==='background-permission'&&windowUpdates.length===0,
    'A missing debugger permission explains recovery and never falls back to moving the browser.');
  chrome.debugger=debuggerApi;
  let releaseInjection;holdInjection=new Promise(resolve=>{releaseInjection=resolve;});
  const injectionsBeforeCancel=injections,sentBeforeCancel=socket.sent.length;
  context.job={id:'f'.repeat(32),text:'Canceled request',target:'en'};
  const canceled=vm.runInContext('dispatchTranslation(socket,job)',context);
  while(injections===injectionsBeforeCancel)await new Promise(resolve=>setImmediate(resolve));
  vm.runInContext('translationJob=null',context);releaseInjection();await canceled;
  check(attached.size===0&&emulated.size===0&&socket.sent.length===sentBeforeCancel&&windowUpdates.length===0,
    'Cancel releases page emulation without returning a stale result or changing browser windows.');
  const count=injections;
  context.job={id:'b'.repeat(32),text:'Never send',target:'fr'};
  await vm.runInContext('dispatchTranslation(socket,job)',context);
  check(count===injections,'Unsupported target languages are rejected before touching the browser.');
  for(const timer of timers)clearTimeout(timer);
  console.log('Translation extension checks passed: '+checks+'. Synthetic DOM only; no remote translation request.');
})().catch(error=>{for(const timer of timers)clearTimeout(timer);console.error(error);process.exitCode=1;});
