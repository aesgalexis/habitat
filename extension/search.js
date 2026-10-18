// Google web results use their own transport, cache and tab ownership.
function isGoogleSearchPage(value) {
  try { const url = new URL(value); return url.origin === "https://www.google.com" && url.pathname === "/search"; }
  catch (_) { return false; }
}
function googleSearchPageError(value) {
  try {
    const url = new URL(value);
    if (url.origin === "https://consent.google.com") return "consent";
    if (url.origin === "https://www.google.com" && url.pathname.startsWith("/sorry/")) return "blocked";
  } catch (_) {}
  return "page-changed";
}
const googleSearchTransport = createSearchTransport({
  alarm:"habitat-search", endpoint:"search-bridge", tabKey:"habitatSearchTab",
  isPage:isGoogleSearchPage, pageError:googleSearchPageError,
  isOwned:value => isGoogleSearchPage(value) || googleSearchPageError(value) !== "page-changed",
  url:(query, start) => "https://www.google.com/search?q=" + encodeURIComponent(query) + "&udm=14&hl=en&start=" + start,
  read:readGoogleSearchPage
});

// Executed in the isolated content world. Return text and HTTP(S) URLs only.
function readGoogleSearchPage(query, start) {
  const url = new URL(location.href);
  if (url.origin !== "https://www.google.com" || url.pathname !== "/search" ||
      url.searchParams.get("q")?.trim() !== query.trim() || Number(url.searchParams.get("start") || 0) !== start)
    return {error:"page-changed"};
  if (document.querySelector('form[action*="consent.google"], [name="set_eom"]')) return {error:"consent"};
  if (document.querySelector('#captcha, form[action*="/sorry/"], iframe[src*="recaptcha"]')) return {error:"blocked"};
  const root = document.querySelector("#rso");
  const clean = (value, max) => (value || "").replace(/\s+/g, " ").trim().slice(0, max);
  const visible = node => {
    const box = node.getBoundingClientRect();
    return box.width > 0 && box.height > 0 && getComputedStyle(node).visibility !== "hidden";
  };
  const results = [], seen = new Set();
  for (const heading of root?.querySelectorAll("h3") || []) {
    if (!visible(heading) || heading.closest('#tads, #tadsb, [data-text-ad], [data-ad-slot]')) continue;
    const link = heading.closest("a[href]");
    if (!link) continue;
    let target;
    try {
      target = new URL(link.href, location.href);
      if (target.origin === url.origin && target.pathname === "/url")
        target = new URL(target.searchParams.get("q") || target.searchParams.get("url"));
    } catch (_) { continue; }
    if (!["https:","http:"].includes(target.protocol) || target.username || target.password || target.href.length > 8192 ||
        target.hostname === "maps.google.com" || (target.hostname === "www.google.com" &&
          /^\/(search|maps|imgres|aclk|url|preferences|setprefs)(\/|$)/.test(target.pathname))) continue;
    const title = clean(heading.textContent, 600);
    if (!title || seen.has(target.href)) continue;
    let snippet = "", block = link.parentElement;
    while (block && block !== root) {
      const text = block.querySelector('.VwiC3b, [data-sncf], .IsZvec');
      if (text && visible(text)) { snippet = clean(text.textContent, 2000); break; }
      if (block.matches('.MjjYud, .g')) break;
      block = block.parentElement;
    }
    seen.add(target.href);
    results.push({title, url:target.href, snippet});
    if (results.length === 30) break;
  }
  let nextStart = null;
  const next = document.querySelector("a#pnnext");
  if (next) {
    try {
      const nextUrl = new URL(next.href, location.href), offset = Number(nextUrl.searchParams.get("start"));
      if (nextUrl.origin === url.origin && nextUrl.pathname === "/search" &&
          nextUrl.searchParams.get("q")?.trim() === query.trim() && Number.isInteger(offset) && offset > start && offset <= 990)
        nextStart = offset;
    } catch (_) {}
  }
  const empty = /did not match any documents|no results found|there aren't any great matches/i.test(document.querySelector("#topstuff")?.textContent || "");
  return {ready:results.length > 0 || empty, results, nextStart};
}

void googleSearchTransport.start();
