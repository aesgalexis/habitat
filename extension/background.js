// Cross-origin bridge. Only one active ChatGPT tab is paired at a time.
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  (async () => {
    if (!isChatGPT(sender.tab?.url)) return {error:"Not a ChatGPT tab"};
    let s = await chrome.storage.local.get(["habitatKey","habitatTab","habitatUrl"]);
    const senderUrl=normalizeUrl(sender.tab.url);
    const knownBinding=sender.tab.id===s.habitatTab && senderUrl===s.habitatUrl;
    if (!knownBinding) {
      if (!sender.tab.active) return {error:"Open the ChatGPT tab to pair it"};
      s = await pairTab(sender.tab);
      if (!s) return {error:"HABITAT desktop app is unavailable"};
    } else if (!s.habitatKey) {
      // A known bound tab may recover the local key even while it is in the
      // background. This is what makes restarting HABITAT self-healing.
      s = await pairTab(sender.tab);
      if (!s) return {error:"HABITAT desktop app is unavailable"};
    }
    if (senderUrl !== s.habitatUrl) {
      return {error:"The linked tab changed conversation"};
    }
    if (request.kind !== "next" && request.kind !== "event") return {error:"Invalid operation"};
    const run = async credentials => {
      const url = "http://127.0.0.1:8765/" + (request.kind === "next" ? "next" : "event");
      const init = {method: request.kind === "next" ? "GET" : "POST",
        headers:{"X-Habitat-Key":credentials.habitatKey,"Content-Type":"application/json"}};
      if (request.kind === "event") init.body=JSON.stringify(request.event);
      return fetch(url,init);
    };
    try {
      let response=await run(s);
      // HABITAT generates a fresh key on every launch. The already-bound tab
      // is allowed to renew it without requiring the user to foreground Chrome.
      if(response.status===403 && (knownBinding || sender.tab.active)) {
        const refreshed=await pairTab(sender.tab);
        if(refreshed) response=await run(refreshed);
      }
      return response.ok ? await response.json() : {error:"HTTP "+response.status};
    } catch(e) {return {error:String(e)};}
  })().then(sendResponse);
  return true;
});

chrome.runtime.onInstalled.addListener(() => { void pairMostRecentChat(); });
chrome.runtime.onStartup.addListener(() => { void pairMostRecentChat(); });
void pairMostRecentChat();

async function pairMostRecentChat() {
  try {
    const tabs=await chrome.tabs.query({url:"https://chatgpt.com/*"});
    const candidates=tabs.filter(tab=>tab.id && tab.url);
    if(!candidates.length)return;
    candidates.sort(compareChatTabs);
    const tab=candidates[0];
    const paired=await pairTab(tab);
    if(!paired)return;
    await chrome.scripting.executeScript({target:{tabId:tab.id},files:["content.js"]});
  } catch (_) {}
}

function compareChatTabs(a,b) {
  const conversation=value=>{
    try { return /\/c\//.test(new URL(value).pathname) ? 1 : 0; }
    catch (_) { return 0; }
  };
  return conversation(b.url)-conversation(a.url) ||
    Number(b.active)-Number(a.active) ||
    (b.lastAccessed||0)-(a.lastAccessed||0);
}

async function pairTab(tab) {
  try {
    const response=await fetch("http://127.0.0.1:8765/pair",{
      headers:{"X-Habitat-Pair":"1"},cache:"no-store"
    });
    if(!response.ok)return null;
    const data=await response.json();
    if(!data?.key)return null;
    const paired={habitatKey:data.key,habitatTab:tab.id,habitatUrl:normalizeUrl(tab.url)};
    await chrome.storage.local.set(paired);
    return paired;
  } catch (_) {return null;}
}

function isChatGPT(value) {
  try { return new URL(value).origin === "https://chatgpt.com"; }
  catch (_) { return false; }
}

function normalizeUrl(value) {
  try {
    const url = new URL(value);
    return url.origin + url.pathname.replace(/\/$/, "");
  } catch (_) {
    return "";
  }
}
