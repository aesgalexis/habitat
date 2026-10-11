const key = document.getElementById("key"), msg = document.getElementById("msg");
const normalizeUrl = value => {
  try { const url=new URL(value); return url.origin+url.pathname.replace(/\/$/,""); }
  catch (_) { return ""; }
};
chrome.storage.local.get(["habitatKey","habitatTab","habitatUrl"], s => {
  key.value=s.habitatKey||"";
  if(s.habitatTab) msg.textContent="Pestaña vinculada: "+s.habitatTab;
});
document.getElementById("bind").onclick = async () => {
  const [tab] = await chrome.tabs.query({active:true,currentWindow:true});
  if (!tab?.url?.startsWith("https://chatgpt.com/")) { msg.textContent="Abre primero una conversación en chatgpt.com";return; }
  if (!key.value.trim()) {msg.textContent="Introduce el código de vinculación";return;}
  const habitatUrl=normalizeUrl(tab.url);
  await chrome.storage.local.set({habitatKey:key.value.trim(),habitatTab:tab.id,habitatUrl});
  try {
    // Content scripts declared in the manifest are not added retroactively to
    // tabs that were already open when an unpacked extension was installed.
    await chrome.scripting.executeScript({target:{tabId:tab.id},files:["content.js"]});
    msg.textContent="Conectada a esta conversación. Déjala abierta.";
  } catch (error) {
    msg.textContent="Vinculada, pero recarga la pestaña de ChatGPT una vez.";
  }
};
document.getElementById("unbind").onclick = async () => {
  await chrome.storage.local.remove(["habitatTab","habitatUrl"]); msg.textContent="Desconectada.";
};
