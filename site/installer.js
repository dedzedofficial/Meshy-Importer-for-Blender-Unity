/* Browser detection runs entirely locally. No tracking, model access or network requests. */
(function () {
  "use strict";
  const project = "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity";
  const zip = project + "/releases/download/v1.5.2/Meshy-Importer-Browser.zip";
  const guide = project + "/blob/main/browser/README.md";
  // Fill these only with real, approved browser-store listing URLs.
  const stores = { edge: "", chrome: "", firefox: "", opera: "" };

  function detect(userAgent, platform, isBrave) {
    const ua = String(userAgent || "");
    let browser = "other";
    if (/Edg\/|EdgA\/|EdgiOS\//i.test(ua)) browser = "edge";
    else if (/OPR\/|Opera\//i.test(ua)) browser = "opera";
    else if (/Firefox\/|FxiOS\//i.test(ua)) browser = "firefox";
    else if (/Vivaldi\//i.test(ua)) browser = "vivaldi";
    else if (isBrave) browser = "brave";
    else if (/Chrome\/|Chromium\/|CriOS\//i.test(ua)) browser = "chrome";
    else if (/Safari\//i.test(ua)) browser = "safari";
    const p = String(platform || "");
    let os = "Desktop";
    if (/Windows/i.test(p) || /Windows/i.test(ua)) os = "Windows";
    else if (/Mac/i.test(p) || /Macintosh/i.test(ua)) os = "macOS";
    else if (/Linux/i.test(p) || /Linux|X11/i.test(ua)) os = "Linux";
    if (/Android/i.test(ua)) os = "Android";
    else if (/iPhone|iPad|iPod/i.test(ua)) os = "iOS / iPadOS";
    return { browser, os, mobile: /Android|iPhone|iPad|iPod|Mobile/i.test(ua) };
  }
  function route(info, availableStores) {
    const names = { chrome:"Google Chrome",edge:"Microsoft Edge",opera:"Opera",firefox:"Firefox",
      brave:"Brave",vivaldi:"Vivaldi",safari:"Safari",other:"Other browser" };
    const name = names[info.browser] || "Other browser";
    if (info.mobile) return { name, kind:"unsupported", label:"View compatibility guide",
      link:guide, message:"The current extension is not supported on mobile browsers." };
    const listing = String((availableStores || stores)[info.browser] || "");
    if (/^https:\/\/(microsoftedge\.microsoft\.com|chromewebstore\.google\.com|addons\.mozilla\.org|addons\.opera\.com)\//.test(listing))
      return { name, kind:"store", label:"Install for " + name,
        link:listing, message:"This browser has an official published extension-store listing." };
    if (info.browser === "edge" || info.browser === "chrome")
      return { name, kind:"zip", label:"Download for " + name,
        link:zip, message:"The tested release is available as a ZIP. Use Developer mode and Load unpacked to install." };
    return { name, kind:"unsupported", label:"Read compatibility guide",
      link:guide, message:"There is no tested package or store listing for this browser yet." };
  }
  function start() {
    const ua = navigator.userAgent || "";
    const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
    let selection = detect(ua, platform, false);
    let manual = false;
    const buttons = Array.from(document.querySelectorAll("[data-browser]"));
    const action = document.getElementById("action");
    function show() {
      const result = route(selection);
      document.getElementById("name").textContent = result.name;
      document.getElementById("platform").textContent = selection.os +
        (selection.mobile ? " (mobile)" : " (desktop)");
      document.getElementById("message").textContent = result.message;
      action.textContent = result.label;
      action.href = result.link;
      action.rel = "noopener noreferrer";
      const texts = result.kind === "store"
        ? ["Open the official store listing.", "Review and approve its browser permissions.",
           "Visit a completed Meshy model to use the extension."]
        : result.kind === "zip"
        ? ["Download the v1.5.2 ZIP from GitHub.", "Extract it into a permanent folder.",
           "Open " + (selection.browser === "edge" ? "edge://extensions" : "chrome://extensions") +
           ", enable Developer mode, then click Load unpacked and select the extracted folder."]
        : ["This browser is not tested for the current extension.",
           "Review the installation and compatibility guide.", "Use desktop Chrome or Edge for the supported build."];
      const steps = document.getElementById("steps");
      steps.replaceChildren(...texts.map(function (text) {
        const li = document.createElement("li");
        li.textContent = text;
        return li;
      }));
      buttons.forEach(function (button) {
        button.setAttribute("aria-pressed", String(button.dataset.browser === selection.browser));
      });
    }
    buttons.forEach(function (button) {
      button.addEventListener("click", function () {
        manual = true;
        selection = { ...selection, browser: button.dataset.browser };
        show();
      });
    });
    show();
    if (navigator.brave && typeof navigator.brave.isBrave === "function")
      navigator.brave.isBrave().then(function (found) {
        if (found && !manual && selection.browser === "chrome") {
          selection = detect(ua, platform, true);
          show();
        }
      }).catch(function () {});
  }
  if (typeof module === "object" && module.exports) module.exports = { detect, route, zip, guide };
  if (typeof document !== "undefined") start();
})();