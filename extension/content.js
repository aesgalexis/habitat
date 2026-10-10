// Operates the REAL ChatGPT UI. Select exactly one chat tab in the popup.
// DOM selectors are inherently fragile as ChatGPT's frontend changes.
(() => {
  if (window.__habitatBridge) return;
  window.__habitatBridge = true;
  let busy = false;
  const pause = ms => new Promise(r=>setTimeout(r,ms));
  const bridge = payload => chrome.runtime.sendMessage(payload).catch(e=>({error:String(e)}));
  const event = (type,id,text) => bridge({kind:"event",event:{type,id,text}});
  const assistantNodes = () => [...document.querySelectorAll('[data-message-author-role="assistant"]')];
  function assistantText(node) {
    const main=node.querySelector('.markdown, [class*="markdown"]') || node;
    return main.innerText?.trim() || "";
  }
  async function process(message) {
    busy=true;
    const {id,text}=message;
    try {
      const box=document.querySelector('#prompt-textarea, [contenteditable="true"][data-placeholder], [contenteditable="true"][role="textbox"]');
      if (!box) throw Error("No encuentro el cuadro de ChatGPT. Abre una conversación normal.");
      const prior=assistantNodes();
      const oldLast=prior[prior.length-1];
      const oldText=oldLast ? assistantText(oldLast) : "";
      box.focus();
      // Use browser editing, not element.innerHTML, so React receives input events.
      if (box.isContentEditable) {
        document.execCommand("selectAll",false,null);
        document.execCommand("insertText",false,text);
      } else {
        const setter=Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,"value")?.set;
        setter?.call(box,text);
        box.dispatchEvent(new Event("input",{bubbles:true}));
      }
      await pause(180);
      const send=document.querySelector('button[data-testid="send-button"], button[aria-label="Enviar mensaje"], button[aria-label="Send message"]');
      if (!send || send.disabled) throw Error("No encuentro el botón Enviar; comprueba ChatGPT.");
      send.click();
      await event("started",id,"");
      let stable=0,last="",started=false;
      const deadline=Date.now()+180000;
      while(Date.now()<deadline) {
        await pause(850);
        const nodes=assistantNodes(), node=nodes[nodes.length-1];
        const isNew=node && (node!==oldLast || nodes.length>prior.length || assistantText(node)!==oldText);
        const current=isNew ? assistantText(node) : "";
        if(current){started=true;stable=current===last?stable+1:0;last=current;}
        const stop=document.querySelector('button[data-testid="stop-button"], button[aria-label*="Stop generating"], button[aria-label*="Detener"]');
        if(started && !stop && stable>=3) {
          await event("response",id,last);
          return;
        }
      }
      throw Error("No pude confirmar el final de la respuesta (3 min). Consulta la pestaña de ChatGPT.");
    } catch(e) {
      await event("error",id,String(e.message||e));
    } finally {busy=false;}
  }
  setInterval(async()=>{
    if(busy || document.visibilityState==="prerender") return;
    const s=await chrome.storage.local.get("habitatTab");
    // background independently enforces bound tab identity.
    if(!s.habitatTab)return;
    const answer=await bridge({kind:"next"});
    if(answer?.message)process(answer.message);
  },1200);
})();
