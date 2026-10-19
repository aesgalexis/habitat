// Read matching channels and video results from the user's real YouTube search page.
function isYouTubeSearchPage(value) {
  try { const url = new URL(value); return url.origin === "https://www.youtube.com" && url.pathname === "/results"; }
  catch (_) { return false; }
}

function youtubeSearchPageError(value) {
  try {
    const url = new URL(value);
    if (url.origin === "https://consent.youtube.com" || url.origin === "https://consent.google.com") return "consent";
    if (url.origin === "https://www.youtube.com" && url.pathname.startsWith("/sorry/")) return "blocked";
  } catch (_) {}
  return "page-changed";
}

const youtubeSearchTransport = createSearchTransport({
  alarm:"habitat-youtube", endpoint:"youtube-bridge", tabKey:"habitatYouTubeTab", reusePages:true, keepActive:true,
  isPage:isYouTubeSearchPage, pageError:youtubeSearchPageError,
  isOwned:value => isYouTubeSearchPage(value) || youtubeSearchPageError(value) !== "page-changed",
  samePage:(left, right) => isYouTubeSearchPage(left) && isYouTubeSearchPage(right) &&
    new URL(left).searchParams.get("search_query") === new URL(right).searchParams.get("search_query"),
  url:query => "https://www.youtube.com/results?search_query=" + encodeURIComponent(query) + "&hl=en",
  read:readYouTubeSearchPage
});

