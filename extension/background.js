// Use Chrome’s active ChatGPT chat. Never create or rename a conversation.
importScripts("connection.js", "transport.js");
importScripts("translation.js");
importScripts("search-transport.js", "search.js", "youtube.js");
importScripts("facebook-messaging.js", "facebook.js");
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  (async () => {
    if (!await habitatConnectionEnabled()) return {error:"Open the HABITAT extension and enable the connection first"};
    if (request.kind === "facebook-connect" && sender.id === chrome.runtime.id &&
        sender.url?.startsWith(chrome.runtime.getURL(""))) {
      return await facebookTransport.connect(request.restart === true);
    }
    if (request.kind === "connect" && sender.id === chrome.runtime.id &&
        sender.url?.startsWith(chrome.runtime.getURL(""))) {
      return await startTransport();
    }
    if (!isChatGPT(sender.tab?.url)) return {error:"Not a ChatGPT tab"};
    if (sender.frameId !== 0) return {error:"Not the top-level ChatGPT page"};
    if (request.kind === "hello") {
      void startTransport();
      return {ok:true};
    }
    let s = await chrome.storage.local.get(["habitatKey","habitatTab","habitatUrl"]);
    const senderUrl=normalizeUrl(sender.tab.url);
    const sameTab=sender.tab.id===s.habitatTab;
    const newChatTransition=sameTab && !isConversationUrl(s.habitatUrl) && isConversationUrl(senderUrl);
    if(newChatTransition) {
      s.habitatUrl=senderUrl;
      await chrome.storage.local.set({habitatUrl:senderUrl});
    }
    const knownBinding=sameTab && (senderUrl===s.habitatUrl || newChatTransition);
    if (!knownBinding) {
      return {error:"This is not the linked ChatGPT conversation"};
    } else if (!s.habitatKey) {
      // A known bound tab may recover the local key even while it is in the
      // background. This is what makes restarting HABITAT self-healing.
      s = await pairTab(sender.tab);
      if (!s) return {error:"HABITAT desktop app is unavailable"};
    }
    if (senderUrl !== s.habitatUrl) {
      return {error:"The linked tab changed conversation"};
    }
    if (request.kind === "delay") {
      // Page timers can be throttled to one wake-up per minute. These short
      // editing/response waits run in the extension worker instead.
      const ms = Number(request.ms);
      if (!Number.isFinite(ms) || ms < 0 || ms > 5000) return {error:"Invalid delay"};
      await new Promise(resolve => setTimeout(resolve, ms));
      return {ok:true};
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

chrome.runtime.onInstalled.addListener(() => {
  void (async () => {
    const saved = await chrome.storage.local.get("habitatConsentVersion");
    if (saved.habitatConsentVersion !== HABITAT_CONSENT_VERSION)
      await chrome.tabs.create({url:chrome.runtime.getURL("welcome.html")});
    else await pairMostRecentChat();
  })();
});
chrome.runtime.onStartup.addListener(() => { void pairMostRecentChat(); });
void closeReloadTabs();
void startTransport();

async function closeReloadTabs() {
  try {
    const tabs=await chrome.tabs.query({url:chrome.runtime.getURL("reload.html")});
    const ids=tabs.map(tab=>tab.id).filter(id=>Number.isInteger(id));
    if(ids.length)await chrome.tabs.remove(ids);
  } catch (_) {}
}

async function pairMostRecentChat() {
  return startTransport();
}

function compareChatTabs(a,b) {
  const conversation=value=>{
    try { return /\/c\//.test(new URL(value).pathname) ? 1 : 0; }
    catch (_) { return 0; }
  };
  return Number(b.active)-Number(a.active) ||
    (b.lastAccessed||0)-(a.lastAccessed||0) ||
    conversation(b.url)-conversation(a.url);
}

function isConversationUrl(value) {
  try { return /(^|\/)c\/[^/]+/.test(new URL(value).pathname); }
  catch (_) { return false; }
}

async function pairTab(tab) {
  try {
    if (!await habitatConnectionEnabled()) return null;
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
