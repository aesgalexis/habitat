const provider = new URL(location.href).searchParams.get("source");
const enable = document.getElementById("enable"), status = document.getElementById("status");
const origins = provider === "facebook" ? ["https://www.facebook.com/*","https://facebook.com/*"] :
  provider === "google" ? ["https://mail.google.com/*","https://contacts.google.com/*"] : null;
document.getElementById("title").textContent = provider === "facebook" ? "Facebook contacts" : "Google browser suggestions";
document.getElementById("description").textContent = provider === "facebook" ?
  "Read the people loaded in Facebook Contacts or All friends when Facebook is open. No messages are read or sent." :
  "Suggest names and addresses loaded in Gmail or Google Contacts. Review and accept suggestions in HABITAT before adding contacts. Mail bodies, subjects and snippets are not collected. For your complete saved address book, use Connect Google account in HABITAT.";
enable.disabled = !origins;
enable.onclick = async () => {
  try {
    // Chrome requires a user gesture for its optional site permission prompt.
    const granted = await chrome.permissions.request({origins});
    status.textContent = granted ? (provider === "facebook" ? "Access enabled. Keep HABITAT and the provider tab open; contacts update automatically." : "Access enabled. Suggestions appear automatically in HABITAT for your review; no contacts are added until accepted.") : "Access was not enabled. You can try again here.";
    if (granted) {
      enable.disabled = true;
      await chrome.runtime.sendMessage({kind:"facebook-connect",restart:true});
    }
  } catch (_) { status.textContent = "Could not enable access. Reload the HABITAT extension and try again."; }
};