// Serialized and executed by Chrome in the search tab's isolated content world.
// Only visible page text and canonical channel/video URLs leave the page.
function readYouTubeSearchPage(query, start) {
  const url = new URL(location.href);
  const clean = (value, max = 2000) => (value || "").replace(/\s+/g, " ").trim().slice(0, max);
  if (url.origin !== "https://www.youtube.com" || url.pathname !== "/results" ||
      clean(url.searchParams.get("search_query")) !== clean(query)) return {error:"page-changed"};
  if (document.querySelector('ytd-consent-bump-v2-lightbox, form[action*="consent.youtube"], form[action*="consent.google"]'))
    return {error:"consent"};
  if (document.querySelector('#captcha, iframe[src*="recaptcha"], form[action*="/sorry/"]')) return {error:"blocked"};
  const root = document.querySelector("ytd-search");
  if (!root) return {ready:false, stage:"search-root-missing:" + document.readyState};
  const visible = node => {
    if (!node) return false;
    const bounds = node.getBoundingClientRect();
    return bounds.width > 0 && bounds.height > 0 && getComputedStyle(node).visibility !== "hidden";
  };
  const normalize = value => clean(value).normalize("NFKC").toLowerCase();
  const searchTerm = normalize(query), handleTerm = searchTerm.replace(/^@/, "");
  const channelUrl = value => {
    try {
      const target = new URL(value, location.href);
      const path = target.pathname.replace(/\/$/, "");
      if (target.origin === url.origin && !target.username && !target.password &&
          /^\/(?:@[\p{L}\p{M}\p{N}._·-]+|channel\/UC[A-Za-z0-9_-]{22}|(?:c|user)\/[\p{L}\p{M}\p{N}._·-]+)$/u.test(decodeURIComponent(path)))
        return target.origin + path;
    } catch (_) {}
    return null;
  };
  const channels = [], seenChannels = new Set();
  for (const row of root.querySelectorAll("ytd-channel-renderer, yt-lockup-view-model")) {
    if (!visible(row) || row.closest('ytd-ad-slot-renderer, ytd-promoted-sparkles-web-renderer, ytd-display-ad-renderer')) continue;
    const link = row.querySelector('a#main-link[href], a.yt-lockup-metadata-view-model__title[href], h3 a[href]');
    if (!visible(link)) continue;
    const target = channelUrl(link.href);
    if (!target || seenChannels.has(target)) continue;
    const titleNode = row.querySelector('#channel-title, ytd-channel-name #text');
    const title = clean(visible(titleNode) ? titleNode.textContent : link.textContent, 600);
    const metadata = [...row.querySelectorAll('#subscribers, #video-count, .yt-content-metadata-view-model__metadata-row')]
      .filter(visible).map(node => clean(node.textContent)).filter(Boolean);
    const path = decodeURIComponent(new URL(target).pathname);
    const handles = [path.startsWith('/@') ? path.slice(2) : "", ...metadata.filter(value => value.startsWith('@'))];
    if (!title || normalize(title) !== searchTerm &&
        !handles.some(handle => handle && normalize(handle).replace(/^@/, "") === handleTerm)) continue;
    const description = row.querySelector('#description, #description-text, .metadata-snippet-text');
    const snippet = [...new Set(metadata)].join(' · ') + (visible(description) ? "\n" + clean(description.textContent) : "");
    channels.push({title, url:target, snippet:snippet.trim().slice(0, 2000)});
    seenChannels.add(target);
  }
  const results = [], seen = new Set();
  const rows = root.querySelectorAll("ytd-video-renderer, yt-lockup-view-model");
  for (const row of rows) {
    if (!visible(row) || row.closest('ytd-ad-slot-renderer, ytd-promoted-sparkles-web-renderer, ytd-display-ad-renderer')) continue;
    const link = row.querySelector('a#video-title[href], a.yt-lockup-metadata-view-model__title[href], h3 a[href]');
    if (!visible(link)) continue;
    let target;
    try { target = new URL(link.href, location.href); } catch (_) { continue; }
    const id = target.searchParams.get("v");
    if (target.origin !== url.origin || target.pathname !== "/watch" || !/^[A-Za-z0-9_-]{11}$/.test(id || "")) continue;
    const title = clean(link.textContent, 600);
    if (!title || seen.has(id)) continue;
    const text = selector => {
      const node = row.querySelector(selector);
      return visible(node) ? clean(node.textContent) : "";
    };
    const channel = text("#channel-name a, .yt-content-metadata-view-model__metadata-row a");
    const metadata = [...row.querySelectorAll("#metadata-line span, .yt-content-metadata-view-model__metadata-row")].filter(visible).map(node => clean(node.textContent)).filter(value => value && value !== channel);
    const duration = text("ytd-thumbnail-overlay-time-status-renderer #text, .yt-badge-shape__text");
    const description = text(".metadata-snippet-text, #description-text");
    const details = [channel, duration, ...metadata].filter(Boolean).join(" · ");
    const snippet = [details, description].filter(Boolean).join("\n").slice(0, 2000);
    results.push({title, url:"https://www.youtube.com/watch?v=" + id, snippet});
    seen.add(id);
  }
  // YouTube may recycle rows that have scrolled offscreen. Keep the first-seen
  // order for this document/query so Previous page never turns into a later page.
  let snapshot = globalThis.__habitatYouTubeSearch;
  if (!snapshot || snapshot.query !== query) {
    snapshot = {query, results:[], positions:new Map(), channels:new Map()};
    globalThis.__habitatYouTubeSearch = snapshot;
  }
  snapshot.channels ??= new Map();
  for (const channel of channels) {
    if (snapshot.channels.has(channel.url) || snapshot.channels.size < 10) snapshot.channels.set(channel.url, channel);
  }
  for (const item of results) {
    const position = snapshot.positions.get(item.url);
    if (position !== undefined) snapshot.results[position] = item;
    else if (snapshot.results.length < 1020) {
      snapshot.positions.set(item.url, snapshot.results.length);
      snapshot.results.push(item);
    }
  }
  const loaded = snapshot.results;
  const pageSize = 20;
  const continuation = [...root.querySelectorAll("ytd-continuation-item-renderer")].find(visible);
  const empty = /no results found|try different keywords/i.test(root.querySelector("ytd-background-promo-renderer")?.textContent || "");
  if (loaded.length < start + pageSize && continuation) {
    // YouTube appends results as its own continuation comes into view. No
    // private endpoints or synthetic API requests are used for pagination.
    continuation.scrollIntoView({block:"end"});
    return {ready:false, stage:"loading:rows:" + rows.length + ";videos:" + loaded.length + ";start:" + start +
      ";visibility:" + document.visibilityState + ";links:" + root.querySelectorAll('a[href*="/watch?"]').length + ";anchors:" +
      [...(rows[0]?.querySelectorAll('a') || [])].map(node => node.className + ":text=" + node.textContent.length + ":w=" + node.getBoundingClientRect().width).join(',').slice(0,200)};
  }
  const page = loaded.slice(start, start + pageSize);
  const nextStart = start + page.length;
  const pageResults = start === 0 ? [...snapshot.channels.values(), ...page] : page;
  return {ready:pageResults.length > 0 || empty, results:pageResults, stage:"read:rows:" + rows.length + ";videos:" + loaded.length + ";start:" + start,
    nextStart:nextStart <= 990 && (loaded.length > nextStart || continuation) ? nextStart : null};
}

void youtubeSearchTransport.start();
