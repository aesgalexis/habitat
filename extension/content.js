// Operates the REAL ChatGPT UI. Select exactly one chat tab in the popup.
// DOM selectors are inherently fragile as ChatGPT's frontend changes.
(() => {
  let stopped = false;
  const previous = window.__habitatBridge;
  try { if (previous?.protocol === 6 && previous.alive()) return; }
  catch (_) { /* The previous extension context was invalidated by reload. */ }
  if (previous && typeof previous.stop === "function") {
    try { previous.stop(); } catch (_) {}
  }
  const controller = {
    protocol: 6,
    alive() { try { return !stopped && Boolean(chrome.runtime?.id); } catch (_) { return false; } },
    stop() {
      stopped = true;
      chrome.runtime.onMessage.removeListener(onCommand);
    }
  };
  window.__habitatBridge = controller;
  let busy = false;
  const handledMessages = new Set();
  const bridge = async payload => {
    try { return await chrome.runtime.sendMessage(payload); }
    catch(e) { return {error:String(e)}; }
  };
  const pause = async ms => {
    if (stopped) throw Error("The ChatGPT bridge was replaced");
    const answer = await bridge({kind:"delay",ms});
    if (stopped || answer?.error) throw Error(answer?.error || "The ChatGPT bridge was replaced");
  };
  async function event(type,id,text,model="") {
    let lastError="";
    for(let attempt=0;attempt<5;attempt++) {
      const answer=await bridge({kind:"event",event:{type,id,text,model}});
      if(!answer?.error)return;
      lastError=answer.error;
      await pause(400*(attempt+1));
    }
    throw Error("Could not deliver the event to HABITAT: "+lastError);
  }
  const assistantNodes = () => {
    const current=[...document.querySelectorAll('[data-content-search-unit-key$=":assistant"]')];
    if(current.length)return current;
    const messageBodies=[...document.querySelectorAll('[data-markdown-text-style="assistant-message"]')];
    if(messageBodies.length)return messageBodies;
    return [...document.querySelectorAll('[data-message-author-role="assistant"]')];
  };
  function assistantText(node) {
    const main=node.matches('[data-markdown-text-style="assistant-message"]')
      ? node
      : node.querySelector('[data-markdown-text-style="assistant-message"], [data-markdown-copy="contents"], .markdown, [class*="markdown"]') || node;
    return main.innerText?.trim() || "";
  }
  function assistantIdentity(node) {
    return node.getAttribute("data-content-search-unit-key") ||
      node.getAttribute("data-chatgpt-search-unit-key") ||
      node.getAttribute("data-chatgpt-selection-message-id") ||
      node.closest('[data-chatgpt-selection-message-id]')?.getAttribute("data-chatgpt-selection-message-id") ||
      node.querySelector('[data-chatgpt-selection-message-id]')?.getAttribute("data-chatgpt-selection-message-id") ||
      "";
  }
  function assistantTurnState(node) {
    const turn=node?.closest('[data-turn-key]');
    const stateNode=turn?.matches('[data-talvt-turn-state]')
      ? turn
      : turn?.querySelector('[data-talvt-turn-state]');
    return stateNode?.getAttribute("data-talvt-turn-state") || "";
  }
  function generationStopButton() {
    return document.querySelector([
      'button[data-testid="stop-button"]',
      'button[data-testid="composer-stop-button"]',
      'button[data-testid*="stop"]',
      'button[aria-label*="Stop generating"]',
      'button[aria-label*="Stop streaming"]',
      'button[aria-label*="Detener"]',
      'button[aria-label*="Interrumpir"]'
    ].join(", "));
  }
  const userNodes = () => {
    const current=[...document.querySelectorAll('[data-content-search-unit-key$=":user"]')];
    if(current.length)return current;
    return [...document.querySelectorAll('[data-message-author-role="user"]')];
  };
  function composerText(box) {
    const value=box instanceof HTMLTextAreaElement ? box.value : box.innerText || box.textContent || "";
    return value.replace(/[\u200B-\u200D\uFEFF]/g, "").trim();
  }
  function visible(element) {
    if(!element)return false;
    const style=getComputedStyle(element), rect=element.getBoundingClientRect();
    return style.display!=="none" && style.visibility!=="hidden" && rect.width>0 && rect.height>0;
  }
  function normalizeModelLabel(value) {
    const label=String(value||"")
      .replace(/^(select|choose|seleccionar|elegir)\s+(a\s+)?(chatgpt\s+)?(model|modelo)\s*[:·-]?\s*/i,"")
      .replace(/\s+/g," ").trim();
    if(!label || /^(chatgpt|model|modelo|modelo de chatgpt)$/i.test(label))return "";

    const family=label.match(/\b(sol|astra|luna|terra)\b/i);
    if(family) {
      const version=label.match(/\b\d+(?:\.\d+)+\b/)?.[0] || "";
      const name=family[1][0].toUpperCase()+family[1].slice(1).toLowerCase();
      return `OpenAI ${name}${version?" "+version:""}`;
    }

    const technical=label.match(/\b(?:gpt[- ]?\d+(?:\.\d+)*(?:[- ]?[a-z]+)?|o\d+(?:[- ]?[a-z]+)?)\b/i);
    if(technical) {
      const name=technical[0].replace(/\s+/g,"-").replace(/^gpt/i,"GPT");
      return `OpenAI ${name}`;
    }
    return "";
  }
  function currentEffortLabel() {
    const selectors=[
      '[data-testid*="reasoning"]',
      '[data-testid*="thinking"]',
      'button[aria-label*="reasoning" i]',
      'button[aria-label*="thinking" i]',
      'button[aria-label*="effort" i]',
      'button[aria-label*="razonamiento" i]'
    ];
    for(const selector of selectors) {
      for(const element of document.querySelectorAll(selector)) {
        if(!visible(element) || isInsideMessage(element))continue;
        const value=[element.innerText,element.getAttribute("aria-label"),element.getAttribute("title")]
          .filter(Boolean).join(" ");
        const match=value.match(/\b(minimal|low|medium|high|xhigh|max|ultra|bajo|medio|alto)\b/i);
        if(!match)continue;
        const normalized={bajo:"low",medio:"medium",alto:"high"}[match[1].toLowerCase()] || match[1].toLowerCase();
        return normalized;
      }
    }
    return "";
  }
  function withEffort(model) {
    const effort=currentEffortLabel();
    return effort && !new RegExp(`\\b${effort}\\b`,"i").test(model) ? `${model} ${effort}` : model;
  }
  function currentModelLabel() {
    const selectors=[
      '[data-testid="model-switcher-dropdown-button"]',
      '[data-testid*="model-picker"]',
      '[data-testid*="model-switcher"]',
      'header button[aria-label*="model" i]',
      'header button[aria-label*="modelo" i]',
      'main button[aria-label*="model" i]',
      'main button[aria-label*="modelo" i]'
    ];
    for(const selector of selectors) {
      for(const element of document.querySelectorAll(selector)) {
        if(!visible(element) || isInsideMessage(element))continue;
        const values=[
          element.innerText,
          element.getAttribute("data-value"),
          element.getAttribute("title"),
          element.getAttribute("aria-label")
        ];
        for(const value of values) {
          const label=normalizeModelLabel(value);
        if(label) {
            return withEffort(label);
          }
        }
      }
    }
    return withEffort("OpenAI model");
  }
  function enabled(element) {
    return element && !element.disabled && element.getAttribute("aria-disabled")!=="true";
  }
  function isInsideMessage(element) {
    return Boolean(element.closest([
      '[data-message-author-role]',
      '[data-content-search-unit-key$=":assistant"]',
      '[data-content-search-unit-key$=":user"]',
      '[data-testid^="conversation-turn-"]'
    ].join(", ")));
  }
  function findComposerBox() {
    // Query each selector separately. querySelector("a, b, c") returns the
    // first node in document order, so a textbox rendered inside an older
    // assistant message could otherwise beat ChatGPT's real prompt box.
    const selectors = [
      '#prompt-textarea',
      '[data-type="unified-composer"] [data-testid="prompt-textarea"]',
      '[data-type="unified-composer"] [contenteditable="true"][role="textbox"]',
      '[data-type="unified-composer"] [contenteditable="true"]',
      '[data-testid="prompt-textarea"]',
      'main form [data-testid="prompt-textarea"]',
      'main form [contenteditable="true"][role="textbox"]'
    ];
    for(const selector of selectors) {
      const match=[...document.querySelectorAll(selector)].find(element =>
        visible(element) && !isInsideMessage(element));
      if(match)return match;
    }
    // Some ChatGPT layouts omit the historical id or the form wrapper.
    // Only accept a unique editable control outside messages and dialogs.
    const candidates=[...document.querySelectorAll('main textarea, main [contenteditable="true"], main [role="textbox"]')]
      .filter(element => visible(element) && enabled(element) && !element.readOnly &&
        !isInsideMessage(element) && !element.closest('[role="dialog"], [role="menu"]'));
    if(candidates.length===1)return candidates[0];
    return null;
  }
  function composerIssue() {
    if(document.readyState==='loading')return 'ChatGPT is still loading.';
    const editors=[...document.querySelectorAll('textarea, [contenteditable="true"], [role="textbox"]')];
    const shown=editors.filter(visible).length;
    const dialogs=[...document.querySelectorAll('[role="dialog"]')].filter(visible);
    const heading=dialogs[0]?.querySelector('h1, h2, [role="heading"]')?.innerText?.trim() || dialogs[0]?.getAttribute('aria-label') || '';
    const login=[...document.querySelectorAll('a[href]')].some(element => visible(element) && /\/(auth|login|log-in|signin)(\/|$)/i.test(element.getAttribute('href')||''));
    const control=editors[0];
    const description=control ? `${control.tagName.toLowerCase()}${control.id?'#'+control.id:''}` : 'none';
    return `ChatGPT editor missing: ${description}, ${shown} visible, ${dialogs.length} dialogs${heading?' ('+heading.slice(0,60)+')':''}${login?', sign-in shown':''}.`;
  }
  function closeImagePreview() {
    // Recover only the image viewer, never consent, login or other dialogs.
    const dialog=[...document.querySelectorAll('[role="dialog"]')].find(element => {
      if(!visible(element))return false;
      const heading=element.querySelector('h1, h2, [role="heading"]')?.innerText?.trim() || element.getAttribute('aria-label') || '';
      return /^(vista previa de imagen|image preview)$/i.test(heading);
    });
    if(!dialog)return;
    const close=[...dialog.querySelectorAll('button')].find(button =>
      /^(close|cerrar)(\b|$)/i.test(button.getAttribute('aria-label') || button.innerText?.trim() || ''));
    if(close)close.click();
    else dialog.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',code:'Escape',bubbles:true,cancelable:true}));
  }
  function composerRoot(box) {
    return box.closest('[data-type="unified-composer"]') ||
      box.closest("form") ||
      box.closest('[data-testid*="composer"]') ||
      box.parentElement;
  }
  function findSendButton(box) {
    const selectors = [
      '[data-testid="send-button"]',
      '[data-testid="composer-submit-button"]',
      '[data-testid*="send"]',
      '#composer-submit-button',
      'button[aria-label^="Enviar"]',
      'button[aria-label^="Send"]',
      '[role="button"][aria-label^="Enviar"]',
      '[role="button"][aria-label^="Send"]'
    ];
    const root=composerRoot(box);
    for (const selector of selectors) {
      const button=root?.querySelector(selector);
      if(button && visible(button))return button;
    }

    // The submit selector must stay inside the composer. A global
    // [type=submit] can select an unrelated control elsewhere on ChatGPT.
    const submit=root?.querySelector('button[type="submit"], input[type="submit"], [role="button"][type="submit"]');
    if(submit && visible(submit))return submit;
    if(!root)return null;
    return [...root.querySelectorAll('button, [role="button"]')].find(button => {
      const name=[
        button.getAttribute("aria-label"),
        button.getAttribute("data-testid"),
        button.getAttribute("title"),
        button.innerText
      ].filter(Boolean).join(" ");
      return visible(button) && /(^|\b)(send|enviar|submit)(\b|$)/i.test(name) &&
        !/(stop|detener|cancel)/i.test(name);
    }) || null;
  }
  async function confirmSent(box, priorUserCount, originalUrl) {
    const deadline=Date.now()+5000;
    while(Date.now()<deadline) {
      await pause(100);
      if(!box.isConnected || composerText(box)==="" || userNodes().length>priorUserCount || location.href!==originalUrl) {
        return true;
      }
    }
    return false;
  }
  async function submitComposer(box) {
    const priorUserCount=userNodes().length;
    const originalUrl=location.href;
    let send=null;
    const buttonDeadline=Date.now()+5000;
    while(Date.now()<buttonDeadline) {
      send=findSendButton(box);
      if(send && enabled(send))break;
      await pause(100);
    }

    if(send && enabled(send)) {
      send.click();
      if(await confirmSent(box,priorUserCount,originalUrl))return;
    }

    // A new, empty chat does not always expose a stable send button. ChatGPT's
    // composer still handles Enter, which is the same action a user performs.
    for(const type of ["keydown","keypress","keyup"]) {
      box.dispatchEvent(new KeyboardEvent(type,{
        key:"Enter",code:"Enter",keyCode:13,which:13,bubbles:true,cancelable:true
      }));
    }
    if(await confirmSent(box,priorUserCount,originalUrl))return;

    const detail=send
      ? `${send.tagName.toLowerCase()}${send.id?"#"+send.id:""} (${send.getAttribute("aria-label")||send.getAttribute("data-testid")||"unlabelled"})`
      : "no send control in the composer";
    throw Error("ChatGPT received the text but did not accept the send action. Detected: "+detail+".");
  }
  async function process(message) {
    busy=true;
    handledMessages.add(message.id);
    if (handledMessages.size > 64) handledMessages.delete(handledMessages.values().next().value);
    const {id,text}=message;
    let model="OpenAI model";
    try {
      const box=findComposerBox();
      if (!box) throw Error("Could not find the ChatGPT composer. Open a regular conversation.");
      model=currentModelLabel();
      const prior=assistantNodes();
      const oldLast=prior[prior.length-1];
      const oldIdentity=oldLast ? assistantIdentity(oldLast) : "";
      const oldText=oldLast ? assistantText(oldLast) : "";
      box.focus();
      // Use browser editing, not element.innerHTML, so React receives input events.
      if (box.isContentEditable) {
        document.execCommand("selectAll",false,null);
        const inserted=document.execCommand("insertText",false,text);
        if(!inserted) {
          box.textContent=text;
          box.dispatchEvent(new InputEvent("input",{bubbles:true,inputType:"insertText",data:text}));
        }
      } else {
        const setter=Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,"value")?.set;
        setter?.call(box,text);
        box.dispatchEvent(new Event("input",{bubbles:true}));
      }
      // Confirm delivery to the desktop before interacting with the button.
      // This also prevents HABITAT from remaining indefinitely in
      // "Enviando a Chrome" when the page later changes context.
      await event("received",id,"",model);
      await submitComposer(box);
      await event("started",id,"",model);
      let stable=0,last="",started=false;
      const deadline=Date.now()+180000;
      while(Date.now()<deadline) {
        await pause(850);
        const nodes=assistantNodes(), node=nodes[nodes.length-1];
        const identity=node ? assistantIdentity(node) : "";
        const textNow=node ? assistantText(node) : "";
        const structuralChange=identity
          ? identity!==oldIdentity
          : !oldIdentity && (node!==oldLast || nodes.length>prior.length);
        const isNew=node && (structuralChange || textNow!==oldText);
        const current=isNew ? textNow : "";
        if(current){started=true;stable=current===last?stable+1:0;last=current;}
        const stop=generationStopButton();
        const turnState=assistantTurnState(node);
        const complete=/^(complete|completed|finished|done)$/i.test(turnState);
        const settled=complete ? stable>=1 : stable>=5;
        if(started && !stop && settled) {
          await event("response",id,last,model);
          return;
        }
      }
      throw Error("Could not confirm the end of the response within 3 minutes. Check the ChatGPT tab.");
    } catch(e) {
      try { await event("error",id,String(e.message||e),model); } catch (_) {}
    } finally {busy=false;}
  }
  function onCommand(request, sender, sendResponse) {
    if (sender.id !== chrome.runtime.id || stopped) return;
    if (request.kind === "habitat:probe") {
      if(!findComposerBox())closeImagePreview();
      const box=findComposerBox();
      sendResponse({protocol:6,userCount:userNodes().length,ready:busy || Boolean(box),busy:busy || Boolean(generationStopButton()),hasDraft:Boolean(box && composerText(box)),model:currentModelLabel(),url:location.href,
        issue:box ? '' : composerIssue()});
    } else if (request.kind === "habitat:deliver") {
      const normalized = location.origin + location.pathname.replace(/\/$/, "");
      if (request.expectedUrl !== normalized || typeof request.message?.id !== "string" || !request.message.id || typeof request.message.text !== "string") {
        sendResponse({error:"The linked conversation changed"});
        return;
      }
      if (busy || handledMessages.has(request.message.id)) {
        sendResponse({accepted:false,busy});
        return;
      }
      // Acknowledge receipt of the extension command immediately. Actual
      // delivery and generation events are still sent separately to HABITAT.
      void process(request.message);
      sendResponse({accepted:true});
    }
  }
  chrome.runtime.onMessage.addListener(onCommand);
  window.addEventListener("pagehide",()=>controller.stop(),{once:true});
  void bridge({kind:"hello"});
})();
