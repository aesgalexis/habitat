// Read-only observations of already-open, opted-in pages. Never read cookies,
// message bodies, hidden application state, or private HTTP endpoints.
function readGoogleBrowserContacts() {
  if (!["https://mail.google.com", "https://contacts.google.com"].includes(location.origin)) return {error:"page-changed"};
  if (document.querySelector('input[type="password"]')) return {error:"login"};
  const text = value => String(value || "").replace(/[\u200b-\u200f\ufeff]/g, "").replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g," ").trim();
  const visible = element => {
    const box = element.getBoundingClientRect();
    return box.width > 0 && box.height > 0 && getComputedStyle(element).visibility !== "hidden" && !element.closest?.('[aria-hidden="true"]');
  };
  const pattern = /[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9.-]*[A-Z0-9])?\.[A-Z]{2,63}/ig;
  const emails = value => [...String(value || "").matchAll(pattern)].map(m => m[0]).filter(e => e.length <= 320);
  const accountEmails = new Set();
  for (const anchor of document.querySelectorAll('a[href*="accounts.google.com/SignOutOptions"],a[href*="accounts.google.com"][aria-label*="@"],button[aria-label*="Google Account"][aria-label*="@"]')) {
    if (!visible(anchor)) continue;
    for (const email of emails(anchor.getAttribute("aria-label") || anchor.getAttribute("title"))) accountEmails.add(email.toLowerCase());
  }
  // /u/0 is an account position, not an identity. Never attach private source
  // records to that position across a sign-out/account switch.
  if (accountEmails.size !== 1) return {error:"account-unavailable"};
  const accountId = [...accountEmails][0];
  const contacts = new Map();
  const add = (name, email, phone, id, page) => {
    email = email && emails(email).find(e => e.toLowerCase() === email.toLowerCase());
    if (email?.toLowerCase() === accountId) return;
    name = text(name) || email || "Unnamed contact";
    if (name.length > 200) name = email && email.length <= 200 ? email : "Email contact";
    phone = text(phone);
    if (phone.length > 100) phone = "";
    if (!email && !phone && !id) return;
    const key = email?.toLowerCase() || id || name + "\n" + phone;
    if (!contacts.has(key) && contacts.size < 200) contacts.set(key, {id:id || null,name,email:email || null,phone:phone || null,page});
  };
  if (location.origin === "https://mail.google.com") {
    for (const sender of document.querySelectorAll('tr.zA [email],tr[role="row"] [email],[role="main"] [role="row"] [email]')) {
      if (!visible(sender)) continue;
      const email = text(sender.getAttribute("email"));
      // Read only the correspondent's own element, never its mail row text.
      add(sender.getAttribute("name") || sender.textContent, email, "", null, "Gmail");
    }
  } else {
    for (const row of document.querySelectorAll('[role="main"] [role="row"][data-id],[role="main"] [role="listitem"][data-id],[role="row"][data-item-id]')) {
      if (!visible(row)) continue;
      const idValue = row.getAttribute("data-id") || row.getAttribute("data-item-id") || "";
      const id = /^(?:c)?\d{8,32}$/.test(idValue) ? idValue : null;
      const addresses = new Set();
      for (const item of row.querySelectorAll('a[href^="mailto:"],[email],[data-email]')) {
        if (!visible(item)) continue;
        const value = item.getAttribute("email") || item.getAttribute("data-email") || item.getAttribute("href")?.replace(/^mailto:/i, "");
        for (const email of emails(value)) addresses.add(email);
      }
      const cells = [...row.querySelectorAll('[role="cell"],[role="gridcell"]')].filter(visible).map(cell => text(cell.textContent));
      for (const cell of cells) for (const email of emails(cell)) addresses.add(email);
      const phoneLink = row.querySelector('a[href^="tel:"]');
      const phone = phoneLink?.getAttribute("href")?.slice(4) || row.getAttribute("data-phone") ||
        cells.find(cell => /^\+?[\d ()\-.]{7,35}$/.test(cell) && cell.replace(/\D/g, "").length >= 7) || "";
      const name = row.getAttribute("data-name") || text(row.querySelector('[data-column-id="name"]')?.textContent) ||
        cells.find(cell => cell && cell.length <= 200 && emails(cell).length === 0 && !/^\+?[\d ()\-.]+$/.test(cell) && !/^(select|seleccionar)$/i.test(cell));
      if (addresses.size) for (const email of addresses) add(name, email, phone, id, "Google Contacts");
      else add(name, null, phone, id, "Google Contacts");
    }
  }
  return {accountId,accountName:accountId,contacts:[...contacts.values()]};
}

