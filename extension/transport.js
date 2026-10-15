// Persistent local transport lives in the MV3 worker, NOT in a hidden page.
// WebSocket traffic keeps the worker alive while HABITAT is running (Chrome
// 116+); a 30-second alarm restores it after browser/worker restarts.
const WATCHDOG = "habitat-transport";
let habitatSocket = null;
let transportStarting = null;
let retryTimer = null;
let retryCount = 0;
let tickRunning = false;
let protectionChange = Promise.resolve();

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === WATCHDOG) void startTransport();
});
chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== "local") return;
  if (changes.habitatTab || changes.habitatUrl || changes.habitatDisabled) {
    closeTransport();
    void startTransport();
  }
});
chrome.tabs.onRemoved.addListener(tabId => {
  if (habitatSocket?.habitatTab === tabId) { closeTransport(); scheduleReconnect(); }
});
chrome.tabs.onUpdated.addListener((tabId, change) => {
  if (change.status === "complete" || change.frozen === false || change.discarded === false) void startTransport();
});

async function startTransport() {
  if (transportStarting) return transportStarting;
  transportStarting = connectTransport().catch(() => {
    scheduleReconnect();
    return {error:"HABITAT desktop app is unavailable"};
  }).finally(() => { transportStarting = null; });
  return transportStarting;
}

async function connectTransport() {
  if (habitatSocket && habitatSocket.readyState <= WebSocket.OPEN) return {ok:true};
  await chrome.alarms.create(WATCHDOG, {periodInMinutes:0.5});
  const saved = await chrome.storage.local.get(["habitatTab","habitatUrl","habitatDisabled"]);
  if (saved.habitatDisabled) return {error:"The extension is disconnected"};
  const tab = await chooseBoundTab(saved);
  if (!tab) { scheduleReconnect(); return {error:"Select a ChatGPT tab in Chrome"}; }
  const credentials = await pairTab(tab);
  if (!credentials) { scheduleReconnect(); return {error:"HABITAT desktop app is unavailable"}; }

  await protectBoundTab(tab);
  const currentBinding = await chrome.storage.local.get(["habitatDisabled","habitatTab","habitatUrl"]);
  if (currentBinding.habitatDisabled || currentBinding.habitatTab !== tab.id || currentBinding.habitatUrl !== normalizeUrl(tab.url)) {
    await restoreProtectedTab();
    scheduleReconnect();
    return {error:currentBinding.habitatDisabled ? "The extension is disconnected" : "The linked tab changed conversation"};
  }
  const socket = new WebSocket("ws://127.0.0.1:8765/bridge");
  socket.habitatTab = tab.id;
  habitatSocket = socket;
  socket.onopen = () => {
    retryCount = 0;
    socket.send(JSON.stringify({kind:"auth",key:credentials.habitatKey}));
  };
  socket.onmessage = event => {
    if (habitatSocket !== socket) return;
    try {
      const packet = JSON.parse(event.data);
      if (packet.kind === "tick") void transportTick(socket, packet.message);
    } catch (_) {}
  };
  socket.onclose = () => {
    if (habitatSocket !== socket) return;
    habitatSocket = null;
    void restoreProtectedTab();
    scheduleReconnect();
  };
  socket.onerror = () => { if (socket.readyState === WebSocket.OPEN) socket.close(); };
  return {ok:true};
}

async function chooseBoundTab(saved) {
  const tabs = await chrome.tabs.query({active:true,lastFocusedWindow:true});
  const active = tabs.find(tab => Number.isInteger(tab.id) && isChatGPT(tab.url));
  if (active) return active;
  // Selecting another website must not disable the already-linked chat.
  // Recover the same binding after a desktop/worker restart without activating
  // its tab or silently choosing an unrelated background conversation.
  if (!Number.isInteger(saved?.habitatTab)) return null;
  try {
    const bound = await chrome.tabs.get(saved.habitatTab);
    if (!bound || !isChatGPT(bound.url)) return null;
    const url = normalizeUrl(bound.url);
    return url === saved.habitatUrl || (!isConversationUrl(saved.habitatUrl) && isConversationUrl(url)) ? bound : null;
  } catch (_) { return null; }
}

