const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
let checks = 0;
function check(value, message) { assert.ok(value, message); checks++; }
function signal() { return {addListener(){}}; }
const sockets = [], creates = [], updates = [], timers = new Set(), session = {};
const removals = [];
const tabs = new Map([[1, {id:1, url:'https://www.google.com/search?q=personal', status:'complete', active:true}],
  [2, {id:2, url:'https://example.com/user-page', status:'complete', active:false}]]);
let nextId = 10, clock = 0, injections = 0, pageMode = 'results', holdInjection = null, holdCreation = null;
let denied = false, nextOffset = 10;
const location = {href:''};
const visible = {getBoundingClientRect:()=>({width:200,height:30})};
function result(title, href, snippet, options = {}) {
  const fragment = {...visible, textContent:snippet};
  const block = {querySelector:()=>snippet ? fragment : null, matches:()=>true, parentElement:null};
  const link = {href, parentElement:block};
  return {...visible, textContent:title,
    getBoundingClientRect:()=>({width:options.hidden?0:200,height:30}),
    closest:selector=>selector==='a[href]' ? link : options.ad ? {} : null};
}
let headings = [];
function standardHeadings() {
  return [result('First & café ☕', 'https://example.com/one', 'A useful description 雪'),
    result('Hidden result', 'https://example.com/hidden', 'Hidden', {hidden:true}),
    result('Ad', 'https://advertiser.example/', 'Sponsored', {ad:true}),
    result('Unsafe', 'javascript:alert(1)', 'Unsafe'),
    result('Private credentials', 'https://user:password@example.com/', 'Unsafe'),
    result('Google navigation', 'https://www.google.com/search?q=other', ''),
    result('Map module', 'https://www.google.com/maps/place/test', ''),
    result('Duplicate', 'https://example.com/one', 'Duplicate'),
    result('Google documentation', 'https://developers.google.com/search/docs', '<b>Plain text</b>'),
    result('Redirected result', 'https://www.google.com/url?q=https%3A%2F%2Fexample.org%2Ftwo', '')];
}
function navigate(value) {
  location.href = value;
  headings = pageMode === 'empty' || pageMode === 'unknown' ? [] : standardHeadings();
  if (new URL(value).searchParams.get('start') === '10') headings = [result('Second page', 'https://example.com/page-two', 'Another description')];
}
const root = {querySelectorAll:()=>headings};
const document = {querySelector:selector=> {
  if (selector === '#rso') return pageMode === 'unknown' ? null : root;
  if (selector === '#topstuff') return {textContent:pageMode === 'empty' ? 'Your search did not match any documents.' : ''};
  if (selector === 'a#pnnext') return nextOffset === null ? null : {href:'https://www.google.com/search?q=' +
    encodeURIComponent(new URL(location.href).searchParams.get('q')) + '&start=' + nextOffset};
  if (pageMode === 'consent' && selector.includes('consent.google')) return {};
  if (pageMode === 'blocked' && selector.includes('#captcha')) return {};
  return null;
}};
const chrome = {alarms:{onAlarm:signal(),create:async()=>{}}, storage:{onChanged:signal(),
  local:{get:async()=>({habitatDisabled:false})}, session:{get:async()=>session,set:async values=>Object.assign(session,values),
    remove:async key=>{delete session[key];}}},
  tabs:{get:async id=>{if(!tabs.has(id))throw Error('Tab closed');return {...tabs.get(id)};},
    remove:async id=>{removals.push(id);tabs.delete(id);},
    update:async(id,values)=>{updates.push({id,values});Object.assign(tabs.get(id),values);navigate(values.url);return {...tabs.get(id)};},
    create:async values=>{creates.push(values);if(holdCreation)await holdCreation;
      const tab={id:nextId++,url:values.url,status:'complete',active:values.active};tabs.set(tab.id,tab);navigate(values.url);return {...tab};}},
  scripting:{executeScript:async options=>{injections++;if(denied)throw Error('Cannot access contents: host permission denied');
    const value=options.func(...options.args);if(holdInjection){const wait=holdInjection;holdInjection=null;await wait;}return [{result:value}];}},
  windows:{update:()=>{throw Error('Search must never change browser windows');}},
  debugger:{attach:()=>{throw Error('Search must not use debugger automation');}}};
