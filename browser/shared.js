/* Validate model signature and sanitize local filenames. */
(function (root) {
  "use strict";
  const MAGIC = [77, 69, 83, 72, 89, 46, 65, 73];
  const MAX_BYTES = 128 * 1024 * 1024;
  function isMeshyPayload(data) {
    if (!(data instanceof ArrayBuffer) || data.byteLength < 64 || data.byteLength > MAX_BYTES) return false;
    const head = new Uint8Array(data, 0, 8);
    return MAGIC.every((v, i) => head[i] === v);
  }
  function safeTitle(raw) {
    let value = String(raw || "").replace(/(?:\s*[-–—|]\s*)?Meshy(?:\.ai|\s*AI)?\s*$/i, "");
    value = value.replace(/[<>:"/\\|?*\u0000-\u001f\u007f]/g, " ")
      .replace(/\s+/g, " ").replace(/^[.\s]+|[.\s]+$/g, "").slice(0, 80).trim();
    if (!value || /^(meshy|home|workspace|create|explore|generate|untitled|model)$/i.test(value)) return "Asset";
    if (/^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i.test(value)) return "Asset";
    return value;
  }
  function filename(title, digest) {
    const hash = String(digest || "").replace(/[^a-f0-9]/gi, "").slice(0, 10).toLowerCase();
    return "Meshy_" + safeTitle(title).replace(/\s+/g, "_") + "_" + (hash || "capture") + ".meshy";
  }
  root.MeshyLocal = Object.freeze({ MAX_BYTES, isMeshyPayload, safeTitle, filename });
})(globalThis);
