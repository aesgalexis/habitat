const enable = document.getElementById("enable");
const disable = document.getElementById("disable");
const status = document.getElementById("status");
async function showConnectionState() {
  const saved = await chrome.storage.local.get(["habitatConsentVersion", "habitatDisabled"]);
  const enabled = saved.habitatConsentVersion === 1 && !saved.habitatDisabled;
  enable.textContent = enabled ? "Connection enabled" : "Enable connection";
  enable.disabled = enabled;
  status.textContent = enabled ? "Connection enabled. Open HABITAT; for ChatGPT, open the conversation you want to use in Chrome." :
    "The extension stays disconnected until you enable it.";
}
enable.addEventListener("click", async () => {
  enable.disabled = true;
  try {
    await chrome.storage.local.set({habitatConsentVersion:1, habitatDisabled:false});
    await showConnectionState();
  } catch (_) { enable.disabled = false; status.textContent = "Could not save your choice. Try again."; }
});
disable.addEventListener("click", async () => {
  try {
    await chrome.storage.local.set({habitatDisabled:true});
    await showConnectionState();
    status.textContent = "Disconnected. You can enable the connection here whenever you are ready.";
  } catch (_) { status.textContent = "Could not save your choice. Try again."; }
});
void showConnectionState().catch(() => {
  enable.disabled = true;
  disable.disabled = true;
  status.textContent = "Open this page from the installed HABITAT extension to change your connection settings.";
});
