/* Isolated world: model bytes remain in this tab, not extension storage/server. */
(() => {
  "use strict";
  const helper = globalThis.MeshyLocal;
  let latest = null, toastNode = null, queue = Promise.resolve();
  const read = keys => new Promise(resolve => chrome.storage.local.get(keys, result => resolve(result || {})));
  const write = value => new Promise(resolve => chrome.storage.local.set(value, resolve));

  function saveLatest() {
    if (!latest) return false;
    const uri = URL.createObjectURL(new Blob([latest.buffer], { type: "application/octet-stream" }));
    const link = document.createElement("a");
    link.download = latest.filename;
    link.href = uri;
    link.style.display = "none";
    (document.body || document.documentElement).appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(uri), 60000);
    return true;
  }

  function showToast(asset, automatic) {
    if (!document.documentElement) return;
    if (toastNode) toastNode.remove();
    const host = document.createElement("div");
    host.style.cssText = "position:fixed;right:16px;bottom:16px;z-index:2147483647";
    const shadow = host.attachShadow({ mode: "closed" });
    const style = document.createElement("style");
    style.textContent = ':host{font:14px system-ui,sans-serif;color:white}.panel{width:310px;box-sizing:border-box;border-radius:12px;padding:15px;background:#132d3a;box-shadow:0 10px 40px #0007}h2{font-size:16px;margin:0 0 5px}p{overflow-wrap:anywhere}button{border:0;border-radius:7px;padding:8px;cursor:pointer;margin-right:8px;background:#75d8f2;color:#09202c}small{display:block;color:#d3e5ef;margin-bottom:10px}';
    const panel = document.createElement("div");
    panel.className = "panel";
    const title = document.createElement("h2");
    title.textContent = "Meshy asset ready";
    const name = document.createElement("p");
    name.textContent = asset.filename;
    const status = document.createElement("small");
    status.textContent = automatic ? "Download requested. Check browser Downloads." : "Ready to save locally.";
    const button = document.createElement("button");
    button.textContent = automatic ? "Save again" : "Download .meshy";
    button.addEventListener("click", () => {
      saveLatest();
      status.textContent = "Download requested. Check browser Downloads.";
    });
    const dismiss = document.createElement("button");
    dismiss.textContent = "Dismiss";
    dismiss.addEventListener("click", () => host.remove());
    panel.append(title, name, status, button, dismiss);
    shadow.append(style, panel);
    document.documentElement.appendChild(host);
    toastNode = host;
  }

  async function capture(payload) {
    if (!helper.isMeshyPayload(payload.buffer)) return;
    const hashBytes = new Uint8Array(await crypto.subtle.digest("SHA-256", payload.buffer));
    const hash = Array.from(hashBytes, byte => byte.toString(16).padStart(2, "0")).join("");
    const filename = helper.filename(payload.title, hash);
    latest = { buffer: payload.buffer, filename };
    const state = await read(["recent", "settings"]);
    const recent = Array.isArray(state.recent) ? state.recent : [];
    if (recent.some(item => item.hash === hash)) return;
    const automatic = !state.settings || state.settings.autoDownload !== false;
    const item = { hash, filename, capturedAt: new Date().toISOString(), automatic };
    await write({ recent: [item, ...recent].slice(0, 50) });
    if (automatic) saveLatest();
    showToast(item, automatic);
    chrome.runtime.sendMessage({ kind: "fishhwb-captured", filename, automatic });
  }

  window.addEventListener("message", event => {
    if (event.source !== window || event.origin !== location.origin) return;
    const payload = event.data;
    if (!payload || payload.source !== "fishhwb-meshy-local-v1" || payload.kind !== "capture") return;
    // Page scripts can spoof messages; only accept a bounded, signed model payload.
    if (!helper.isMeshyPayload(payload.buffer)) return;
    queue = queue.then(() => capture(payload)).catch(() => {});
  });

  chrome.runtime.onMessage.addListener((message, _sender, respond) => {
    if (message && message.kind === "fishhwb-save-latest") {
      respond({ saved: saveLatest(), filename: latest && latest.filename });
    }
  });
})();
