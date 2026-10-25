(() => {
  "use strict";
  const $ = id => document.getElementById(id);
  chrome.storage.local.get(["settings", "recent"], state => {
    const settings = state.settings || {};
    $("auto").checked = settings.autoDownload !== false;
    $("notifications").checked = settings.notifications !== false;
    const recent = Array.isArray(state.recent) ? state.recent : [];
    if (!recent.length) {
      const li = document.createElement("li");
      li.textContent = "No captures yet.";
      $("recent").appendChild(li);
    }
    for (const entry of recent.slice(0, 12)) {
      const li = document.createElement("li");
      li.textContent = entry.filename || "Meshy asset";
      const detail = document.createElement("small");
      detail.textContent = new Date(entry.capturedAt).toLocaleString();
      li.appendChild(detail);
      $("recent").appendChild(li);
    }
  });
  function set(key, value) {
    chrome.storage.local.get(["settings"], state =>
      chrome.storage.local.set({ settings: { ...(state.settings || {}), [key]: value } }));
  }
  $("auto").addEventListener("change", e => set("autoDownload", e.target.checked));
  $("notifications").addEventListener("change", e => set("notifications", e.target.checked));
  chrome.tabs.query({ active: true, currentWindow: true }, tabs => {
    if (tabs[0] && tabs[0].id !== undefined) {
      chrome.runtime.sendMessage({ kind: "fishhwb-popup-open", tabId: tabs[0].id });
    }
  });
  $("save").addEventListener("click", () => chrome.tabs.query({ active: true, currentWindow: true }, tabs => {
    if (!tabs[0] || tabs[0].id === undefined) {
      $("status").textContent = "Open the Meshy tab which loaded the asset.";
      return;
    }
    chrome.tabs.sendMessage(tabs[0].id, { kind: "fishhwb-save-latest" }, result => {
      if (chrome.runtime.lastError || !result || !result.saved) {
        $("status").textContent = "Open or reload the model in a Meshy tab first.";
      } else {
        $("status").textContent = "Download requested: " + result.filename;
      }
    });
  }));
})();
