const statusNode = document.getElementById("status");
(async () => {
  try {
    const result = await chrome.runtime.sendMessage({kind:"connect"});
    if (result?.error) throw Error(result.error);
    document.title = "HABITAT connected";
    statusNode.textContent = "HABITAT is connecting to the linked conversation. Your browser can remain in the background. You can close this tab.";
  } catch (error) {
    document.title = "Error HABITAT";
    statusNode.textContent = "Could not connect: " + String(error?.message || error);
  }
})();
