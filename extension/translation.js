// Translate through the real Google Translate page, never a translation API.
// This channel is independent of the ChatGPT tab and never sends chat messages.
const TRANSLATION_ALARM = "habitat-translation";
let translationSocket = null;
let translationStarting = null;
let translationRetry = null;
let translationJob = null;
let translationCache = null;
let translationTabSelection = Promise.resolve();
let translationPageChanges = Promise.resolve();
const translationPageLeases = new Map();

chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === TRANSLATION_ALARM) void startTranslationTransport(); });
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && (changes.habitatDisabled || changes.habitatConsentVersion)) {
    const old = translationSocket;
    translationSocket = null;
    translationJob = null;
    old?.close();
    void startTranslationTransport();
  }
});

async function startTranslationTransport() {
  if (translationStarting) return translationStarting;
  translationStarting = (async () => {
    if (translationSocket && translationSocket.readyState <= WebSocket.OPEN) return;
    if (!await habitatConnectionEnabled()) return;
    await chrome.alarms.create(TRANSLATION_ALARM, {periodInMinutes:0.5});
    const response = await fetch("http://127.0.0.1:8765/pair", {headers:{"X-Habitat-Pair":"1"}, cache:"no-store"});
    if (!response.ok) throw Error("Desktop unavailable");
    const credentials = await response.json();
    if (typeof credentials.key !== "string") throw Error("Desktop unavailable");
    const socket = new WebSocket("ws://127.0.0.1:8765/translate-bridge");
    translationSocket = socket;
    socket.onopen = () => socket.send(JSON.stringify({key:credentials.key, translationProtocol:1}));
    socket.onmessage = event => {
      if (translationSocket !== socket) return;
      try {
        const packet = JSON.parse(event.data);
        if (packet.kind !== "translation-tick") return;
        socket.send(JSON.stringify({kind:"translation-pong"}));
        if (!packet.request) { translationJob = null; return; }
        void dispatchTranslation(socket, packet.request);
      } catch (_) {}
    };
    socket.onclose = () => {
      if (translationSocket !== socket) return;
      translationSocket = null;
      translationJob = null;
      scheduleTranslationReconnect();
    };
    socket.onerror = () => { if (socket.readyState === WebSocket.OPEN) socket.close(); };
  })().catch(scheduleTranslationReconnect).finally(() => { translationStarting = null; });
  return translationStarting;
}

function scheduleTranslationReconnect() {
  if (translationRetry !== null) return;
  translationRetry = setTimeout(() => { translationRetry = null; void startTranslationTransport(); }, 2000);
}

function isTranslatePage(value) {
  try { return new URL(value).origin === "https://translate.google.com"; } catch (_) { return false; }
}

function isTranslateTab(tab) {
  return isTranslatePage(tab?.pendingUrl || tab?.url);
}

function acquireTranslationTab(url, current) {
  // Serialize discovery and creation so a replacement request cannot create a
  // second tab while the previous request is still opening the first one.
  const selection = translationTabSelection.then(async () => {
    if (!current()) return null;
    const saved = await chrome.storage.session.get("habitatTranslateTab");
    let tab;
    if (Number.isInteger(saved.habitatTranslateTab)) {
      try { tab = await chrome.tabs.get(saved.habitatTranslateTab); } catch (_) {}
      if (!isTranslateTab(tab)) tab = null;
    }
    if (!tab) {
      const open = await chrome.tabs.query({url:"https://translate.google.com/*"});
      tab = open.filter(isTranslateTab).sort((a, b) => Number(b.active) - Number(a.active) ||
        (b.lastAccessed || 0) - (a.lastAccessed || 0))[0];
    }
    if (!current()) return null;
    tab = tab ? await chrome.tabs.update(tab.id, {url}) : await chrome.tabs.create({url, active:false});
    // Keep the tab even if this request was canceled while Chrome opened it.
    // The next request can then reuse it instead of opening another one.
    await chrome.storage.session.set({habitatTranslateTab:tab.id});
    return tab;
  });
  translationTabSelection = selection.catch(() => null);
  return selection;
}

function changeTranslationPage(change) {
  const operation = translationPageChanges.then(change);
  translationPageChanges = operation.catch(() => null);
  return operation;
}

function keepTranslationPageActive(tabId) {
  return changeTranslationPage(async () => {
    let lease = translationPageLeases.get(tabId);
    if (lease) { lease.users++; return lease; }
    if (!chrome.debugger) throw Error("background-permission");
    if (!isTranslateTab(await chrome.tabs.get(tabId))) throw Error("page-changed");
    const target = {tabId};
    try { await chrome.debugger.attach(target, "1.3"); }
    catch (_) { throw Error("background-unavailable"); }
    try {
      // Keep the document rendering even in an inactive tab or minimized
      // window. This emulates page focus; it never changes OS or browser focus.
      await chrome.debugger.sendCommand(target, "Emulation.setFocusEmulationEnabled", {enabled:true});
    } catch (_) {
      try { await chrome.debugger.detach(target); } catch (_) {}
      throw Error("background-unavailable");
    }
    lease = {target, users:1};
    translationPageLeases.set(tabId, lease);
    return lease;
  });
}

