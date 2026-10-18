const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; }
function signal() { return {addListener(){}}; }
const session = {}, sockets = [], creates = [], updates = [], removals = [], timers = new Set();
const tabs = new Map([[1,{id:1,url:'https://www.youtube.com/watch?v=personal123',active:true,status:'complete'}],
  [2,{id:2,url:'https://www.google.com/search?q=personal',active:false,status:'complete'}]]);
let nextId = 10, clock = 0, injections = 0, mode = 'results', denied = false;
let loaded = 15, total = 47, scrolls = 0, holdCreation = null, holdRead = null;
let virtualStart = 0;
let attachFailure = false, emulationFailure = false;
const attached = new Set(), emulated = new Set();
const visible = {getBoundingClientRect:()=>({width:400,height:50})};
const location = {href:''};
const videoId = i => String(i).padStart(11,'v');
const node = textContent => ({...visible,textContent});
function row(index, options = {}) {
  const link = {...visible,textContent:'Video ☕ '+index,href:options.href || 'https://www.youtube.com/watch?v='+videoId(index)+'&pp=tracking'};
  return {...visible, getBoundingClientRect:()=>({width:options.hidden?0:400,height:50}),
    closest:()=>options.ad ? {} : null,
    querySelector:selector=> selector.startsWith('a#video-title[href]') ? link : selector.startsWith('#channel-name a') ? node('Channel '+index) :
      selector.startsWith('ytd-thumbnail-overlay-time-status-renderer #text') ? node('12:34') : node('Description & 雪 '+index),
    querySelectorAll:()=>[node('1K views'),node('2 days ago')]};
}
const continuation = {...visible,scrollIntoView:()=>{scrolls++;loaded=Math.min(total,loaded+20);}};
const root = {
  querySelectorAll:selector=> selector.startsWith('ytd-video-renderer') ? mode==='empty' ? [] : [row(1,{ad:true}),row(2,{hidden:true}),
    row(3,{href:'https://example.com/watch?v='+videoId(3)}),row(4,{href:'javascript:alert(1)'}),
    ...Array.from({length:Math.max(0,Math.min(loaded,total)-virtualStart)},(_,i)=>row(i+virtualStart+1)),row(1)] : selector==='ytd-continuation-item-renderer' && loaded<total ? [continuation] : [],
  querySelector:()=>mode==='empty'?{textContent:'No results found. Try different keywords.'}:null
};
const document = {querySelector:selector=>{
  if(selector==='ytd-search')return mode==='unknown'?null:root;
  if(mode==='consent' && selector.includes('consent-bump'))return {};
  if(mode==='blocked' && selector.includes('#captcha'))return {};
  return null;
}};
function navigate(url){location.href=url;loaded=15;virtualStart=0;delete context.__habitatYouTubeSearch;}
const chrome = {alarms:{onAlarm:signal(),create:async()=>{}},storage:{onChanged:signal(),local:{get:async()=>({})},
  session:{get:async()=>session,set:async values=>Object.assign(session,values),remove:async key=>{delete session[key];}}},
  tabs:{get:async id=>{if(!tabs.has(id))throw Error('Tab closed');return {...tabs.get(id)};},
    create:async values=>{creates.push(values);if(holdCreation)await holdCreation;const tab={id:nextId++,windowId:7,status:'complete',...values};
      tabs.set(tab.id,tab);navigate(values.url);return {...tab};},
    update:async(id,values)=>{updates.push({id,values});Object.assign(tabs.get(id),values);navigate(values.url);return {...tabs.get(id)};},
    remove:async id=>{removals.push(id);tabs.delete(id);}},
  scripting:{executeScript:async options=>{injections++;if(denied)throw Error('Cannot access contents: missing host permission');
    location.href=tabs.get(options.target.tabId).url;const result=options.func(...options.args);
    if(holdRead){const wait=holdRead;holdRead=null;await wait;}return [{result}];}},
  windows:{update:()=>{throw Error('Do not activate YouTube windows.');}},
  debugger:{attach:async(target,version)=>{assert.equal(version,'1.3');if(attachFailure)throw Error('Debugger in use');attached.add(target.tabId);},
    sendCommand:async(target,method,args)=>{assert.equal(method,'Emulation.setFocusEmulationEnabled');
      if(args.enabled && emulationFailure)throw Error('Emulation failed');
      if(args.enabled)emulated.add(target.tabId);else emulated.delete(target.tabId);},
    detach:async target=>{attached.delete(target.tabId);emulated.delete(target.tabId);}}
};
class Socket {static OPEN=1;constructor(url){this.url=url;this.sent=[];this.readyState=1;sockets.push(this);queueMicrotask(()=>this.onopen?.());}
  send(value){this.sent.push(JSON.parse(value));}close(){this.readyState=3;this.onclose?.();}}
