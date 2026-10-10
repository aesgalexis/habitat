// Cross-origin bridge. Only the selected ChatGPT tab can request it.
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  (async () => {
    const s = await chrome.storage.local.get(["habitatKey","habitatTab"]);
    if (!s.habitatKey || sender.tab?.id !== s.habitatTab) return {error:"Not linked"};
    if (request.kind !== "next" && request.kind !== "event") return {error:"Invalid operation"};
    try {
      const url = "http://127.0.0.1:8765/" + (request.kind === "next" ? "next" : "event");
      const init = {method: request.kind === "next" ? "GET" : "POST",
        headers:{"X-Habitat-Key":s.habitatKey,"Content-Type":"application/json"}};
      if (request.kind === "event") init.body=JSON.stringify(request.event);
      const r=await fetch(url,init);
      return r.ok ? await r.json() : {error:"HTTP "+r.status};
    } catch(e) {return {error:String(e)};}
  })().then(sendResponse);
  return true;
});