function releaseTranslationPage(lease) {
  if (!lease) return Promise.resolve();
  return changeTranslationPage(async () => {
    if (--lease.users > 0) return;
    translationPageLeases.delete(lease.target.tabId);
    try { await chrome.debugger.sendCommand(lease.target, "Emulation.setFocusEmulationEnabled", {enabled:false}); } catch (_) {}
    try { await chrome.debugger.detach(lease.target); } catch (_) {}
  });
}

async function dispatchTranslation(socket, request) {
  if (!request || typeof request.id !== "string" || !/^[a-f0-9]{32}$/.test(request.id) ||
      typeof request.text !== "string" || request.text.length > 10000 || !["en","el","es"].includes(request.target)) return;
  if (translationCache?.id === request.id) { socket.send(JSON.stringify(translationCache)); return; }
  if (translationJob?.id === request.id) return;
  const job = {id:request.id};
  translationJob = job;
  const current = () => translationJob === job && translationSocket === socket && socket.readyState === WebSocket.OPEN;
  let tabId;
  let pageLease;
  let response;
  try {
    const url = "https://translate.google.com/?hl=en&sl=auto&tl=" + request.target + "&op=translate";
    let tab = await acquireTranslationTab(url, current);
    if (!current() || !tab) return;
    tabId = tab.id;
    pageLease = await keepTranslationPageActive(tabId);
    if (!current()) return;
    const deadline = Date.now() + 45000;
    let pasted = false, stable = "", stableSince = 0, diagnostic = "navigation";
    while (current() && Date.now() < deadline) {
      tab = await chrome.tabs.get(tabId);
      if (tab.status === "complete" && !isTranslatePage(tab?.url)) {
        if (tab?.url?.startsWith("https://consent.google.com/")) throw Error("consent");
        throw Error("page-changed");
      }
      if (tab.status === "complete") {
        const results = await chrome.scripting.executeScript({target:{tabId}, func:translatePageStep,
          args:[pasted ? "read" : "paste", request.id, request.text, request.target]});
        if (!current()) return;
        const result = results[0]?.result;
        diagnostic = result?.stage || "no-page-result";
        if (result?.error) throw Error(result.error);
        if (result?.pasted) pasted = true;
        if (typeof result?.text === "string" && result.text.length > 0) {
          if (stable !== result.text) { stable = result.text; stableSince = Date.now(); }
          else if (Date.now() - stableSince >= 1000) {
            response = {kind:"translation-result", id:request.id, text:stable,
              diagnostic:"extension:" + chrome.runtime.getManifest().version + ";tab:" + tabId +
                ";background:true"};
            return;
          }
        } else { stable = ""; stableSince = 0; }
      }
      await new Promise(resolve => setTimeout(resolve, 300));
    }
    if (current()) { const error = Error("timeout"); error.diagnostic = diagnostic; throw error; }
  } catch (error) {
    if (!current()) return;
    const code = ["consent","page-changed","timeout","background-permission","background-unavailable"].includes(error.message) ? error.message
      : /permission|Cannot access|host permission/i.test(String(error)) ? "permission" : "result";
    response = {kind:"translation-result", id:request.id, error:code, diagnostic:error.diagnostic || code};
  } finally {
    await releaseTranslationPage(pageLease);
    // Release the page override before reporting completion or caching the reply.
    if (response && current()) {
      translationCache = response;
      socket.send(JSON.stringify(response));
    }
  }
}

// Serialized by Chrome and run in the translator tab's isolated content world.
// Selectors were checked against Google Translate's rendered page.
function translatePageStep(action, id, text, target) {
  if (location.origin !== "https://translate.google.com" || new URL(location.href).searchParams.get("tl") !== target)
    return {error:"page-changed"};
  const source = document.querySelector('textarea[aria-label="Source text"]');
  if (!source) return {stage:"source-missing:" + document.documentElement.lang + ":" + document.querySelectorAll("textarea").length};
  const normalize = value => value.replace(/\r\n?/g, "\n");
  if (action === "paste") {
    globalThis.__habitatTranslateJob = id;
    source.focus();
    const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value").set;
    setter.call(source, text);
    source.dispatchEvent(new InputEvent("input", {bubbles:true, inputType:"insertText", data:text}));
    return {pasted:true, stage:"pasted"};
  }
  if (globalThis.__habitatTranslateJob !== id || normalize(source.value) !== normalize(text)) return {error:"page-changed"};
  const visible = element => {
    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && getComputedStyle(element).visibility !== "hidden";
  };
  const copy = document.querySelector('button[aria-label="Copy translation"]');
  if (!copy || copy.disabled || !visible(copy)) return {stage:"copy-unavailable:" + Boolean(copy) + ":" +
    document.querySelectorAll('span[jsname="W297wb"]').length + ":" + document.visibilityState};
  const spans = [...document.querySelectorAll('span[jsname="W297wb"]')].filter(element =>
    visible(element) && element.closest("[lang]")?.getAttribute("lang") === target);
  return {text:spans.map(element => element.textContent).join(""), stage:"result-spans:" + spans.length + ":" + document.visibilityState};
}

void startTranslationTransport();
