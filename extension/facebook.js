// Facebook is optional. Read only rendered Contacts or All friends on demand;
// never inspect messages, cookies, page internals or private HTTP endpoints.
function readFacebookContacts() {
  if (location.origin !== "https://www.facebook.com" && location.origin !== "https://facebook.com") return {error:"page-changed"};
  if (/^\/(checkpoint|two_step_verification)/.test(location.pathname)) return {error:"blocked"};
  if (/^\/login/.test(location.pathname) || document.querySelector('input[type="password"]')) return {error:"login"};
  const visible = element => {
    const box = element.getBoundingClientRect();
    return box.width > 0 && box.height > 0 && getComputedStyle(element).visibility !== "hidden";
  };
  const text = value => String(value || "").replace(/[\u200b-\u200f\ufeff]/g,"").replace(/[\u0000-\u001f\u007f]/g," ").replace(/\s+/g," ").trim();
  const friends = /^\/friends\/list\/?$/.test(location.pathname);
  // Never confuse requests, suggestions, stories or feed authors with friends.
  const labels = new Set(friends ? ["all friends","todos los amigos","todos tus amigos","todos os amigos","alle freunde","tous les amis","tutti gli amici"] :
    ["contacts","contactos","contatos","kontakte","contatti"]);
  let region, heading, next;
  if (!friends && location.pathname !== "/") return {error:"page-changed"};
  const regions = [...document.querySelectorAll(friends ? 'nav,[role="navigation"]' : '[role="complementary"],aside,[role="region"][aria-label]')];
  for (const candidate of regions) {
    if (!visible(candidate)) continue;
    const headings = [...candidate.querySelectorAll('h2,h3,[role="heading"]')].filter(visible);
    const index = headings.findIndex(item => labels.has(text(item.textContent).toLowerCase()));
    // All friends includes a second heading with the total count, before rows.
    if (index >= 0) { region = candidate; heading = headings[index]; next = friends ? null : headings[index + 1]; break; }
    if (labels.has(text(candidate.getAttribute("aria-label")).toLowerCase())) { region = candidate; break; }
  }
  if (!region) return {error:"contacts-unavailable",diagnostic:{stage:"section-missing",sections:regions.length}};
  const contacts = [], seen = new Set();
  const photos = [...region.querySelectorAll("img,svg image")];
  let rowCount = 0;
  for (const photo of photos) {
    if (!visible(photo) || heading && !(heading.compareDocumentPosition(photo) & 4) ||
        next && !(photo.compareDocumentPosition(next) & 4)) continue;
    const source = photo.currentSrc || photo.getAttribute("src") || photo.getAttribute("href") || photo.getAttribute("xlink:href");
    let image;
    try {
      image = new URL(source, location.href);
      if (image.protocol !== "https:" || image.username || image.password || image.port || image.href.length > 8192 ||
          !(image.hostname === "fbcdn.net" || image.hostname.endsWith(".fbcdn.net"))) continue;
    } catch (_) { continue; }
    let row = photo.closest('a[href],[role="button"],[role="listitem"],li');
    if (!row || !region.contains(row)) continue;
    // A section-wide button/list item must never become an invented contact.
    if (row.querySelectorAll("img,svg image").length !== 1) continue;
    rowCount++;
    let name = text(row.getAttribute("aria-label")) || text(row.textContent) || text(photo.getAttribute("alt"));
    // Facebook's photo-only profile link can take its accessible name from SVG.
    if (!name) name = text(photo.closest('[aria-label]')?.getAttribute("aria-label"));
    name = name.replace(/(?:[,·]?\s*(?:Active now|Activo ahora|Activa ahora|En línea))$/i, "").trim();
    if (!name || name.length > 200 || /^(?:profile picture|foto del perfil|imagen del perfil)$/i.test(name)) continue;
    let link = null;
    const anchor = row.matches("a[href]") ? row : row.querySelector("a[href]");
    if (anchor) {
      try {
        const url = new URL(anchor.getAttribute("href"), location.href);
        if (url.protocol === "https:" && !url.username && !url.password && !url.port &&
            ["www.facebook.com","facebook.com"].includes(url.hostname)) {
          const path = url.pathname.replace(/^\/|\/$/g, "");
          const reserved = new Set(["login","checkpoint","recover","friends","groups","marketplace","watch","reel","stories","settings","logout.php","l.php","help","notifications"]);
          if (path === "profile.php" && /^\d+$/.test(url.searchParams.get("id") || ""))
            link = "https://www.facebook.com/profile.php?id=" + url.searchParams.get("id");
          else if (/^messages\/t\/[A-Za-z0-9._-]+$/.test(path) || /^[A-Za-z0-9.]+$/.test(path) && !reserved.has(path))
            link = "https://www.facebook.com/" + path;
        }
      } catch (_) {}
    }
    if (friends && !link) continue;
    const identity = link || name + "\n" + image.href;
    if (seen.has(identity)) continue;
    seen.add(identity);
    contacts.push({name,photoUrl:image.href,link});
    if (contacts.length === 200) break;
  }
  return {contacts,diagnostic:{stage:"section-read",sections:regions.length,photos:photos.length,rows:rowCount}};
}