const browserContacts = (() => {
  let socket = null, signature = "", busy = false, nextRead = 0, revision = 0;
  const setups = new Set();
  const invalidate = () => { nextRead = 0; revision++; };
  chrome.tabs.onUpdated?.addListener((_id, change) => { if (change.url || change.status === "complete") invalidate(); });
  chrome.tabs.onRemoved?.addListener(invalidate);
  chrome.permissions?.onAdded?.addListener(invalidate);
  chrome.permissions?.onRemoved?.addListener(invalidate);
  const permission = async origins => {
    try { return await chrome.permissions.contains({origins}); } catch (_) { return false; }
  };
  async function tick(current, config) {
    if (!config || typeof config.facebook !== "boolean" || typeof config.google !== "boolean") return;
    if (socket !== current) { socket = current; nextRead = 0; }
    const changed = JSON.stringify([config.facebook,config.google]);
    if (changed !== signature) { signature = changed; nextRead = 0; }
    if (config.setup && /^[a-f0-9]{32}$/.test(config.setup.id) && ["facebook","google"].includes(config.setup.provider)) {
      const setup = config.setup;
      if (!setups.has(setup.id)) {
        setups.add(setup.id);
        try { await chrome.tabs.create({url:chrome.runtime.getURL("contacts-settings.html") + "?source=" + setup.provider}); }
        catch (_) { setups.delete(setup.id); }
      }
      if (setups.has(setup.id) && current.readyState === WebSocket.OPEN)
        current.send(JSON.stringify({kind:"browser-contacts",setupDone:setup.id}));
    }
    if (busy || Date.now() < nextRead) return;
    busy = true; nextRead = Date.now() + 30000;
    const captured = signature;
    const capturedRevision = revision;
    const valid = () => current === socket && current.readyState === WebSocket.OPEN && captured === signature && capturedRevision === revision;
    try {
      if (!await habitatConnectionEnabled()) return;
      const states = [], snapshots = [];
      for (const provider of ["facebook","google"]) {
        if (!config[provider]) { states.push({provider,allowed:false,open:false,state:"disabled"}); continue; }
        const facebook = provider === "facebook";
        const allowedFacebook = facebook && await permission(["https://www.facebook.com/*","https://facebook.com/*"]);
        const allowedMail = !facebook && await permission(["https://mail.google.com/*"]);
        const allowedContacts = !facebook && await permission(["https://contacts.google.com/*"]);
        if (!valid()) return;
        if (!allowedFacebook && !allowedMail && !allowedContacts) { states.push({provider,allowed:false,open:false,state:"permission"}); continue; }
        const urls = facebook ? ["https://www.facebook.com/*","https://facebook.com/*"] :
          [...(allowedMail ? ["https://mail.google.com/*"] : []),...(allowedContacts ? ["https://contacts.google.com/*"] : [])];
        const allowedHosts = new Set(urls.map(url => new URL(url).hostname));
        const tabs = (await chrome.tabs.query({url:urls})).filter(t => {
          try { const url = new URL(t.url); return t.status === "complete" && !t.discarded && url.protocol === "https:" && allowedHosts.has(url.hostname); }
          catch (_) { return false; }
        }).slice(0,12);
        if (!valid()) return;
        if (!tabs.length) { states.push({provider,allowed:true,open:false,state:"waiting-for-tab"}); continue; }
        const accounts = new Map();
        let login = false, unknownAccount = false;
        for (const tab of tabs) {
          if (!valid()) return;
          try {
            const result = await chrome.scripting.executeScript({target:{tabId:tab.id},func:facebook ? readFacebookContacts : readGoogleBrowserContacts});
            if (!valid()) return;
            const page = result[0]?.result;
            login ||= page?.error === "login";
            unknownAccount ||= page?.error === "account-unavailable";
            if (!Array.isArray(page?.contacts) || !page.contacts.length) continue;
            const accountId = facebook ? page.account?.id || "unidentified" : page.accountId;
            const accountName = facebook ? page.account?.name || "Chrome (account not identified)" : page.accountName;
            if (typeof accountId !== "string" || typeof accountName !== "string") continue;
            let account = accounts.get(accountId);
            if (!account) accounts.set(accountId, account = {provider,accountId,accountName,contacts:new Map()});
            for (const row of page.contacts) {
              const contact = facebook ? {name:row.name,photoUrl:row.photoUrl || null,profileUrl:row.link || null,page:"Facebook"} : row;
              const key = facebook ? row.link || row.name + "\n" + row.photoUrl : row.email?.toLowerCase() || row.id || row.name + "\n" + row.phone;
              if (account.contacts.size < 200 || account.contacts.has(key)) {
                const old = account.contacts.get(key);
                account.contacts.set(key, old && contact.page === "Gmail" && old.page === "Google Contacts" ? old : contact);
              }
            }
          } catch (_) { /* One unavailable tab does not block other accounts. */ }
        }
        let count = 0;
        for (const account of accounts.values()) {
          let bytes = 1024;
          const contacts = [...account.contacts.values()].filter(row => (bytes += new TextEncoder().encode(JSON.stringify(row)).length) < 180000);
          if (contacts.length) { snapshots.push({...account,contacts}); count += contacts.length; }
        }
        states.push({provider,allowed:true,open:true,state:count ? "ready" : login ? "login" : unknownAccount ? "account-unavailable" : "waiting-for-contacts",count});
      }
      if (!valid() || !await habitatConnectionEnabled()) return;
      current.send(JSON.stringify({kind:"browser-contacts",states}));
      for (const snapshot of snapshots) {
        // Repeat observations after each scan so an interrupted desktop save
        // can recover. Stable identities and no-op commits prevent duplicates.
        current.send(JSON.stringify({kind:"browser-contacts",snapshots:[snapshot]}));
      }
    } finally { busy = false; }
  }
  return {tick,invalidate};
})();
