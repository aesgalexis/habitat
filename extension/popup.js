const key = document.getElementById("key"), msg = document.getElementById("msg");
chrome.storage.local.get(["habitatKey","habitatTab"], s => { key.value=s.habitatKey||""; if(s.habitatTab)msg.textContent="Pestaña vinculada: "+s.habitatTab; });
document.getElementById("bind").onclick = async () => {
  const [tab] = await chrome.tabs.query({active:true,currentWindow:true});
  if (!tab?.url?.startsWith("https://chatgpt.com/")) { msg.textContent="Abre primero una conversación en chatgpt.com";return; }
  if (!key.value.trim()) {msg.textContent="Introduce el código de vinculación";return;}
  await chrome.storage.local.set({habitatKey:key.value.trim(),habitatTab:tab.id});
  msg.textContent="Conectada a esta pestaña. Déjala abierta.";
};
document.getElementById("unbind").onclick = async () => {
  await chrome.storage.local.remove(["habitatTab"]); msg.textContent="Desconectada.";
};
