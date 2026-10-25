/* Desktop notifications and toolbar badge receive metadata only, never model bytes. */
chrome.runtime.onMessage.addListener((message, sender) => {
  if (!message || message.kind !== "fishhwb-captured" || !sender.tab) return;
  let host;
  try { host = new URL(sender.url || sender.tab.url).hostname; } catch (_) { return; }
  if (host !== "meshy.ai" && !host.endsWith(".meshy.ai")) return;
  const tabId = sender.tab.id;
  chrome.action.setBadgeBackgroundColor({ color: "#127e9d", tabId });
  chrome.action.setBadgeText({ text: "1", tabId });
  chrome.storage.local.get(["settings"], data => {
    if (data.settings && data.settings.notifications === false) return;
    chrome.notifications.create("fishhwb-meshy-" + tabId, {
      type: "basic",
      iconUrl: chrome.runtime.getURL("icons/icon.png"),
      title: "Meshy asset ready",
      message: (message.automatic ? "Download requested: " : "Ready to save: ") + message.filename,
      priority: 1
    });
  });
});

chrome.notifications.onClicked.addListener(id => {
  const match = /^fishhwb-meshy-(\d+)$/.exec(id);
  if (match) chrome.tabs.update(Number(match[1]), { active: true });
});
chrome.runtime.onMessage.addListener(message => {
  if (message && message.kind === "fishhwb-popup-open" && Number.isInteger(message.tabId)) {
    chrome.action.setBadgeText({ text: "", tabId: message.tabId });
  }
});
