// Explicit desktop submissions only. This module never reads the inbox, uses
// private endpoints, searches people by name or retries an uncertain send.
function facebookMessagePage(action, targetLink, targetName, threadLink, text, resolution = {}) {
  const clean = value => String(value || "").replace(/\u00a0/g," ").replace(/\r\n/g,"\n");
  const label = value => clean(value).normalize("NFC").replace(/[\u200b-\u200f\u202a-\u202e\u2066-\u2069\ufeff]/g,"").replace(/\s+/g," ").trim();
  const visible = item => {
    const rect = item.getBoundingClientRect();
    const style = getComputedStyle(item);
    return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
  };
  const canonical = value => {
    if (typeof value !== "string" || !value.trim()) return null;
    try {
      const url = new URL(value,location.href);
      if (url.protocol !== "https:" || !["facebook.com","www.facebook.com"].includes(url.hostname) || url.username || url.password || url.port) return null;
      const path = url.pathname.replace(/\/$/,"");
      return "https://www.facebook.com" + path + (path === "/profile.php" ? "?id=" + url.searchParams.get("id") : "");
    } catch (_) { return null; }
  };
  const thread = value => {
    const url = canonical(value);
    return url && /^https:\/\/www\.facebook\.com\/messages\/(?:e2ee\/)?t\/[A-Za-z0-9._-]+$/.test(url) ? url : null;
  };
  if (!canonical(location.href)) return {error:"recipient"};
  if (/^\/(checkpoint|two_step_verification)/.test(location.pathname)) return {error:"blocked"};
  if (/^\/login/.test(location.pathname) || document.querySelector('input[type="password"]')) return {error:"login"};
  const profile = thread(targetLink) ? null : canonical(targetLink);
  const main = [...document.querySelectorAll('[role="main"]')].filter(visible);
  const aliases = new Set([profile || thread(threadLink) || thread(targetLink)]);
  const addProfile = value => {
    const url = canonical(value);
    if (!url || thread(url)) return;
    aliases.add(url);
    const id = /^https:\/\/www\.facebook\.com\/(\d+)$/.exec(url)?.[1] ||
      /^https:\/\/www\.facebook\.com\/profile\.php\?id=(\d+)$/.exec(url)?.[1];
    if (id) { aliases.add("https://www.facebook.com/" + id); aliases.add("https://www.facebook.com/profile.php?id=" + id); }
  };
  // Facebook can expose the same profile as a username and as a numeric ID.
  // Only page-owned canonical/app-link metadata can establish that alias.
  if (profile && canonical(location.href) === profile) {
    for (const selector of ['link[rel="canonical"]','meta[property="og:url"]']) {
      const item = document.querySelector(selector);
      addProfile(item?.getAttribute(selector.startsWith("link") ? "href" : "content"));
    }
    for (const property of ["al:android:url","al:ios:url"]) {
      const value = document.querySelector(`meta[property="${property}"]`)?.getAttribute("content");
      const id = /^fb:\/\/profile\/(\d+)$/.exec(value || "")?.[1];
      if (id) addProfile("https://www.facebook.com/" + id);
    }
  }
  if (resolution.recipient) addProfile(resolution.recipient);
  // A profile is verified by its actual link in a conversation heading, never
  // by a display name alone (different people can share the same name).
  const identifies = area => [...area.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"],header,[role="banner"]')].some(heading => {
    const links = [...heading.querySelectorAll('a[href]')];
    const parent = heading.closest('a[href]');
    if (parent) links.push(parent);
    const identities = [...new Set(links.map(anchor => canonical(anchor.getAttribute("href"))).filter(Boolean))];
    return identities.length === 1 && aliases.has(identities[0]) && label(heading.textContent) === label(targetName);
  });
  const chatSettings = /^(?:chat settings|configuración del chat)$/i;
  const isChatSettings = item => chatSettings.test(label(item.getAttribute("aria-label") || item.textContent)) ||
    [...item.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')].some(heading => label(heading.textContent) === label(targetName));
  const popupRecipient = (area, requireAlias = true) => {
    const headings = [...area.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')].filter(visible);
    if (!headings.some(item => label(item.textContent) === label(targetName))) return null;
    const links = [...area.querySelectorAll('a[href]')].filter(item => visible(item) &&
      label(item.getAttribute("aria-label") || item.textContent) === label(targetName) && (requireAlias ? aliases.has(canonical(item.getAttribute("href"))) :
        /^https:\/\/www\.facebook\.com\/(?:\d+|profile\.php\?id=\d+)$/.test(canonical(item.getAttribute("href")) || "")));
    return [...new Set(links.map(item => canonical(item.getAttribute("href"))))].length === 1 ? canonical(links[0].getAttribute("href")) : null;
  };
  const areas = [...document.querySelectorAll('[role="dialog"],[role="complementary"],[role="group"]')];
  // Some desktop chat panels expose generic containers, not dialog/group roles.
  // Find the smallest container joining the named header and its own composer.
  for (const heading of document.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')) {
    if (!visible(heading) || label(heading.textContent) !== label(targetName)) continue;
    let area = heading.parentElement;
    for (let depth = 0; area && depth < 16; depth++, area = area.parentElement) {
      if (main.includes(area)) break;
      if (area.querySelectorAll('[contenteditable="true"][role="textbox"]').length) { areas.push(area); break; }
    }
  }
  const popupAreas = [...new Set(areas)].filter(item => visible(item) &&
    [...item.querySelectorAll('button,[role="button"]')].some(button => visible(button) && isChatSettings(button)));
  const popups = popupAreas.filter(item => popupRecipient(item) && !popupAreas.some(other => other !== item && item.contains?.(other)));
  const describe = () => {
    const headings = main.flatMap(area => [...area.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')]).filter(visible);
    const message = /^(?:message|send message|mensaje|enviar mensaje)$/i;
    const send = /^(?:send|send message|press enter to send|enviar|enviar mensaje|(?:pulsa|pulsar|presiona|presionar) (?:intro|enter) para enviar)$/i;
    const buttons = main.flatMap(area => [...area.querySelectorAll('button,[role="button"]')]).filter(visible);
    return {page:thread(location.href)?"thread":canonical(location.href)===profile?"profile":"other",mains:main.length,
      headings:headings.length,nameMatches:headings.filter(item=>label(item.textContent)===label(targetName)).length,
      messageButtons:buttons.filter(item=>message.test(label(item.getAttribute("aria-label")||item.textContent))).length,
      messageLinks:main.flatMap(area=>[...area.querySelectorAll('a[href]')]).filter(item=>visible(item)&&thread(item.getAttribute("href"))&&message.test(label(item.getAttribute("aria-label")||item.textContent))).length,
      popups:popupAreas.length,
      identityMatches:main.filter(identifies).length + popups.length,
      editors:main.flatMap(area=>[...area.querySelectorAll('[contenteditable="true"][role="textbox"]')]).filter(visible).length,
      sendButtons:buttons.filter(item=>send.test(label(item.getAttribute("aria-label")||item.textContent))).length,
      profileAliases:aliases.size,
      chatHeadings:[...document.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')].filter(item=>visible(item)&&label(item.textContent)===label(targetName)).length,
      menuItems:document.querySelectorAll('[role="menu"] [role="menuitem"]').length};
  };
  const report = result => ({...result,diagnostic:describe()});
  if (action === "inspect") return report({inspected:true});
  if (action === "verify-profile") {
    const canonicalUrls = [canonical(location.href),canonical(document.querySelector('link[rel="canonical"]')?.getAttribute("href")),
      canonical(document.querySelector('meta[property="og:url"]')?.getAttribute("content"))];
    return report({verified:canonicalUrls.includes(profile) && main.some(area =>
      [...area.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')].some(item => visible(item) && label(item.textContent) === label(targetName)))});
  }
  if (action === "resolve" || action === "open") {
    const current = thread(location.href);
    if (current && (profile || current === thread(targetLink)) && main.some(identifies)) return report({thread:current});
    if (canonical(location.href) !== profile) return report({error:"recipient"});
    const message = /^(?:message|send message|mensaje|enviar mensaje)$/i;
    const headings = main.flatMap(area => [...area.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')])
      .filter(item => visible(item) && label(item.textContent) === label(targetName));
    for (const heading of headings) {
      let area = heading.parentElement;
      for (let depth = 0; area && depth < 8 && !main.includes(area); depth++, area = area.parentElement) {
        const links = [...area.querySelectorAll('a[href]')].filter(item => visible(item) && message.test(label(item.getAttribute("aria-label") || item.textContent)));
        const destinations = [...new Set(links.map(item => thread(item.getAttribute("href"))).filter(Boolean))];
        if (destinations.length === 1) return report({thread:destinations[0]});
        if (action === "open") {
          const buttons = [...area.querySelectorAll('button,[role="button"]')].filter(item => visible(item) &&
            item.getAttribute("aria-disabled") !== "true" && !item.disabled && message.test(label(item.getAttribute("aria-label") || item.textContent)));
          if (buttons.length === 1) { buttons[0].click(); return report({opening:true}); }
        }
      }
    }
    if (action === "open" && main.some(identifies)) {
      const buttons = main.flatMap(area => [...area.querySelectorAll('button,[role="button"]')]).filter(item => visible(item) &&
        item.getAttribute("aria-disabled") !== "true" && !item.disabled && message.test(label(item.getAttribute("aria-label") || item.textContent)));
      if (buttons.length === 1) { buttons[0].click(); return report({opening:true}); }
    }
    // Only a popup whose heading identifies this exact profile may supply a
    // full Messenger thread. Other already-open chats are ignored.
    const legacyPopups = [...document.querySelectorAll('[role="dialog"],[role="complementary"]')].filter(item => visible(item) && identifies(item));
    const candidates = legacyPopups.flatMap(area => [...area.querySelectorAll('a[href]')]).filter(item => visible(item) &&
      /^(?:open in messenger|see all in messenger|see in messenger|abrir en messenger|ver todo en messenger|ver en messenger)$/i.test(label(item.getAttribute("aria-label") || item.textContent)))
      .map(item => thread(item.getAttribute("href"))).filter(Boolean);
    const unique = [...new Set(candidates)];
    if (unique.length === 1) return report({thread:unique[0]});
    if (!popups.length) {
      const candidates = [...new Set(popupAreas.map(area => popupRecipient(area,false)).filter(Boolean))];
      if (candidates.length === 1) return report({verifyProfile:candidates[0]});
    }
    if (popups.length === 1) {
      const recipient = popupRecipient(popups[0]);
      if (!resolution.menuOpened) {
        const buttons = [...popups[0].querySelectorAll('button,[role="button"]')].filter(item => visible(item) && isChatSettings(item));
        if (buttons.length === 1) { buttons[0].click(); return report({openingMenu:true,recipient}); }
      } else if (!resolution.threadOpened) {
        const items = [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].filter(item => visible(item) &&
          /^(?:open in messenger|abrir en messenger)$/i.test(label(item.getAttribute("aria-label") || item.textContent)));
        if (items.length === 1) { items[0].click(); return report({openingThread:true,recipient}); }
      }
    }
    return report({waiting:true});
  }
  if (!thread(threadLink) || thread(location.href) !== thread(threadLink)) return report({error:"recipient"});
  // When a conversation was resolved from a profile, verify that profile in
  // its heading again before both text insertion and the final click.
  if (!main.some(identifies)) return report({error:"recipient"});
  const isComposer = item => {
    const name = label(item.getAttribute("aria-label"));
    const region = item.closest?.('[role="region"],section[aria-label]');
    return ["Message","Mensaje","Type a message","Type a message…","Escribe en " + label(targetName)].includes(name) ||
      /^(?:conversation composer|editor de conversación)$/i.test(label(region?.getAttribute("aria-label")));
  };
  const editors = main.flatMap(area => [...area.querySelectorAll('[contenteditable="true"][role="textbox"]')]).filter(item => visible(item) && isComposer(item));
  const uniqueEditors = [...new Set(editors)];
  if (uniqueEditors.length !== 1) return report({error:"composer"});
  const editor = uniqueEditors[0];
  const value = () => clean(editor.innerText ?? editor.textContent);
  if (action === "ready") return report(value().trim().length ? {error:"draft"} : {ready:true});
  if (action === "confirm") return report({submitted:value().trim().length === 0});
  if (action === "prepare") {
    if (value().trim().length) return report({error:"draft"});
    editor.focus();
    document.execCommand("insertText",false,text);
    // Lexical updates its DOM asynchronously; execCommand's return value is
    // not an acknowledgement. Insert once, then verify exact text and controls
    // in separate read-only calls before allowing the final click.
    return report({prepared:true});
  }
  if (!["send","can-send"].includes(action) || value() !== clean(text)) return report({error:"composer"});
  const send = /^(?:send|send message|press enter to send|enviar|enviar mensaje|(?:pulsa|pulsar|presiona|presionar) (?:intro|enter) para enviar)$/i;
  const buttons = main.flatMap(area => [...area.querySelectorAll('button,[role="button"]')]).filter(item => visible(item) &&
    !item.disabled && item.getAttribute("aria-disabled") !== "true" && send.test(label(item.getAttribute("aria-label") || item.textContent)));
  if (buttons.length !== 1) return report({error:"composer"});
  if (action === "can-send") return report({ready:true});
  buttons[0].click();
  return report({attempted:true});
}

const facebookMessaging = (() => {
  let job = null;
  const safeTarget = value => {
    try {
      const url = new URL(value);
      return url.protocol === "https:" && !url.username && !url.password && !url.port &&
        ["facebook.com","www.facebook.com"].includes(url.hostname) &&
        (/^\/messages\/(?:e2ee\/)?t\/[A-Za-z0-9._-]+\/?$/.test(url.pathname) && !url.search ||
         /^\/[A-Za-z0-9.]+\/?$/.test(url.pathname) && !url.search && !/\/(login|checkpoint|friends|settings|groups|marketplace|watch|stories|notifications|help)\/?$/.test(url.pathname) ||
         url.pathname === "/profile.php" && /^\?id=\d+$/.test(url.search));
    } catch (_) { return false; }
  };
  async function handle(socket, request) {
    if (!request) { job = null; return; }
    if (!/^[a-f0-9]{32}$/.test(request.id) || !safeTarget(request.targetLink) ||
        typeof request.targetName !== "string" || !request.targetName.trim() || request.targetName.length > 200 ||
        typeof request.text !== "string" || !request.text.trim() || request.text.length > 5000) return;
    if (job?.id === request.id) { if (job.result) socket.send(JSON.stringify(job.result)); return; }
    if (job && !job.result) return;
    const current = {id:request.id}; job = current;
    const valid = () => job === current && socket.readyState === WebSocket.OPEN;
    let signature = "", lastDiagnostic = null;
    const progress = (stage, diagnostic = null) => {
      if (!valid()) return;
      if (diagnostic) lastDiagnostic = diagnostic;
      const next = JSON.stringify({stage,diagnostic});
      if (signature === next) return;
      signature = next;
      socket.send(JSON.stringify({kind:"facebook-message-progress",id:request.id,stage,diagnostic}));
    };
    let attempted = false, prepared = false, tab = null, result, attempts;
    try {
      progress("received");
      progress("permission");
      if (!await habitatConnectionEnabled() || !await chrome.permissions.contains({origins:["https://www.facebook.com/*"]})) throw Error("permission");
      attempts = (await chrome.storage.session.get("habitatMessengerAttempts")).habitatMessengerAttempts || {};
      if (attempts[request.id]) result = attempts[request.id] === "inspected" && request.inspectOnly === true ?
        {status:"not-sent",error:"inspected"} : {status:attempts[request.id] === "submitted" ? "submitted" : "uncertain"};
      else if (valid()) {
        // Persist only operation IDs and outcomes, before the first mutation.
        // A worker restart must never submit this operation a second time.
        attempts = Object.fromEntries(Object.entries(attempts).slice(-99));
        attempts[request.id] = "claimed";
        await chrome.storage.session.set({habitatMessengerAttempts:attempts});
        progress("claimed");
        if (!valid()) return;
        progress("opening-profile");
        tab = await chrome.tabs.create({url:request.targetLink,active:false});
        progress("waiting-page");
        const resolution = {};
        const run = async (action, threadLink = null) => {
          progress(action,lastDiagnostic);
          if (!valid() || !await habitatConnectionEnabled() || !await chrome.permissions.contains({origins:["https://www.facebook.com/*"]})) throw Error("permission");
          const currentTab = await chrome.tabs.get(tab.id);
          const actualUrl = currentTab.pendingUrl || currentTab.url;
          if (/^https:\/\/(?:www\.)?facebook\.com\/login/.test(actualUrl)) throw Error("login");
          if (/^https:\/\/(?:www\.)?facebook\.com\/(?:checkpoint|two_step_verification)/.test(actualUrl)) throw Error("blocked");
          if (!safeTarget(actualUrl)) throw Error("recipient");
          const packets = await chrome.scripting.executeScript({target:{tabId:tab.id},func:facebookMessagePage,
            args:[action,request.targetLink,request.targetName,threadLink,request.text,resolution]});
          const page = packets[0]?.result;
          progress(action,page?.diagnostic || null);
          if (!page || page.error) throw Error(page?.error || "unsupported");
          return page;
        };
        let threadLink = null, opened = false;
        const deadline = Date.now() + 18000;
        while (valid() && Date.now() < deadline) {
          const currentTab = await chrome.tabs.get(tab.id);
          if (currentTab.status === "complete") {
            const page = await run(opened ? "resolve" : "open");
            if (page.thread) { threadLink = page.thread; break; }
            if (page.verifyProfile) {
              if (!safeTarget(page.verifyProfile)) throw Error("recipient");
              const verification = await chrome.tabs.create({url:page.verifyProfile,active:false});
              try {
                const until = Date.now() + 8000;
                while (valid() && (await chrome.tabs.get(verification.id)).status !== "complete" && Date.now() < until)
                  await new Promise(resolve => setTimeout(resolve,250));
                if (!valid()) return;
                const actual = await chrome.tabs.get(verification.id);
                if (!safeTarget(actual.pendingUrl || actual.url)) throw Error("recipient");
                const packets = await chrome.scripting.executeScript({target:{tabId:verification.id},func:facebookMessagePage,
                  args:["verify-profile",request.targetLink,request.targetName,null,""]});
                if (!packets[0]?.result?.verified) throw Error("recipient");
                resolution.recipient = page.verifyProfile;
              } finally { await chrome.tabs.remove(verification.id).catch(()=>{}); }
            }
            if (page.opening) {
              opened = true; progress("opening-chat");
              // Facebook defers mounting chat panels while the tab is hidden.
              // Select only this submission's tab; never navigate a personal tab.
              await chrome.tabs.update(tab.id,{active:true});
            }
            if (page.recipient) resolution.recipient = page.recipient;
            if (page.openingMenu) resolution.menuOpened = true;
            if (page.openingThread) resolution.threadOpened = true;
          }
          await new Promise(resolve => setTimeout(resolve,250));
        }
        if (!threadLink || !safeTarget(threadLink)) throw Error("unsupported");
        if (!valid()) return;
        progress("navigating-thread");
        if ((await chrome.tabs.get(tab.id)).url.replace(/\/$/,"") !== threadLink)
          await chrome.tabs.update(tab.id,{url:threadLink}); // This tab was created by this submission.
        const loading = Date.now() + 10000;
        while (valid() && (await chrome.tabs.get(tab.id)).status !== "complete" && Date.now() < loading)
          await new Promise(resolve => setTimeout(resolve,250));
        const readiness = Date.now() + 8000;
        while (valid()) {
          try { await run("ready",threadLink); break; }
          catch (error) {
            if (!["recipient","composer"].includes(error.message) || Date.now() >= readiness) throw error;
            await new Promise(resolve => setTimeout(resolve,250));
          }
        }
        if (request.inspectOnly === true) result = {status:"not-sent",error:"inspected"};
        else {
          prepared = true;
          const insertion = await run("prepare",threadLink);
          if (!insertion.prepared) throw Error("composer");
          if (!valid()) return;
          const controls = Date.now() + 3000;
          while (valid()) {
            try { await run("can-send",threadLink); break; }
            catch (error) {
              if (error.message !== "composer" || Date.now() >= controls) throw error;
              await new Promise(resolve => setTimeout(resolve,100));
            }
          }
          // Mark uncertainty before invoking the final action. Never click twice.
          attempted = true;
          const sent = await run("send",threadLink);
          if (!sent.attempted) { attempted = false; throw Error("composer"); }
          const confirmation = Date.now() + 5000;
          while (valid() && Date.now() < confirmation) {
            if ((await run("confirm",threadLink)).submitted) { result = {status:"submitted"}; break; }
            await new Promise(resolve => setTimeout(resolve,250));
          }
          result ||= {status:"uncertain"};
        }
        attempts[request.id] = request.inspectOnly === true ? "inspected" : result.status;
        await chrome.storage.session.set({habitatMessengerAttempts:attempts});
      }
    } catch (error) {
      progress("failed",lastDiagnostic);
      result = {status:attempted ? "uncertain" : "not-sent",error:
        ["permission","login","blocked","recipient","composer","draft","unsupported"].includes(error.message) ? error.message : "unsupported"};
    } finally {
      // Close only this operation's temporary tab. A draft, verification prompt
      // or uncertain send stays open for review; cleanup never changes the result.
      const confirmed = result?.status === "submitted" || result?.error === "inspected";
      const review = attempted || prepared || ["draft","login","blocked"].includes(result?.error);
      if (tab && (confirmed || !review)) await chrome.tabs.remove(tab.id).catch(()=>{});
    }
    if (valid() && result) {
      current.result = {kind:"facebook-message-result",id:request.id,...result};
      socket.send(JSON.stringify(current.result));
    }
  }
  return {handle};
})();
