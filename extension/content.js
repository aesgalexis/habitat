// Operates the REAL ChatGPT UI. Select exactly one chat tab in the popup.
// DOM selectors are inherently fragile as ChatGPT's frontend changes.
(() => {
  let stopped = false;
  let pollTimer = null;
  const previous = window.__habitatBridge;
  if (previous && typeof previous.stop === "function") previous.stop();
  const controller = {
    stop() {
      stopped = true;
      if (pollTimer !== null) clearInterval(pollTimer);
    }
  };
  window.__habitatBridge = controller;
  let busy = false;
  const pause = ms => new Promise(r=>setTimeout(r,ms));
  const bridge = payload => chrome.runtime.sendMessage(payload).catch(e=>({error:String(e)}));
  async function event(type,id,text) {
    let lastError="";
    for(let attempt=0;attempt<5;attempt++) {
      const answer=await bridge({kind:"event",event:{type,id,text}});
      if(!answer?.error)return;
      lastError=answer.error;
      await pause(400*(attempt+1));
    }
    throw Error("No se pudo entregar el evento a HABITAT: "+lastError);
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
  function findSendButton(box) {
    const selectors = [
      '[data-testid="send-button"]',
      '[data-testid="composer-submit-button"]',
      '[data-testid*="send"]',
      '#composer-submit-button',
      '[type="submit"]',
      'button[aria-label^="Enviar"]',
      'button[aria-label^="Send"]',
      '[role="button"][aria-label^="Enviar"]',
      '[role="button"][aria-label^="Send"]'
    ];
    for (const selector of selectors) {
      const button=document.querySelector(selector);
      if(button && visible(button))return button;
    }

    // ChatGPT currently exposes the send control as the composer's submit
    // button. Keep this scoped to the same form so another page button cannot
    // be mistaken for Send when labels or test ids change again.
    const form=box.closest("form");
    if(form) {
      const submit=form.querySelector('[type="submit"]');
      if(submit && visible(submit))return submit;
    }

    const composer=box.closest('[data-type="unified-composer"]') || form;
    if(!composer)return null;
    return [...composer.querySelectorAll("button")].find(button => {
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
    const deadline=Date.now()+2500;
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
    const form=box.closest("form");
    let send=null;
    const buttonDeadline=Date.now()+1200;
    while(Date.now()<buttonDeadline) {
      send=findSendButton(box);
      if(send && !send.disabled)break;
      await pause(100);
    }

    if(send && !send.disabled) {
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

    // Final route for frontend variants that only register the form submit.
    if(form) {
      if(typeof form.requestSubmit==="function")form.requestSubmit();
      else form.dispatchEvent(new Event("submit",{bubbles:true,cancelable:true}));
      if(await confirmSent(box,priorUserCount,originalUrl))return;
    }

    throw Error("ChatGPT recibió el texto, pero no aceptó el envío. Recarga esa pestaña una vez.");
  }
  async function process(message) {
    busy=true;
    const {id,text}=message;
    try {
      const box=document.querySelector('#prompt-textarea, textarea[data-testid="prompt-textarea"], main [contenteditable="true"][role="textbox"]');
      if (!box) throw Error("No encuentro el cuadro de ChatGPT. Abre una conversación normal.");
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
      await submitComposer(box);
      await event("started",id,"");
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
          await event("response",id,last);
          return;
        }
      }
      throw Error("No pude confirmar el final de la respuesta (3 min). Consulta la pestaña de ChatGPT.");
    } catch(e) {
      try { await event("error",id,String(e.message||e)); } catch (_) {}
    } finally {busy=false;}
  }
  async function poll() {
    if(stopped || busy || document.visibilityState==="prerender") return;
    try {
      // The background validates and, for the active ChatGPT tab, establishes
      // or refreshes the loopback pairing before returning a message.
      const answer=await bridge({kind:"next"});
      if(answer?.message)await process(answer.message);
    } catch (_) {
      // Reloading/updating an unpacked extension invalidates existing content
      // script contexts. Stop this stale poller; a reinjection or page reload
      // creates the replacement instance.
      controller.stop();
    }
  }
  pollTimer=setInterval(poll,1200);
  window.addEventListener("pagehide",()=>controller.stop(),{once:true});
  void poll();
})();
