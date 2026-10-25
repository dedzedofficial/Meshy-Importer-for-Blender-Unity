const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const install = require("../site/installer.js");
const chrome = "Mozilla/5.0 (X11; Linux x86_64) Chrome/126.0.0 Safari/537.36";
const edge = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0.0 Safari/537.36 Edg/126.0.0";
const firefox = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14; rv:127.0) Gecko/20100101 Firefox/127.0";
test("Edge takes priority over Chrome", () => {
  const details = install.detect(edge, "Win32", false);
  assert.deepEqual(details, { browser:"edge", os:"Windows", mobile:false });
  assert.equal(install.route(details).kind, "zip");
});
test("Chrome desktop downloads the actual v1.5.2 ZIP", () => {
  const choice = install.route(install.detect(chrome, "Linux", false));
  assert.equal(choice.kind,"zip");
  assert.equal(choice.link, install.zip);
  assert.match(choice.link, /releases\/download\/v1\.5\.2\/Meshy-Importer-Browser\.zip$/);
});
test("mobile clients never get the desktop installation", () => {
  for (const ua of ["Mozilla/5.0 (Linux; Android 14) Chrome/126.0 Mobile Safari/537.36",
    "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0) CriOS/126.0 Mobile Safari/604.1"]) {
    assert.equal(install.route(install.detect(ua, "", false)).kind, "unsupported");
  }
});
test("Firefox Opera and Safari do not advertise non-existent listings", () => {
  for (const ua of [firefox, chrome+" OPR/114.0", "Mozilla/5.0 (Macintosh) Version/17.1 Safari/605.1.15"])
    assert.equal(install.route(install.detect(ua, "MacIntel", false)).kind, "unsupported");
});
test("only known browser-store domains activate future direct store installs", () => {
  const details = install.detect(edge,"Win32",false);
  assert.equal(install.route(details,{edge:"https://example.com/extension"}).kind,"zip");
  assert.equal(install.route(details,{edge:"https://microsoftedge.microsoft.com/addons/detail/verified"}).kind,"store");
});
test("installer is self-contained and does not send tracking data", () => {
  const js = fs.readFileSync(path.join(__dirname,"../site/installer.js"),"utf8");
  const html = fs.readFileSync(path.join(__dirname,"../site/index.html"),"utf8");
  assert.doesNotMatch(js, /fetch\s*\(|XMLHttpRequest|sendBeacon|google-analytics|gtag\s*\(/);
  assert.doesNotMatch(js, /\u2014/);
  assert.match(html, /src="installer\.js"/);
  assert.doesNotMatch(html, /<script[^>]+src="https?:/);
});
