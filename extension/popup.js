const key = document.getElementById("key"), msg = document.getElementById("msg");
const normalizeUrl = value => {
  try { const url=new URL(value); return url.origin+url.pathname.replace(/\/$/,""); }
  catch (_) { return ""; }
};
chrome.storage.local.get(["habitatKey","habitatTab","habitatConsentVersion","habitatDisabled"], s => {
  const enabled = s.habitatConsentVersion === 1 && !s.habitatDisabled;
  document.getElementById("automatic").disabled = !enabled;
  document.getElementById("bind").disabled = !enabled;
  key.disabled = !enabled;
  if (enabled) {
    key.value=s.habitatKey||"";
    msg.textContent=s.habitatTab ? "Conversation linked. Keep HABITAT and the ChatGPT tab open." : "Open HABITAT and a ChatGPT conversation in Chrome, then connect.";
  }
});

document.getElementById("settings").onclick = () => chrome.tabs.create({url:chrome.runtime.getURL("welcome.html")});
document.getElementById("automatic").onclick = async () => {
  try {
    const result = await chrome.runtime.sendMessage({kind:"connect"});
    msg.textContent = result?.error || "Connecting. Keep HABITAT and the ChatGPT conversation open.";
  } catch (_) { msg.textContent = "Could not connect. Open HABITAT and try again."; }
};
document.getElementById("bind").onclick = async () => {
  if ((await chrome.storage.local.get("habitatConsentVersion")).habitatConsentVersion !== 1) {
    msg.textContent="Review the connection settings to enable HABITAT."; return;
  }
  const [tab] = await chrome.tabs.query({active:true,currentWindow:true});
  if (!tab?.url?.startsWith("https://chatgpt.com/")) { msg.textContent="Open a conversation on chatgpt.com first";return; }
  if (!key.value.trim()) {msg.textContent="Enter the pairing code";return;}
  const habitatUrl=normalizeUrl(tab.url);
  await chrome.storage.local.set({habitatKey:key.value.trim(),habitatTab:tab.id,habitatUrl,habitatDisabled:false});
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
  await chrome.storage.local.set({habitatDisabled:true});
  document.getElementById("automatic").disabled = true;
  document.getElementById("bind").disabled = true;
  key.disabled = true;
  msg.textContent="Disconnected. Use Connection and privacy to enable it again.";
};