class Socket {static OPEN=1;constructor(url){this.url=url;this.readyState=1;this.sent=[];sockets.push(this);queueMicrotask(()=>this.onopen?.());}
  send(packet){this.sent.push(JSON.parse(packet));}close(){this.readyState=3;this.onclose?.();}}
const context = vm.createContext({chrome,WebSocket:Socket,URL,Promise,queueMicrotask,location,document,
  getComputedStyle:()=>({visibility:'visible'}),Date:{now:()=>clock+=300},
  setTimeout:(fn,ms)=>{const timer=setTimeout(fn,ms===300?0:ms);timers.add(timer);return timer;},
  fetch:async url=>{check(url==='http://127.0.0.1:8765/pair','Search fetch is limited to authenticated local pairing.');
    return {ok:true,json:async()=>({key:'synthetic-key'})};}});
let jobNumber=0;
function job(query='café & snow 雪',start=0) { context.job={id:(++jobNumber).toString(16).padStart(32,'0'),query,start};return context.job; }
async function dispatch() { await vm.runInContext('googleSearchTransport.dispatch(socket,job)',context);return context.socket.sent.at(-1); }

(async()=>{
  vm.runInContext(fs.readFileSync(path.resolve(__dirname,'../extension/search-transport.js'),'utf8'),context);
  vm.runInContext(fs.readFileSync(path.resolve(__dirname,'../extension/search.js'),'utf8'),context);
  for(let i=0;i<10;i++)await new Promise(resolve=>setImmediate(resolve));
  context.socket=sockets[0];
  check(context.socket.url==='ws://127.0.0.1:8765/search-bridge' && context.socket.sent[0].searchProtocol===1,
    'Google search connects without a ChatGPT tab.');
  const query='café & snow 雪';job(query);
  let page=await dispatch();
  check(page.results.length===3 && page.results[0].title==='First & café ☕' && page.results[0].snippet==='A useful description 雪' &&
    page.results[1].url==='https://developers.google.com/search/docs' && page.results[2].url==='https://example.org/two' && page.nextStart===10,
    'Return title, safe link and snippet in Google DOM order; unwrap redirects and omit ads, maps, navigation, hidden rows and duplicates.');
  check(page.results[1].snippet==='<b>Plain text</b>' && page.results[2].snippet==='', 'Preserve plain text and allow results without a description.');
  check(creates.length===1 && creates[0].active===false && tabs.get(1).url.includes('personal') && tabs.get(1).active && updates.length===0,
    'Create a dedicated inactive search tab without replacing a personal Google tab or moving browser focus.');
  const ownTab=session.habitatSearchTab;
  check(new URL(tabs.get(ownTab).url).searchParams.get('q')===query && tabs.get(ownTab).url.includes('udm=14'),
    'Encode Unicode and delimiters exactly and request ordinary web results.');
  const before=injections;await dispatch();
  check(injections===before && creates.length===1,'Redelivered job IDs resend cached results without searching twice.');
  nextOffset=null;job(query,10);page=await dispatch();
  check(page.results[0].title==='Second page' && page.nextStart===null && creates.length===1 && updates.at(-1).id===ownTab,
    'Next-page jobs reuse the dedicated tab and return their own results.');
  tabs.get(ownTab).pendingUrl='https://example.com/user-changed';job();await dispatch();
  check(creates.length===2 && tabs.get(ownTab).pendingUrl==='https://example.com/user-changed',
    'A search tab navigating elsewhere is preserved and replaced with a new dedicated tab.');
  pageMode='empty';job();page=await dispatch();
  check(page.results.length===0 && !page.error,'Recognize a confirmed no-results page.');
  for(const mode of ['consent','blocked']){pageMode=mode;job();page=await dispatch();check(page.error===mode,'Explain the Google '+mode+' blocker without accepting it automatically.');}
  pageMode='results';denied=true;job();page=await dispatch();
  check(page.error==='permission','Missing search host permission returns a recoverable error.');denied=false;
  pageMode='unknown';job();page=await dispatch();
  check(page.error==='timeout','An unknown Google layout fails explicitly instead of claiming there are no results.');pageMode='results';
  let release;holdInjection=new Promise(resolve=>{release=resolve;});job();
  const count=injections,sent=context.socket.sent.length,pending=dispatch();
  while(injections===count)await new Promise(resolve=>setImmediate(resolve));
  vm.runInContext('googleSearchTransport.cancel()',context);release();await pending;
  check(context.socket.sent.length===sent,'Cancel discards a late page result.');
  tabs.delete(session.habitatSearchTab);
  let releaseCreate;holdCreation=new Promise(resolve=>{releaseCreate=resolve;});job('Old query');
  const createsBefore=creates.length,old=dispatch();
  while(creates.length===createsBefore)await new Promise(resolve=>setImmediate(resolve));
  job('Replacement query');const latest=dispatch();releaseCreate();holdCreation=null;
  await Promise.all([old,latest]);
  check(creates.length===createsBefore+1 && context.socket.sent.at(-1).id===context.job.id,
    'Overlapping jobs serialize tab creation and only publish the latest query.');
  const prior=injections;job('Invalid',-1);await dispatch();job('x'.repeat(2049));await dispatch();
  check(injections===prior,'Reject invalid offsets and oversized queries before touching Chrome.');
  async function reset() {
    context.resetId=(++jobNumber).toString(16).padStart(32,'0');
    await vm.runInContext('googleSearchTransport.reset(socket,resetId)',context);
    return context.socket.sent.at(-1);
  }
  const owned=session.habitatSearchTab;
  let cleared=await reset();
  check(cleared.kind==='search-reset' && cleared.closed===true && removals.at(-1)===owned &&
    !tabs.has(owned) && !('habitatSearchTab' in session) && tabs.has(1) && tabs.has(2),
    'Reset closes exactly the dedicated Google tab and forgets its saved ID while preserving personal tabs.');
  job();await dispatch();
  const freshTab=session.habitatSearchTab,removedCount=removals.length;
  await vm.runInContext('googleSearchTransport.reset(socket,resetId)',context);
  check(removals.length===removedCount && tabs.has(freshTab), 'Redelivery of an acknowledged reset never closes a following search tab.');
  tabs.get(freshTab).pendingUrl='https://example.com/user-navigation';
  cleared=await reset();
  check(cleared.closed===false && tabs.has(freshTab) && !('habitatSearchTab' in session),
    'If the user navigated the saved tab away from Google, reset preserves the page and relinquishes ownership.');
  for(const blocker of ['https://consent.google.com/m','https://www.google.com/sorry/index']) {
    job();await dispatch();const blockedTab=session.habitatSearchTab;tabs.get(blockedTab).url=blocker;
    cleared=await reset();
    check(cleared.closed===true && !tabs.has(blockedTab),'Reset also closes its own Google blocker tab: '+blocker);
  }
  job();await dispatch();tabs.delete(session.habitatSearchTab);cleared=await reset();
  check(cleared.closed===false && !('habitatSearchTab' in session), 'Reset succeeds if the user already closed the search tab.');
  let finishCreate;holdCreation=new Promise(resolve=>{finishCreate=resolve;});job('Reset during creation');
  const createsCount=creates.length,running=dispatch();
  while(creates.length===createsCount)await new Promise(resolve=>setImmediate(resolve));
  const closing=reset();finishCreate();holdCreation=null;
  await Promise.all([running,closing]);
  check(context.socket.sent.at(-1).kind==='search-reset' && !('habitatSearchTab' in session) &&
    !tabs.has(nextId-1), 'Reset waits for unfinished tab creation and closes the late-created tab without publishing stale results.');
  let finishRead;holdInjection=new Promise(resolve=>{finishRead=resolve;});job('Reset during read');
  const reads=injections,reading=dispatch();
  while(injections===reads)await new Promise(resolve=>setImmediate(resolve));
  await reset();const messages=context.socket.sent.length;finishRead();await reading;
  check(context.socket.sent.length===messages && context.socket.sent.at(-1).kind==='search-reset',
    'Reset during DOM reading discards the late result and its cached response.');
  for(const timer of timers)clearTimeout(timer);
  console.log('Google search extension checks passed: '+checks+'. Synthetic DOM only.');
})().catch(error=>{for(const timer of timers)clearTimeout(timer);console.error(error);process.exitCode=1;});
