// Runs the packaged Facebook worker against a real, isolated desktop bridge.
// Browser tab APIs are synthetic; HTTP pairing and WebSocket frames are real.
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const address=new URL(process.argv[2]);
if(address.hostname!=='127.0.0.1'||address.port==='8765')throw Error('An isolated loopback port is required.');
const signal=()=>({addListener(){}});
class LocalSocket extends WebSocket {
  constructor(url){
    if(url!=='ws://127.0.0.1:8765/facebook-bridge')throw Error('Unexpected socket.');
    super(address.href.replace('http:','ws:').replace(/\/$/,'')+'/facebook-bridge');
  }
}
const source=fs.existsSync(path.join(__dirname,'facebook.js'))?path.join(__dirname,'facebook.js'):path.join(__dirname,'../extension/facebook.js');
const chrome={runtime:{getManifest:()=>({version:'synthetic'})},alarms:{onAlarm:signal(),create:async()=>{}},
  storage:{onChanged:signal(),local:{get:async()=>({habitatConsentVersion:1})},session:{get:async()=>({}),set:async()=>{}}},
  permissions:{contains:async()=>true,onRemoved:signal(),onAdded:signal()},
  tabs:{query:async()=>[{id:1,url:'https://www.facebook.com/',status:'complete'}],get:async()=>({id:1,url:'https://www.facebook.com/',status:'complete'})},
  scripting:{executeScript:async()=>[{result:{contacts:[{name:'Real wire · synthetic contact ☕',photoUrl:'https://scontent.fbcdn.net/synthetic.jpg',link:null,isOnline:true}]}}]}};
const context=vm.createContext({chrome,WebSocket:LocalSocket,URL,AbortSignal,TextEncoder,Date,Promise,setTimeout,
  habitatConnectionEnabled:async()=>true,
  fetch:(url,options)=>{
    if(url!=='http://127.0.0.1:8765/pair')throw Error('Unexpected request.');
    return fetch(new URL('/pair',address),options);
  }});
vm.runInContext(fs.readFileSync(source,'utf8'),context,{filename:'facebook.js'});
setTimeout(()=>{console.error('Facebook worker integration timed out.');process.exit(1);},20000).unref();
