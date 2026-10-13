const key = document.getElementById("key"), msg = document.getElementById("msg");
const normalizeUrl = value => {
  try { const url=new URL(value); return url.origin+url.pathname.replace(/\/$/,""); }
  catch (_) { return ""; }
};
chrome.storage.local.get(["habitatKey","habitatTab","habitatUrl"], s => {
  key.value=s.habitatKey||"";
  if(s.habitatTab) msg.textContent="Linked tab: "+s.habitatTab;
});
document.getElementById("bind").onclick = async () => {
  const [tab] = await chrome.tabs.query({active:true,currentWindow:true});
  if (!tab?.url?.startsWith("https://chatgpt.com/")) { msg.textContent="Open a conversation on chatgpt.com first";return; }
  if (!key.value.trim()) {msg.textContent="Enter the pairing code";return;}
  const habitatUrl=normalizeUrl(tab.url);
  await chrome.storage.local.set({habitatKey:key.value.trim(),habitatTab:tab.id,habitatUrl});
  try {
    // Content scripts declared in the manifest are not added retroactively to
    // tabs that were already open when an unpacked extension was installed.
    await chrome.scripting.executeScript({target:{tabId:tab.id},files:["content.js"]});
    msg.textContent="Connected to this conversation. Keep it open.";
  } catch (error) {
    msg.textContent="Linked, but reload the ChatGPT tab once.";
  }
};
document.getElementById("unbind").onclick = async () => {
  await chrome.storage.local.remove(["habitatTab","habitatUrl"]); msg.textContent="Disconnected.";
};
