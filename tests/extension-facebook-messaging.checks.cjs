const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../extension/facebook-messaging.js'),'utf8');
let checks=0;
const check=(value,message)=>{assert.ok(value,message);checks++;};
const thread='https://www.facebook.com/messages/t/synthetic.a';
const profile='https://www.facebook.com/synthetic.a';
function environment(shared={attempts:{}}){
  const state={draft:'',clicks:0,inserts:0,editors:1,buttons:1,creates:0,permission:true,enabled:true,
    profile:thread,wrongNavigation:false,retainText:false,clock:0,disconnectOnPrepare:false,closed:[]};
  const location={};
  const navigate=value=>{const url=new URL(value);Object.assign(location,{href:url.href,pathname:url.pathname});};
  navigate(thread);
  const rect={getBoundingClientRect:()=>({width:40,height:40}),getAttribute:()=>null};
  const editor={...rect,getAttribute:name=>name==='aria-label'?(state.commentEditor?'Write a comment…':'Message'):null,
    focus:()=>{},get innerText(){return state.draft;}};
  const anchor={...rect,get textContent(){return state.ariaProfile?'':'Synthetic A';},
    getAttribute:name=>name==='href'?state.profile:name==='aria-label'&&state.ariaProfile?'Synthetic A':null};
  const heading={...rect,textContent:'Synthetic A',querySelectorAll:()=>state.secondaryProfile?[anchor,{...anchor,getAttribute:()=>state.secondaryProfile}]:[anchor],closest:()=>null};
  const messageLink={...rect,textContent:'Message',getAttribute:name=>name==='href'?thread:null};
  const openMessenger={...messageLink,textContent:'Open in Messenger'};
  const openButton={...rect,textContent:'Message',click:()=>{state.opens=(state.opens||0)+1;state.popupVisible=true;}};
  const profileArea={...rect,querySelectorAll:selector=>selector==='a[href]'?(state.popupMode?[]:[messageLink]):
    selector==='button,[role="button"]'&&state.popupMode?[openButton]:[]};
  const popupHeading={...heading,parentElement:null};
  const settings={...rect,get textContent(){return state.genericPopup?'Synthetic A':'Chat settings';},querySelectorAll:()=>[popupHeading],
    click:()=>{state.menuClicks=(state.menuClicks||0)+1;state.menuVisible=true;}};
  const menuItem={...rect,textContent:'Open in Messenger',click:()=>{
    state.threadOpens=(state.threadOpens||0)+1;state.popupVisible=false;state.menuVisible=false;
    const url='https://www.facebook.com/messages/t/123456789';
    for(const tab of tabs.values())tab.url=url;navigate(url);
  }};
  const popup={...rect,querySelectorAll:selector=>selector==='a[href]'?(state.groupPopup?[anchor]:[openMessenger]):
    selector==='button,[role="button"]'?(state.groupPopup?[settings]:[]):selector.includes('contenteditable')?[editor]:[popupHeading]};
  popupHeading.parentElement=popup;
  const send={...rect,getAttribute:name=>name==='aria-label'?(state.sendLabel||'Send'):null,
    click:()=>{state.clicks++;if(!state.retainText)state.draft='';}};
  const main={...rect,querySelectorAll:selector=>selector.includes('contenteditable')?Array(state.editors).fill(0).map(()=>Object.create(editor)):
    selector==='button,[role="button"]'?(state.profileButtonOnly?[openButton]:Array(state.buttons).fill(0).map(()=>send)):
    selector==='h1,[role="heading"][aria-level="1"]'&&state.noH1?[]:selector.includes('h1')?[heading]:[]};
  heading.parentElement=profileArea;profileArea.parentElement=main;
  const document={querySelector:selector=>state.numericMetadata&&selector==='meta[property="al:android:url"]'?{getAttribute:()=> 'fb://profile/123456789'}:null,
    querySelectorAll:selector=>selector==='[role="main"]'?[main]:
      selector==='[role="menu"] [role="menuitem"]'?(state.menuVisible?[menuItem]:[]):
      selector==='h1,h2,h3,h4,h5,h6,[role="heading"]'?(state.popupVisible?[heading,popupHeading]:[heading]):
      state.genericPopup?[]:
      state.groupPopup&&!selector.includes('[role="group"]')?[]:state.popupVisible?[popup]:[],
    execCommand:(command,_unused,text)=>{assert.equal(command,'insertText');state.inserts++;
      if(state.asyncInsertion)state.nextDraft=text;else if(!state.rejectInsertion)state.draft=text;
      return !state.asyncInsertion&&!state.rejectInsertion;}};
  const socket={readyState:1,sent:[],send:value=>socket.sent.push(JSON.parse(value))};
  const tabs=new Map();
  let context;
  const chrome={permissions:{contains:async()=>state.permission},storage:{session:{
    get:async()=>({habitatMessengerAttempts:structuredClone(shared.attempts)}),
    set:async value=>{shared.attempts=structuredClone(value.habitatMessengerAttempts);}}},
    tabs:{create:async args=>{check(args.active===false,'A submission opens its own background tab without taking focus.');state.creates++;
      const tab={id:100+state.creates,url:state.wrongNavigation?'https://www.facebook.com/messages/t/someone.else':
        state.numericRedirect&&args.url==='https://www.facebook.com/123456789'?profile:args.url,status:'complete'};
      tabs.set(tab.id,tab);navigate(tab.url);return tab;},
      get:async id=>tabs.get(id),update:async(id,changes)=>{check(tabs.has(id),'Only a tab created by this submission can be navigated.');Object.assign(tabs.get(id),changes);if(changes.url)navigate(changes.url);},
      remove:async id=>{check(tabs.has(id),'Only a tab created by this operation is closed.');state.closed.push(id);
        if(state.failCleanup)throw Error('Synthetic tab cleanup failure');
        tabs.delete(id);if(tabs.size)navigate([...tabs.values()][0].url);}},
    scripting:{executeScript:async args=>{if(state.nextDraft&&args.args[0]==='can-send'){state.draft=state.nextDraft;state.nextDraft=null;}
      context.callArgs=args.args;const result=vm.runInContext('facebookMessagePage(...callArgs)',context);
      if(args.args[0]==='prepare'&&state.disconnectOnPrepare)socket.readyState=3;return[{result}];}}};
  context=vm.createContext({URL,document,location,chrome,WebSocket:{OPEN:1},getComputedStyle:()=>({visibility:'visible',display:'block'}),
    habitatConnectionEnabled:async()=>state.enabled,Date:{now:()=>state.clock+=500},setTimeout:resolve=>setImmediate(resolve),console});
  vm.runInContext(source,context);
  const page=(action,target=thread,expectedThread=thread,text='Synthetic text')=>{
    context.callArgs=[action,target,'Synthetic A',expectedThread,text];return vm.runInContext('facebookMessagePage(...callArgs)',context);};
  const handle=async request=>{context.socket=socket;context.request=request;await vm.runInContext('facebookMessaging.handle(socket,request)',context);return socket.sent.at(-1);};
  return{state,page,handle,socket,navigate,shared};
}
(async()=>{
  let e=environment();
  const inspected=e.page('inspect');
  check(inspected.inspected&&inspected.diagnostic.mains===1&&inspected.diagnostic.identityMatches===1&&e.state.inserts===0&&e.state.clicks===0,
    'Read-only inspection returns structural counts without inserting or sending.');
  check(!JSON.stringify(inspected.diagnostic).includes('Synthetic A')&&!JSON.stringify(inspected.diagnostic).includes('facebook.com'),
    'DOM diagnostics contain no names, URLs or message text.');
  check(e.page('prepare').prepared&&e.state.draft==='Synthetic text'&&e.state.clicks===0,'Preparing exact text does not send it.');
  check(e.page('send').attempted&&e.state.clicks===1&&e.page('confirm').submitted,'One explicit send action is confirmed only after the editor clears.');
  for (const sendLabel of ['Pulsa Enter para enviar','Presionar Enter para enviar']) {
    e=environment();e.state.sendLabel=sendLabel;e.page('prepare');
    check(e.page('can-send').ready&&e.state.clicks===0&&e.page('send').attempted&&e.state.clicks===1,
      'Spanish Messenger send labels can be checked without clicking and submitted exactly once.');
  }
  e=environment();e.state.draft='An existing Messenger draft';
  check(e.page('prepare').error==='draft'&&e.state.inserts===0&&e.state.clicks===0,'Never overwrite an existing Messenger draft.');
  e=environment();e.state.editors=2;
  check(e.page('prepare').error==='composer'&&e.state.inserts===0,'Ambiguous editors are rejected before insertion.');
  e=environment();e.state.commentEditor=true;
  check(e.page('ready').error==='composer'&&e.page('prepare').error==='composer'&&e.state.inserts===0,
    'A profile comment editor cannot be mistaken for Messenger during asynchronous URL changes.');
  e=environment();e.state.buttons=2;e.state.draft='Synthetic text';
  check(e.page('send').error==='composer'&&e.state.clicks===0,'Ambiguous send controls are rejected.');
  e=environment();e.state.draft='Changed by user';
  check(e.page('send').error==='composer'&&e.state.clicks===0,'Changed text is never submitted.');
  e=environment();e.navigate('https://www.facebook.com/messages/t/someone.else');
  check(e.page('resolve').error==='recipient'&&e.page('prepare').error==='recipient'&&e.state.clicks===0,'Redirects to another conversation cannot change the recipient.');
  e=environment();e.state.profile='https://www.facebook.com/someone.else';
  check(e.page('prepare',profile).error==='recipient'&&e.state.inserts===0,'A profile destination needs an exact matching profile link in the conversation heading.');
  e=environment();e.state.profile=profile;
  check(e.page('prepare',profile).prepared,'A verified profile heading can prepare its resolved conversation.');
  e=environment();e.state.profile='https://www.facebook.com/messages/t/someone.else';
  check(e.page('prepare').error==='recipient'&&e.state.clicks===0,'A stale conversation heading cannot be trusted merely because the address bar has the requested URL.');
  e=environment();e.state.secondaryProfile='https://www.facebook.com/someone.else';
  check(e.page('prepare').error==='recipient'&&e.state.inserts===0,'A heading that identifies multiple people is never treated as a one-to-one recipient.');
  e=environment();e.navigate('https://evil.test/messages/t/synthetic.a');
  check(e.page('prepare').error==='recipient'&&e.state.inserts===0,'No insertion or send is possible on an external origin.');
  const request={id:'1'.repeat(32),targetLink:thread,targetName:'Synthetic A',text:'Synthetic worker body ☕\nsecond line'};
  e=environment();let result=await e.handle(request);
  check(result.status==='submitted'&&e.state.clicks===1,'The actual worker orchestrates one fully synthetic submission.');
  check(e.state.closed.length===1,'A confirmed send closes its temporary messaging tab.');
  const cleanupFailure=environment();cleanupFailure.state.failCleanup=true;
  check((await cleanupFailure.handle(request)).status==='submitted'&&cleanupFailure.state.clicks===1,
    'A tab cleanup error cannot turn a confirmed send into a retryable failure.');
  const lexical=environment();lexical.state.asyncInsertion=true;
  check((await lexical.handle(request)).status==='submitted'&&lexical.state.inserts===1&&lexical.state.clicks===1,
    'An asynchronous Lexical insertion is verified after the command returns false, without inserting twice.');
  const rejectedInsertion=environment();rejectedInsertion.state.rejectInsertion=true;
  check((await rejectedInsertion.handle(request)).status==='not-sent'&&rejectedInsertion.state.inserts===1&&rejectedInsertion.state.clicks===0,
    'A rejected insertion never passes the exact-text check or reaches the final click.');
  const stages=e.socket.sent.filter(packet=>packet.kind==='facebook-message-progress').map(packet=>packet.stage);
  check(['received','permission','opening-profile','open','ready','prepare','send','confirm'].every(stage=>stages.includes(stage)),
    'The worker reports each stage needed to diagnose the entire submission cycle.');
  check(!JSON.stringify(e.socket.sent.filter(packet=>packet.kind==='facebook-message-progress')).includes('Synthetic worker body'),
    'Progress packets never contain the submitted message.');
  const inspection=environment();const inspectionResult=await inspection.handle({...request,inspectOnly:true});
  check(inspectionResult.status==='not-sent'&&inspectionResult.error==='inspected'&&inspection.state.inserts===0&&inspection.state.clicks===0,
    'An explicit inspection verifies the conversation and editor without inserting or sending.');
  check(inspection.state.closed.length===1,'Successful inspection closes its temporary tab too.');
  check(!inspection.socket.sent.some(packet=>['prepare','send'].includes(packet.stage)),
    'Inspection can never reach the mutation stages.');
  const resumedInspection=environment(inspection.shared);
  check((await resumedInspection.handle({...request,inspectOnly:true})).error==='inspected'&&resumedInspection.state.creates===0,
    'Completed inspections are acknowledged after restart without repeating navigation.');
  await e.handle(request);
  check(e.state.clicks===1&&e.state.creates===1,'Repeated bridge ticks return the cached result without sending again.');
  check(!JSON.stringify(e.shared).includes('worker body')&&!JSON.stringify(e.shared).includes('Synthetic A'), 'Session deduplication stores operation IDs/outcomes only, without message text or recipient names.');
  const restarted=environment(e.shared);result=await restarted.handle(request);
  check(result.status==='submitted'&&restarted.state.clicks===0&&restarted.state.creates===0,'A worker restart acknowledges a completed operation without replaying it.');
  e=environment();e.state.profile=profile;result=await e.handle({...request,targetLink:profile});
  check(result.status==='submitted'&&e.state.clicks===1,'A profile target resolves an explicit Message link and verifies the linked profile again in the thread heading.');
  e=environment();e.state.profile=profile;e.state.popupMode=true;result=await e.handle({...request,targetLink:profile});
  check(result.status==='submitted'&&e.state.opens===1&&e.state.clicks===1,'A profile Message action opens once, then only its verified popup can resolve the conversation.');
  e=environment();e.state.profile=profile;e.state.popupMode=true;e.state.noH1=true;
  result=await e.handle({...request,targetLink:profile});
  check(result.status==='submitted'&&e.state.opens===1&&e.state.clicks===1,
    'A profile using a level-two heading still resolves its Message action without relaxing thread identity checks.');
  e=environment();e.state.profile='https://www.facebook.com/123456789';e.state.popupMode=true;e.state.groupPopup=true;e.state.numericMetadata=true;
  result=await e.handle({...request,targetLink:profile});
  check(result.status==='submitted'&&e.state.opens===1&&e.state.menuClicks===1&&e.state.threadOpens===1&&e.state.clicks===1,
    'A group popup uses the profile-owned numeric alias and opens Messenger through its menu exactly once.');
  e=environment();e.state.profile='https://www.facebook.com/123456789';e.state.popupMode=true;e.state.groupPopup=true;e.state.genericPopup=true;e.state.numericMetadata=true;
  result=await e.handle({...request,targetLink:profile});
  check(result.status==='submitted'&&e.state.menuClicks===1&&e.state.clicks===1,
    'A generic chat container and a settings button named by its child heading preserve recipient verification.');
  e=environment();e.state.profile='https://www.facebook.com/123456789';e.state.popupMode=true;e.state.groupPopup=true;e.state.genericPopup=true;e.state.numericRedirect=true;e.state.ariaProfile=true;
  result=await e.handle({...request,targetLink:profile});
  check(result.status==='submitted'&&e.state.creates===2&&e.state.menuClicks===1&&e.state.clicks===1,
    'Without alias metadata, normal profile navigation must verify the numeric link against the exact requested profile before opening its thread.');
  e=environment();e.state.profile='https://www.facebook.com/987654321';e.state.popupMode=true;e.state.groupPopup=true;e.state.numericMetadata=true;
  result=await e.handle({...request,targetLink:profile});
  check(result.status==='not-sent'&&result.error==='recipient'&&e.state.inserts===0&&!e.state.menuClicks,
    'An equally named popup with a different numeric profile cannot be selected.');
  e=environment({attempts:{[request.id]:'claimed'}});result=await e.handle(request);
  check(result.status==='uncertain'&&e.state.clicks===0&&e.state.creates===0,'A claimed operation after restart is never retried.');
  e=environment();e.state.retainText=true;result=await e.handle(request);
  check(result.status==='uncertain'&&e.state.clicks===1,'A missing submission confirmation does not trigger a second click.');
  check(e.state.closed.length===0,'An uncertain submission keeps its tab available for review.');
  e=environment();e.state.permission=false;result=await e.handle(request);
  check(result.status==='not-sent'&&result.error==='permission'&&e.state.creates===0,'Optional permission is required before opening or editing a conversation.');
  e=environment();e.state.wrongNavigation=true;result=await e.handle(request);
  check(result.status==='not-sent'&&result.error==='recipient'&&e.state.inserts===0&&e.state.clicks===0,'A changed tab recipient fails closed before any message is inserted.');
  check(e.state.closed.length===1,'A rejected recipient closes the unused operation tab.');
  e=environment();e.state.disconnectOnPrepare=true;await e.handle(request);
  check(e.state.clicks===0&&e.shared.attempts[request.id]==='claimed','Disconnecting between preparation and send prevents the final click and keeps the operation claimed.');
  console.log(`Facebook messaging checks passed: ${checks}. Synthetic DOM, tabs and sockets only; no real messages.`);
})().catch(error=>{console.error(error);process.exitCode=1;});