const context=vm.createContext({chrome,location,document,URL,Promise,queueMicrotask,WebSocket:Socket,
  getComputedStyle:()=>({visibility:'visible'}),Date:{now:()=>clock+=300},
  setTimeout:(fn,ms)=>{const timer=setTimeout(fn,ms===300?0:ms);timers.add(timer);return timer;},
  fetch:async url=>{check(url==='http://127.0.0.1:8765/pair','YouTube uses HTTP only for local pairing, never video/search API calls.');
    return {ok:true,json:async()=>({key:'synthetic'})};}});
let number=0;
function job(query='café & snow 雪',start=0){context.job={id:(++number).toString(16).padStart(32,'0'),query,start};}
async function dispatch(){await vm.runInContext('youtubeSearchTransport.dispatch(socket,job)',context);return context.socket.sent.at(-1);}
async function reset(){context.resetId=(++number).toString(16).padStart(32,'0');
  await vm.runInContext('youtubeSearchTransport.reset(socket,resetId)',context);return context.socket.sent.at(-1);}
(async()=>{
  for(const file of ['search-transport.js','youtube.js'])vm.runInContext(fs.readFileSync(path.resolve(__dirname,'../extension/'+file),'utf8'),context);
  for(let i=0;i<10;i++)await new Promise(resolve=>setImmediate(resolve));
  context.socket=sockets[0];
  check(context.socket.url==='ws://127.0.0.1:8765/youtube-bridge' && context.socket.sent[0].searchProtocol===1,
    'YouTube uses its independent authenticated search channel.');
  job();let page=await dispatch();
  check(page.results.length===20 && page.results[0].title==='Video ☕ 1' && page.results[0].url==='https://www.youtube.com/watch?v='+videoId(1) &&
    page.results[19].url.endsWith(videoId(20)) && page.nextStart===20,
    'Read ordered canonical video links and titles, skipping ads, hidden rows, unsafe hosts and duplicates.');
  check(page.results[0].snippet==='Channel 1 · 12:34 · 1K views · 2 days ago\nDescription & 雪 1',
    'Preserve channel, duration, views, age and plain video description.');
  const own=session.habitatYouTubeTab;
  check(creates.length===1 && creates[0].active===false && tabs.get(1).active && tabs.get(2).url.includes('personal') && scrolls>0,
    'Load continuation in a dedicated inactive tab while preserving personal YouTube and Google tabs.');
  check(new URL(tabs.get(own).url).searchParams.get('search_query')===context.job.query,'Encode the exact Unicode YouTube query.');
  const reads=injections;await dispatch();check(reads===injections,'A repeated video job resends cached results without reading twice.');
  job('café & snow 雪',20);page=await dispatch();
  check(page.results[0].url.endsWith(videoId(21)) && page.results.length===20 && page.nextStart===40 && updates.length===0,
    'Next page loads more videos in the same page without restarting or replaying a video.');
  job('café & snow 雪',40);page=await dispatch();
  check(page.results.length===7 && page.results[0].url.endsWith(videoId(41)) && page.nextStart===null,'The final video page has no next-page action.');
  virtualStart=20;
  job('café & snow 雪',20);page=await dispatch();
  check(page.results[0].url.endsWith(videoId(21)) && updates.length===0,'Previous page returns the already loaded sequence.');
  tabs.get(own).url+='&app=desktop';
  job('café & snow 雪',0);page=await dispatch();
  check(page.results[0].url.endsWith(videoId(1)) && updates.length===0,'Previous page preserves its original order after YouTube recycles offscreen rows or adds URL parameters.');
  const initialWindow=tabs.get(own).windowId;
  job('A different query & café 雪');page=await dispatch();
  check(page.results.length===20 && creates.length===1 && session.habitatYouTubeTab===own &&
    updates.at(-1).id===own && tabs.get(own).windowId===initialWindow &&
    new URL(tabs.get(own).url).searchParams.get('search_query')===context.job.query,
    'A different YouTube query navigates the same dedicated tab in the same window without creating another tab.');
  const consentCreates=creates.length;
  tabs.get(own).url='https://consent.youtube.com/m';
  job('Next query after consent');page=await dispatch();
  check(page.results.length===20 && creates.length===consentCreates && session.habitatYouTubeTab===own && updates.at(-1).id===own,
    'A consecutive YouTube query also reuses its owned consent tab instead of abandoning it.');
  let cleared=await reset();
  check(cleared.closed===true && removals.at(-1)===own && !tabs.has(own) && tabs.has(1) && tabs.has(2) && !session.habitatYouTubeTab,
    'Clear search closes only its own YouTube result tab.');
  job();await dispatch();const nextTab=session.habitatYouTubeTab;const removeCount=removals.length;
  await vm.runInContext('youtubeSearchTransport.reset(socket,resetId)',context);
  check(removals.length===removeCount && tabs.has(nextTab),'Redelivered YouTube reset does not close a following search tab.');
  tabs.get(nextTab).pendingUrl='https://www.youtube.com/watch?v='+videoId(99);cleared=await reset();
  check(cleared.closed===false && tabs.has(nextTab),'If a user started watching in the owned tab, reset preserves the video.');
  for(const blocker of ['https://consent.youtube.com/m','https://consent.google.com/m']){
    job();await dispatch();const id=session.habitatYouTubeTab;tabs.get(id).url=blocker;
    cleared=await reset();check(cleared.closed===true && !tabs.has(id),'Reset closes its own consent page: '+blocker);
  }
  mode='empty';loaded=total=0;job('No matches');page=await dispatch();
  check(page.results.length===0 && !page.error,'YouTube reports a confirmed empty search explicitly.');total=47;
  for(const blocker of ['consent','blocked']){mode=blocker;job();page=await dispatch();check(page.error===blocker,'YouTube returns recoverable '+blocker+' without accepting it.');}
  mode='results';denied=true;job();page=await dispatch();check(page.error==='permission','Missing YouTube host permission explains recovery.');denied=false;
  mode='unknown';job();page=await dispatch();check(page.error==='timeout','An unknown YouTube layout does not claim zero results.');mode='results';
  let release;holdRead=new Promise(resolve=>{release=resolve;});job();const before=injections,running=dispatch();
  while(injections===before)await new Promise(resolve=>setImmediate(resolve));
  await reset();const messages=context.socket.sent.length;release();await running;
  check(context.socket.sent.length===messages,'Reset while reading YouTube discards late results.');
  let finishCreate;holdCreation=new Promise(resolve=>{finishCreate=resolve;});job();const createsBefore=creates.length,opening=dispatch();
  while(creates.length===createsBefore)await new Promise(resolve=>setImmediate(resolve));
  const closing=reset();finishCreate();holdCreation=null;await Promise.all([opening,closing]);
  check(!session.habitatYouTubeTab && !tabs.has(nextId-1),'Reset waits for in-flight YouTube tab creation and closes the late tab.');
  const count=injections;job('invalid',-1);await dispatch();job('x'.repeat(2049));await dispatch();
  check(injections===count,'Reject invalid YouTube offsets and oversized queries before touching Chrome.');
  attachFailure=true;job();page=await dispatch();
  check(page.error==='background-unavailable' && attached.size===0,'A busy YouTube debugger reports recovery without moving browser focus.');
  attachFailure=false;emulationFailure=true;job();page=await dispatch();
  check(page.error==='background-unavailable' && attached.size===0 && emulated.size===0,'A failed page focus command detaches its partial YouTube session.');
  emulationFailure=false;const debuggerApi=chrome.debugger;delete chrome.debugger;job();page=await dispatch();
  check(page.error==='background-permission','A missing background permission reports recovery before reading YouTube.');
  chrome.debugger=debuggerApi;
  check(attached.size===0 && emulated.size===0,'Every YouTube request releases its page focus override after completion, failure or reset.');
  for(const timer of timers)clearTimeout(timer);
  console.log('YouTube search extension checks passed: '+checks+'. Synthetic pages only.');
})().catch(error=>{for(const timer of timers)clearTimeout(timer);console.error(error);process.exitCode=1;});