const facebookTransport = (() => {
  let socket = null, starting = null, retry = null, job = null, cache = null;
  let lastTick = 0, connectedAt = 0, issue = null, readiness = null, ready = null;
  let lastRead = null;
  let tabSelection = Promise.resolve();
  const alarm = "habitat-facebook";
  const origin = "https://www.facebook.com/*";
  const isFacebook = value => {
    try { const url = new URL(value); return url.protocol === "https:" && !url.username && !url.password && !url.port &&
      ["www.facebook.com","facebook.com"].includes(url.hostname); } catch (_) { return false; }
  };
  const schedule = () => { if (retry === null) retry = setTimeout(() => { retry = null; void start(); }, 2000); };
  const disconnect = () => {
    const old = socket; socket = null; job = cache = null; lastTick = connectedAt = 0; lastRead = null;
    ready?.(); ready = null; old?.close();
  };
  const status = () => ({facebookProtocol:1,connected:socket?.readyState === WebSocket.OPEN && lastTick > 0 && Date.now() - lastTick < 10000,
    connecting:!!starting || socket?.readyState === 0, issue,lastRead,
    extensionVersion:chrome.runtime?.getManifest?.().version || "unknown"});
  function watch(current) {
    setTimeout(() => {
      if (socket !== current) return;
      if (Date.now() - (lastTick || connectedAt) >= 10000) {
        issue = "The Facebook desktop connection stopped responding.";
        disconnect(); void start();
      } else watch(current);
    }, 10000);
  }
  async function start() {
    if (starting) return starting;
    starting = (async () => {
      if (!await habitatConnectionEnabled()) return;
      if (socket && socket.readyState <= WebSocket.OPEN && Date.now() - (lastTick || connectedAt) < 10000) return;
      if (socket) disconnect();
      await chrome.alarms.create(alarm, {periodInMinutes:0.5});
      const response = await fetch("http://127.0.0.1:8765/pair", {headers:{"X-Habitat-Pair":"1"},cache:"no-store",signal:AbortSignal.timeout(5000)});
      if (!response.ok) throw Error("Desktop unavailable");
      const auth = await response.json();
      if (typeof auth.key !== "string") throw Error("Desktop unavailable");
      const current = new WebSocket("ws://127.0.0.1:8765/facebook-bridge");
      socket = current;
      connectedAt = Date.now(); lastTick = 0; issue = null;
      readiness = new Promise(resolve => { ready = resolve; });
      watch(current);
      current.onopen = () => current.send(JSON.stringify({key:auth.key,facebookProtocol:1,
        extensionVersion:chrome.runtime?.getManifest?.().version || "unknown"}));
      current.onmessage = event => {
        if (socket !== current) return;
        try {
          const packet = JSON.parse(event.data);
          if (packet.kind !== "facebook-tick") return;
          lastTick = Date.now(); issue = null; ready?.(); ready = null;
          current.send(JSON.stringify({kind:"facebook-pong"}));
          if (!packet.request) { job = cache = null; return; }
          void dispatch(current, packet.request);
        } catch (_) {}
      };
      current.onclose = () => {
        if (socket === current) { issue ??= "Facebook disconnected from HABITAT."; disconnect(); schedule(); }
      };
      current.onerror = () => { if (socket === current) issue = "Could not connect Facebook to the HABITAT desktop app."; current.close(); };
    })().catch(() => { issue = "Open HABITAT to connect Facebook."; schedule(); }).finally(() => { starting = null; });
    return starting;
  }
  async function connect(force = false) {
    if (starting) await starting;
    if (force) disconnect();
    await start();
    if (!status().connected && readiness) await Promise.race([readiness,new Promise(resolve => setTimeout(resolve,5000))]);
    return status();
  }
  async function dispatch(current, request) {
    if (!request || !/^[a-f0-9]{32}$/.test(request.id) || current !== socket) return;
    if (cache?.id === request.id) { current.send(JSON.stringify(cache)); return; }
    if (job?.id === request.id) return;
    const active = {id:request.id}; job = active; lastRead = null;
    const valid = () => socket === current && job === active && current.readyState === WebSocket.OPEN;
    let diagnostic = {stage:"permission"}, progressSignature = "";
    const progress = metadata => {
      diagnostic = metadata;
      if (!valid()) return;
      const signature = JSON.stringify(metadata);
      if (signature === progressSignature) return;
      progressSignature = signature;
      current.send(JSON.stringify({kind:"facebook-progress",id:request.id,diagnostic:metadata}));
    };
    let result;
    try {
      if (!await habitatConnectionEnabled()) return;
      progress(diagnostic);
      if (!await chrome.permissions.contains({origins:[origin]})) throw Error("permission");
      progress({stage:"tab-selection"});
      const selection = tabSelection.then(async () => {
        if (!valid()) return null;
        const tabs = await chrome.tabs.query({url:["https://www.facebook.com/*","https://facebook.com/*"]});
        if (!valid()) return null;
        // Prefer an explicit friends list; Home may not render Contacts at all.
        const supported = item => isFacebook(item.url) && /^\/(?:friends\/list\/?)?$/.test(new URL(item.url).pathname);
        let tab = tabs.filter(supported)
          .sort((a,b) => Number(new URL(b.url).pathname.startsWith("/friends/list")) - Number(new URL(a.url).pathname.startsWith("/friends/list")) ||
            Number(b.active) - Number(a.active) || (b.lastAccessed || 0) - (a.lastAccessed || 0))[0];
        if (!tab) {
          const saved = await chrome.storage.session.get("habitatFacebookTab");
          if (Number.isInteger(saved.habitatFacebookTab)) {
            try { const own = await chrome.tabs.get(saved.habitatFacebookTab); if (supported({...own,url:own.pendingUrl || own.url})) tab = own; } catch (_) {}
          }
        }
        if (!valid()) return null;
        if (!tab) {
          tab = tabs.find(item => isFacebook(item.url) && /^\/(login|checkpoint|two_step_verification)/.test(new URL(item.url).pathname));
          if (!tab) {
            tab = await chrome.tabs.create({url:"https://www.facebook.com/friends/list/",active:false});
            await chrome.storage.session.set({habitatFacebookTab:tab.id});
          }
        }
        return tab;
      });
      tabSelection = selection.catch(() => null);
      let tab = await selection;
      if (!valid() || !tab) return;
      progress({stage:"page-loading"});
      const deadline = Date.now() + 18000;
      const homeDeadline = Date.now() + 2500;
      while (valid() && Date.now() < deadline) {
        if (!await chrome.permissions.contains({origins:[origin]})) throw Error("permission");
        tab = await chrome.tabs.get(tab.id);
        if (!isFacebook(tab.pendingUrl || tab.url)) throw Error("page-changed");
        if (tab.status === "complete") {
          const read = await chrome.scripting.executeScript({target:{tabId:tab.id},func:readFacebookContacts});
          if (!valid()) return;
          const page = read[0]?.result;
          progress(page?.diagnostic || {stage:"page-read"});
          if (page?.error && page.error !== "contacts-unavailable") throw Error(page.error);
          if (Array.isArray(page?.contacts) && (page.contacts.length || new URL(tab.url).pathname !== "/")) {
            let size = 0;
            result = {contacts:page.contacts.slice(0,200).filter(contact => (size += new TextEncoder().encode(JSON.stringify(contact)).length) <= 180000)};
            break;
          }
          if (new URL(tab.url).pathname === "/" && Date.now() >= homeDeadline) {
            // Read a dedicated list when Home has no contacts. Never navigate,
            // scroll or close a personal tab, nor open a conversation.
            const saved = await chrome.storage.session.get("habitatFacebookTab");
            let own;
            if (Number.isInteger(saved.habitatFacebookTab)) {
              try { const candidate = await chrome.tabs.get(saved.habitatFacebookTab);
                if (isFacebook(candidate.url) && /^\/friends\/list\/?$/.test(new URL(candidate.url).pathname)) own = candidate;
              } catch (_) {}
            }
            if (!valid()) return;
            tab = own || await chrome.tabs.create({url:"https://www.facebook.com/friends/list/",active:false});
            await chrome.storage.session.set({habitatFacebookTab:tab.id});
            progress({stage:"page-loading"});
          }
        }
        await new Promise(resolve => setTimeout(resolve, 500));
      }
      result ??= {error:"contacts-unavailable"};
    } catch (error) {
      result = {error:["permission","login","blocked","page-changed"].includes(error.message) ? error.message :
        /permission|Cannot access/i.test(String(error)) ? "permission" : "result"};
    }
    if (valid()) {
      lastRead = {stage:diagnostic.stage,error:result.error || null,contactCount:result.contacts?.length ?? null};
      cache = {kind:"facebook-result",id:request.id,...result,diagnostic}; current.send(JSON.stringify(cache));
    }
  }
  chrome.alarms.onAlarm.addListener(item => { if (item.name === alarm) void start(); });
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area === "local" && (changes.habitatDisabled || changes.habitatConsentVersion)) { disconnect(); void start(); }
  });
  chrome.permissions?.onRemoved?.addListener(() => { disconnect(); void start(); });
  chrome.permissions?.onAdded?.addListener(change => {
    if (change.origins?.some(value => value.includes("facebook.com"))) void connect(true);
  });
  void start();
  return {dispatch,start,connect,status};
})();
