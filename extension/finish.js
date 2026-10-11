(async()=>{
  const prefix=`chrome-extension://${chrome.runtime.id}/`;
  const tabs=await chrome.tabs.query({});
  const ids=tabs.filter(tab=>tab.id && tab.url?.startsWith(prefix)).map(tab=>tab.id);
  if(ids.length)await chrome.tabs.remove(ids);
})();
