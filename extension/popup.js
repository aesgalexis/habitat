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
const facebook = document.getElementById("facebook");
const facebookReconnect = document.getElementById("facebook-reconnect");
const facebookStatus = document.getElementById("facebook-status");
const facebookOrigins = {origins:["https://www.facebook.com/*", "https://facebook.com/*"]};
let facebookEnabled = false;
let facebookWorkerNeedsReload = false;
async function connectFacebook(restart = false) {
  facebookStatus.textContent = "Connecting Facebook to HABITAT…";
  facebookReconnect.disabled = true;
  try {
    const result = await chrome.runtime.sendMessage({kind:"facebook-connect",restart});
    facebookWorkerNeedsReload = !result || result.facebookProtocol !== 1 && !result.error?.includes("enable the connection");
    facebookReconnect.textContent = facebookWorkerNeedsReload ? "Reload extension for Facebook" : "Reconnect Facebook";
    const readError = {
      permission:"Enable Facebook contacts to allow the reader.",
      login:"Sign in to Facebook in Chrome, then retry its widget.",
      blocked:"Complete Facebook's verification in Chrome, then retry its widget.",
      "page-changed":"Open Facebook All friends in Chrome, then retry its widget.",
      "contacts-unavailable":"The reader cannot find Facebook contacts. Open Friends → All friends in Chrome, wait for the profile photos, then retry its widget.",
      result:"The reader could not load Facebook contacts. Open Facebook Home, then retry its widget."
    };
    facebookStatus.textContent = facebookWorkerNeedsReload ? "The running extension needs reloading to start its Facebook reader." :
      result.error || (!result.connected ? result.issue || "Facebook is not connected to HABITAT yet." :
        result.lastRead?.error ? readError[result.lastRead.error] || readError.result :
        result.lastRead?.contactCount > 0 ? `Facebook is connected to HABITAT. ${result.lastRead.contactCount} contacts received.` :
        result.lastRead?.contactCount === 0 ? "Facebook is connected to HABITAT, but its Contacts panel returned no contacts." :
        "Facebook is connected to HABITAT. Open its widget to load contacts.");
  } catch (_) { facebookStatus.textContent = "Could not connect Facebook. Keep HABITAT open, then reconnect."; }
  finally { facebookReconnect.disabled = false; }
}
chrome.permissions.contains(facebookOrigins).then(enabled => {
  facebookEnabled = enabled;
  facebook.textContent = enabled ? "Disable Facebook contacts" : "Enable Facebook contacts";
  if (enabled) void connectFacebook();
}).catch(() => { facebook.disabled = true; });
facebookReconnect.onclick = () => {
  if (facebookWorkerNeedsReload) { chrome.runtime.reload(); return; }
  void connectFacebook(true);
};
facebook.onclick = async () => {
  try {
    // Chrome requires this permission request to originate in a user gesture.
    if (facebookEnabled) {
      if (await chrome.permissions.remove(facebookOrigins)) {
        facebookEnabled = false;
        facebook.textContent = "Enable Facebook contacts";
        msg.textContent = "Facebook contacts disabled. Existing Facebook tabs remain open.";
      }
      return;
    }
    const granted = await chrome.permissions.request(facebookOrigins);
    facebookEnabled = granted;
    facebook.textContent = granted ? "Disable Facebook contacts" : "Enable Facebook contacts";
    msg.textContent = granted ? "Facebook access enabled. Enable the connection, sign in to Facebook in Chrome, then open Facebook in HABITAT." :
      "Facebook access was not enabled. You can try again here.";
    if (granted) void connectFacebook(true);
  } catch (_) { msg.textContent = "Could not enable Facebook access. Reload the HABITAT extension and try again."; }
};
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