async function transportTick(socket, message) {
  if (tickRunning || socket !== habitatSocket || socket.readyState !== WebSocket.OPEN) return;
  tickRunning = true;
  try {
    const saved = await chrome.storage.local.get(["habitatTab","habitatUrl","habitatDisabled"]);
    if (saved.habitatDisabled || saved.habitatTab !== socket.habitatTab) { closeTransport(); return; }
    const tab = await chrome.tabs.get(saved.habitatTab);
    const url = normalizeUrl(tab.url);
    const transition = !isConversationUrl(saved.habitatUrl) && isConversationUrl(url);
    if (!isChatGPT(tab.url)) {
      heartbeat(socket, false, "The linked tab changed conversation. Reconnect the intended chat.");
      return;
    }
    if (transition || url !== saved.habitatUrl) {
      await chrome.storage.local.set({habitatUrl:url});
      return;
    }
    if (tab.discarded || tab.frozen) {
      heartbeat(socket, false, "Chrome suspended the linked tab. Open it once to resume.");
      return;
    }
    const page = await contactPage(tab.id, {kind:"habitat:probe"});
    if (socket !== habitatSocket) return;
    // Follow a deliberately selected ChatGPT chat between requests. A queued
    // message or active response stays in its original bound conversation.
    if (!page?.busy && !message) {
      const active = await chooseBoundTab(saved);
      if (active && (active.id !== tab.id || normalizeUrl(active.url) !== url)) {
        await pairTab(active);
        return;
      }
    }
    const ready = page?.ready === true && normalizeUrl(page.url) === url;
    heartbeat(socket, ready, ready ? "Linked ChatGPT chat connected" : (page?.issue || "Waiting for the linked ChatGPT page…"), page?.model);
    if (ready && message && !page.busy) {
      // Re-check the stored binding immediately before dispatch. Activation
      // of another chat or worker retries must not route a message elsewhere.
      const current = await chrome.storage.local.get(["habitatTab","habitatUrl"]);
      if (current.habitatTab !== tab.id || current.habitatUrl !== url || socket !== habitatSocket) return;
      await contactPage(tab.id, {kind:"habitat:deliver",message,expectedUrl:url});
    }
  } catch (error) {
    heartbeat(socket, false, "ChatGPT bridge: " + String(error?.message || error).slice(0,140));
  } finally { tickRunning = false; }
}

async function contactPage(tabId, request) {
  try {
    const answer = await timedPageMessage(tabId, request);
    if (request.kind !== "habitat:probe" || answer?.protocol === 6 || answer?.busy) return answer;
    throw Error("ChatGPT page bridge needs updating");
  }
  catch (error) {
    // Never reinject after an ambiguous delivery: it may already have sent.
    if (request.kind !== "habitat:probe") throw error;
    const tab = await chrome.tabs.get(tabId);
    if (tab.discarded || tab.frozen) throw error;
    await chrome.scripting.executeScript({target:{tabId},files:["content.js"]});
    return timedPageMessage(tabId, request);
  }
}

function timedPageMessage(tabId, request) {
  let timer;
  return Promise.race([
    chrome.tabs.sendMessage(tabId, request, {frameId:0}),
    new Promise((_, reject) => { timer = setTimeout(() => reject(Error("ChatGPT page did not respond")), 2000); })
  ]).finally(() => clearTimeout(timer));
}

function heartbeat(socket, ready, issue, model = "") {
  if (socket === habitatSocket && socket.readyState === WebSocket.OPEN)
    socket.send(JSON.stringify({kind:"heartbeat",ready,issue,model}));
}

function closeTransport() {
  const old = habitatSocket;
  habitatSocket = null;
  if (old) old.close();
  void restoreProtectedTab();
}

function scheduleReconnect() {
  if (retryTimer !== null) return;
  const ms = [1000,2000,5000,10000,20000][Math.min(retryCount++,4)];
  retryTimer = setTimeout(() => { retryTimer = null; void startTransport(); }, ms);
}

async function protectBoundTab(tab) {
  // Affect only this tab, never global Chrome performance settings. Preserve
  // the user's previous value and restore it when HABITAT disconnects.
  protectionChange = protectionChange.catch(() => {}).then(async () => {
    const saved = await chrome.storage.session.get("habitatProtectedTab");
    if (saved.habitatProtectedTab?.id !== tab.id) {
      await restoreProtectedTabCore();
      await chrome.storage.session.set({habitatProtectedTab:{id:tab.id,autoDiscardable:tab.autoDiscardable !== false}});
    }
    await chrome.tabs.update(tab.id, {autoDiscardable:false});
  });
  return protectionChange;
}

async function restoreProtectedTab() {
  protectionChange = protectionChange.catch(() => {}).then(restoreProtectedTabCore);
  return protectionChange;
}

async function restoreProtectedTabCore() {
  try {
    const saved = await chrome.storage.session.get("habitatProtectedTab");
    if (!saved.habitatProtectedTab) return;
    await chrome.tabs.update(saved.habitatProtectedTab.id, {autoDiscardable:saved.habitatProtectedTab.autoDiscardable});
    await chrome.storage.session.remove("habitatProtectedTab");
  } catch (_) {}
}
