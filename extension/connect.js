const statusNode=document.getElementById("status");
const normalizeUrl=value=>{
  const url=new URL(value);
  return url.origin+url.pathname.replace(/\/$/,"");
};
const compareChatTabs=(a,b)=>{
  const conversation=value=>/\/c\//.test(new URL(value).pathname)?1:0;
  return conversation(b.url)-conversation(a.url) ||
    Number(b.active)-Number(a.active) ||
    (b.lastAccessed||0)-(a.lastAccessed||0);
};

(async()=>{
  try {
    const tabs=await chrome.tabs.query({url:"https://chatgpt.com/*"});
    const candidates=tabs.filter(tab=>tab.id && tab.url);
    if(!candidates.length)throw Error("No hay ninguna pestaña de chatgpt.com abierta");
    candidates.sort(compareChatTabs);
    const tab=candidates[0];
    const response=await fetch("http://127.0.0.1:8765/pair",{
      headers:{"X-Habitat-Pair":"1"},cache:"no-store"
    });
    if(!response.ok)throw Error("El servidor local respondió HTTP "+response.status);
    const data=await response.json();
    if(!data?.key)throw Error("La app local no devolvió una clave");
    await chrome.storage.local.set({
      habitatKey:data.key,
      habitatTab:tab.id,
      habitatUrl:normalizeUrl(tab.url)
    });
    await chrome.scripting.executeScript({target:{tabId:tab.id},files:["content.js"]});
    document.title="HABITAT conectado";
    statusNode.textContent="HABITAT está conectado. Puedes cerrar esta pestaña.";
  } catch(error) {
    const message=String(error?.message||error);
    document.title="Error HABITAT";
    statusNode.textContent="No se pudo conectar: "+message;
  }
})();
