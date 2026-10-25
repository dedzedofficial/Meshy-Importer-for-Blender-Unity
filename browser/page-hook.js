/* Runs in the Meshy page's MAIN world. Observes only responses Meshy fetches itself. */
(() => {
  "use strict";
  if (window.__fishhwbMeshyHookInstalled) return;
  Object.defineProperty(window, "__fishhwbMeshyHookInstalled", { value: true });
  const LIMIT = 128 * 1024 * 1024;
  const MAGIC = [77, 69, 83, 72, 89, 46, 65, 73];

  function publish(buffer) {
    if (!(buffer instanceof ArrayBuffer) || buffer.byteLength < 64 || buffer.byteLength > LIMIT) return;
    const head = new Uint8Array(buffer, 0, 8);
    if (!MAGIC.every((byte, i) => head[i] === byte)) return;
    const heading = document.querySelector('[data-testid="model-name"], main h1, h1');
    const title = (heading && heading.textContent && heading.textContent.trim()) || document.title || "Asset";
    window.postMessage({ source: "fishhwb-meshy-local-v1", kind: "capture", buffer, title }, location.origin, [buffer]);
  }

  async function inspectFetch(response) {
    if (!response || !response.ok || response.type === "opaque") return;
    if (Number(response.headers.get("content-length")) > LIMIT) return;
    let reader;
    try {
      const copy = response.clone();
      if (!copy.body) return;
      reader = copy.body.getReader();
      const chunks = [];
      let size = 0, verified = false;
      for (;;) {
        const next = await reader.read();
        if (next.done) break;
        if (!next.value || !next.value.length) continue;
        size += next.value.length;
        if (size > LIMIT) { await reader.cancel(); return; }
        chunks.push(next.value);
        if (!verified && size >= 8) {
          const header = new Uint8Array(8);
          let written = 0;
          for (const chunk of chunks) {
            const length = Math.min(8 - written, chunk.length);
            header.set(chunk.subarray(0, length), written);
            written += length;
            if (written === 8) break;
          }
          if (!MAGIC.every((byte, i) => header[i] === byte)) {
            await reader.cancel();
            return;
          }
          verified = true;
        }
      }
      if (!verified || size < 64) return;
      const result = new Uint8Array(size);
      let offset = 0;
      for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.length; }
      publish(result.buffer);
    } catch (_) {
      // Never change the response passed to the Meshy renderer.
      try { if (reader) await reader.cancel(); } catch (_) {}
    }
  }

  const originalFetch = window.fetch;
  if (typeof originalFetch === "function") {
    window.fetch = function (...args) {
      const result = Reflect.apply(originalFetch, this, args);
      Promise.resolve(result).then(inspectFetch, () => {});
      return result;
    };
  }

  const proto = window.XMLHttpRequest && window.XMLHttpRequest.prototype;
  if (proto) {
    const originalSend = proto.send;
    proto.send = function (...args) {
      this.addEventListener("load", function () {
        try {
          if (this.status < 200 || this.status >= 300) return;
          if (this.responseType === "arraybuffer" && this.response instanceof ArrayBuffer) {
            publish(this.response.slice(0)); // Never transfer/detach the page's buffer.
          } else if (this.responseType === "blob" && this.response instanceof Blob &&
                     this.response.size <= LIMIT) {
            this.response.arrayBuffer().then(publish).catch(() => {});
          }
        } catch (_) {}
      }, { once: true });
      return Reflect.apply(originalSend, this, args);
    };
  }
})();
