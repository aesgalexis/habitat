// Shared authenticated transport for independent Google and YouTube search tabs.
function createSearchTransport(config) {
  const SEARCH_ALARM = config.alarm;
  let searchSocket = null, searchStarting = null, searchRetry = null;
  let searchJob = null, searchCache = null;
  let searchResetJob = null, searchResetCache = null;
  let searchTabSelection = Promise.resolve();
  let pageChanges = Promise.resolve();
  const pageLeases = new Map();

  chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === SEARCH_ALARM) void startSearchTransport(); });
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area === "local" && (changes.habitatDisabled || changes.habitatConsentVersion)) {
      const old = searchSocket;
      searchSocket = null;
      searchJob = null;
      searchCache = null;
      old?.close();
      void startSearchTransport();
    }
  });

  async function startSearchTransport() {
    if (searchStarting) return searchStarting;
    searchStarting = (async () => {
      if (searchSocket && searchSocket.readyState <= WebSocket.OPEN) return;
      if (!await habitatConnectionEnabled()) return;
      await chrome.alarms.create(SEARCH_ALARM, {periodInMinutes:0.5});
      const response = await fetch("http://127.0.0.1:8765/pair", {headers:{"X-Habitat-Pair":"1"}, cache:"no-store"});
      if (!response.ok) throw Error("Desktop unavailable");
      const credentials = await response.json();
      if (typeof credentials.key !== "string") throw Error("Desktop unavailable");
      const socket = new WebSocket("ws://127.0.0.1:8765/" + config.endpoint);
      searchSocket = socket;
      socket.onopen = () => socket.send(JSON.stringify({key:credentials.key, searchProtocol:1}));
      socket.onmessage = event => {
        if (searchSocket !== socket) return;
        try {
          const packet = JSON.parse(event.data);
          if (packet.kind !== "search-tick") return;
          socket.send(JSON.stringify({kind:"search-pong"}));
          if (typeof packet.reset === "string") { void dispatchSearchReset(socket, packet.reset); return; }
          if (!packet.request) { searchJob = null; return; }
          void dispatchSearch(socket, packet.request);
        } catch (_) {}
      };
      socket.onclose = () => {
        if (searchSocket !== socket) return;
        searchSocket = null;
        searchJob = null;
        // Cached results belong to a desktop session, whose IDs are unique.
        searchCache = null;
        scheduleSearchReconnect();
      };
      socket.onerror = () => { if (socket.readyState === WebSocket.OPEN) socket.close(); };
    })().catch(scheduleSearchReconnect).finally(() => { searchStarting = null; });
    return searchStarting;
  }

  function scheduleSearchReconnect() {
    if (searchRetry !== null) return;
    searchRetry = setTimeout(() => { searchRetry = null; void startSearchTransport(); }, 2000);
  }


  function acquireSearchTab(url, current, reuse) {
    const selection = searchTabSelection.then(async () => {
      if (!current()) return null;
      const saved = await chrome.storage.session.get(config.tabKey);
      let tab;
      if (Number.isInteger(saved[config.tabKey])) {
        try { tab = await chrome.tabs.get(saved[config.tabKey]); } catch (_) {}
        // Only reuse our own tab. A search the user opened is left intact.
        if (!config.isOwned(tab?.pendingUrl || tab?.url)) tab = null;
      }
      if (!current()) return null;
      tab = tab ? reuse && (config.samePage ? config.samePage(tab.url, url) : tab.url === url) && !tab.pendingUrl ? tab : await chrome.tabs.update(tab.id, {url})
        : await chrome.tabs.create({url, active:false});
      await chrome.storage.session.set({[config.tabKey]:tab.id});
      return tab;
    });
    searchTabSelection = selection.catch(() => null);
    return selection;
  }


  async function dispatchSearchReset(socket, id) {
    if (!/^[a-f0-9]{32}$/.test(id) || socket !== searchSocket) return;
    if (searchResetCache?.id === id) { socket.send(JSON.stringify(searchResetCache)); return; }
    if (searchResetJob?.id === id) return;
    const reset = {id};
    searchResetJob = reset;
    searchJob = null;
    searchCache = null;
    // Wait for any tab creation already in progress, so clearing during navigation
    // cannot leave an orphan tab or close the following search's tab.
    const cleanup = searchTabSelection.then(async () => {
      const saved = await chrome.storage.session.get(config.tabKey);
      let closed = false;
      if (Number.isInteger(saved[config.tabKey])) {
        let tab;
        try { tab = await chrome.tabs.get(saved[config.tabKey]); } catch (_) {}
        if (tab && config.isOwned(tab.pendingUrl || tab.url)) {
          try { await chrome.tabs.remove(tab.id); closed = true; }
          catch (error) {
            // A user may have closed it between get and remove. Other failures
            // leave the ID intact and are retried on the next desktop tick.
            let remaining;
            try { remaining = await chrome.tabs.get(tab.id); } catch (_) {}
            if (remaining) throw error;
          }
        }
        await chrome.storage.session.remove(config.tabKey);
      }
      return closed;
    });
    searchTabSelection = cleanup.catch(() => null);
    try {
      const closed = await cleanup;
      const response = {kind:"search-reset", id, closed};
      searchResetCache = response;
      if (socket === searchSocket && socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(response));
    } catch (_) { /* Keep the desktop reset pending for redelivery. */ }
    finally { if (searchResetJob === reset) searchResetJob = null; }
  }

  async function dispatchSearch(socket, request) {
    if (!request || typeof request.id !== "string" || !/^[a-f0-9]{32}$/.test(request.id) ||
        typeof request.query !== "string" || !request.query.trim() || request.query.length > 2048 ||
        !Number.isInteger(request.start) || request.start < 0 || request.start > 990) return;
    if (searchCache?.id === request.id) { socket.send(JSON.stringify(searchCache)); return; }
    if (searchJob?.id === request.id) return;
    const job = {id:request.id};
    searchJob = job;
    const current = () => searchJob === job && searchSocket === socket && socket.readyState === WebSocket.OPEN;
    let response;
    let diagnostic = "navigation";
    let pageLease;
    try {
      const url = config.url(request.query, request.start);
      let tab = await acquireSearchTab(url, current, config.reusePages);
      if (!current() || !tab) return;
      const tabId = tab.id, deadline = Date.now() + 45000;
      if (config.keepActive) pageLease = await acquirePageLease(tabId);
      let stable = "", stableSince = 0;
      while (current() && Date.now() < deadline) {
        tab = await chrome.tabs.get(tabId);
        if (tab.status === "complete" && !config.isPage(tab.url)) {
          throw Error(config.pageError(tab.url));
        }
        if (tab.status === "complete") {
          const injection = await chrome.scripting.executeScript({target:{tabId}, func:config.read,
            args:[request.query, request.start]});
          if (!current()) return;
          const page = injection[0]?.result;
          diagnostic = page?.stage || "page-read";
          if (page?.error) throw Error(page.error);
          if (page?.ready && Array.isArray(page.results)) {
            const signature = JSON.stringify([page.results, page.nextStart]);
            if (stable !== signature) { stable = signature; stableSince = Date.now(); }
            else if (Date.now() - stableSince >= 700) {
              response = {kind:"search-result", id:request.id, results:page.results, nextStart:page.nextStart ?? null};
              break;
            }
          } else { stable = ""; stableSince = 0; }
        }
        await new Promise(resolve => setTimeout(resolve, 300));
      }
      if (!response && current()) throw Error("timeout");
    } catch (error) {
      if (!current()) return;
      const code = ["consent","blocked","page-changed","timeout","result","background-permission","background-unavailable"].includes(error.message) ? error.message
        : /permission|Cannot access|host permission/i.test(String(error)) ? "permission" : "result";
      response = {kind:"search-result", id:request.id, error:code, diagnostic};
    } finally { await releasePageLease(pageLease); }
    if (response && current()) { searchCache = response; socket.send(JSON.stringify(response)); }
  }

  function changePage(action) {
    const operation = pageChanges.then(action);
    pageChanges = operation.catch(() => null);
    return operation;
  }

  function acquirePageLease(tabId) {
    return changePage(async () => {
      const existing = pageLeases.get(tabId);
      if (existing) { existing.users++; return existing; }
      if (!chrome.debugger) throw Error("background-permission");
      const tab = await chrome.tabs.get(tabId);
      if (!config.isPage(tab.pendingUrl || tab.url)) throw Error(config.pageError(tab.url));
      const target = {tabId};
      try { await chrome.debugger.attach(target, "1.3"); } catch (_) { throw Error("background-unavailable"); }
      try { await chrome.debugger.sendCommand(target, "Emulation.setFocusEmulationEnabled", {enabled:true}); }
      catch (_) {
        try { await chrome.debugger.detach(target); } catch (_) {}
        throw Error("background-unavailable");
      }
      const lease = {target, users:1};
      pageLeases.set(tabId, lease);
      return lease;
    });
  }

  function releasePageLease(lease) {
    if (!lease) return Promise.resolve();
    return changePage(async () => {
      if (--lease.users > 0) return;
      pageLeases.delete(lease.target.tabId);
      try { await chrome.debugger.sendCommand(lease.target, "Emulation.setFocusEmulationEnabled", {enabled:false}); } catch (_) {}
      try { await chrome.debugger.detach(lease.target); } catch (_) {}
    });
  }

  return {start:startSearchTransport, dispatch:dispatchSearch, reset:dispatchSearchReset, cancel:() => { searchJob = null; }};
}
