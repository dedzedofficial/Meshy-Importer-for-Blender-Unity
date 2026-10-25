const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const root = path.resolve(__dirname, "..");
const code = fs.readFileSync(path.join(root, "browser/shared.js"), "utf8");
const sandbox = { ArrayBuffer, Uint8Array };
vm.runInNewContext(code, sandbox);
const f = sandbox.MeshyLocal;

test("accepts only bounded Meshy signature payloads", () => {
  const good = new Uint8Array(64);
  good.set(Buffer.from("MESHY.AI"));
  assert.equal(f.isMeshyPayload(good.buffer), true);
  good[0] = 0;
  assert.equal(f.isMeshyPayload(good.buffer), false);
  assert.equal(f.isMeshyPayload(new ArrayBuffer(0)), false);
  assert.equal(f.isMeshyPayload(new ArrayBuffer(f.MAX_BYTES + 1)), false);
});
test("produces safe deterministic filenames", () => {
  assert.equal(f.filename("Red Dragon | Meshy", "AABBCC123456"), "Meshy_Red_Dragon_aabbcc1234.meshy");
  assert.equal(f.filename("../..\\bad:*?model", "1234"), "Meshy_bad_model_1234.meshy");
  assert.equal(f.filename("Meshy", "deadbeef"), "Meshy_Asset_deadbeef.meshy");
});
test("extension is MV3 and limited to Meshy host", () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, "browser/manifest.json"), "utf8"));
  assert.equal(manifest.manifest_version, 3);
  assert.deepEqual(manifest.host_permissions.sort(), ["https://*.meshy.ai/*", "https://meshy.ai/*"]);
  assert.equal(manifest.permissions.includes("cookies"), false);
  assert.equal(manifest.permissions.includes("history"), false);
  for (const src of manifest.content_scripts.flatMap(entry => entry.js).concat(manifest.background.service_worker,"popup.js")) {
    new vm.Script(fs.readFileSync(path.join(root, "browser", src), "utf8"), { filename: src });
  }
});
