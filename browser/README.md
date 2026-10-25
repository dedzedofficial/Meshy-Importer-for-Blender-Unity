# FISHHWB Meshy Asset Capture — Chrome / Edge

This optional Manifest V3 extension saves genuine `.meshy` files when Meshy's open browser tab loads them. It does **not** poll cloud jobs or intercept login sessions; it merely observes the responses the existing Meshy site already downloads.

## Install from GitHub

[![Download / Install Extension](https://img.shields.io/badge/INSTALL-Browser_Extension-087ea4?style=for-the-badge&logo=googlechrome&logoColor=white)](../downloads/Meshy-Importer-Browser.zip?raw=1)

1. Click **[Download Browser Extension](../downloads/Meshy-Importer-Browser.zip?raw=1)**. This ZIP is bundled directly in the GitHub repository; a release is not required.
2. Extract the ZIP anywhere convenient.
3. Open `chrome://extensions` (Chrome) or `edge://extensions` (Edge).
4. Enable **Developer mode** and select **Load unpacked**.
5. Select the extracted folder containing `manifest.json`. The extension should then appear in your browser toolbar.

**Chrome/Edge do not allow silent or one-click installation from a GitHub ZIP.** An actual single-click browser installation requires publishing the extension in the Chrome Web Store / Edge Add-ons. This GitHub install button downloads the official source-packaged extension so it can be loaded locally.

## Use

1. Open a completed Meshy model page, or reload it after installing. When it loads a genuine `MESHY.AI` payload, an in-page popup and (if allowed) a desktop notification appear.
2. The extension requests a model download automatically by default; the extension popup lets you turn this off.
3. Import the downloaded `.meshy` file into your engine.

### Alternative developer installation

You can also clone the repository and select its `browser/` folder with **Load unpacked**.

### Capture details


By default the extension requests a browser download named like `Meshy_Red_Dragon_123abc4567.meshy`. In the extension toolbar popup, turn automatic saving or notifications on/off. If off, use the page popup's **Download .meshy** button. Import the saved `.meshy` file through Unity, Blender, Godot or Unreal. The toolbar popup lists recent capture names and dates and allows **Download latest model again** while that browser tab still has the bytes in memory.

The browser may ask where to save files or block automatic downloads. Verify in the Downloads page that saving succeeded. Store submission is not yet completed; this is a **developer-mode, unpacked extension**.

## Local-only data handling

- The extension does not initiate requests to Meshy, GitHub, FISHHWB or any other host; it observes the model response already being fetched by the Meshy page, verifies `MESHY.AI` and saves the bytes directly through the browser.
- Models are kept in the page tab's memory and the user's Downloads folder. The extension's persistent local storage contains only preferences and the most recent 50 content hashes, filenames and timestamps, to avoid repeat downloads.
- It does not gather cookies, account credentials, browsing history, tokens or telemetry. Host permissions are restricted to `meshy.ai` and its subdomains. The notification service worker receives names only, never model bytes.
- **Important distinction:** Meshy's own website continues to communicate with Meshy's servers. The engine add-ons can optionally access public GitHub release metadata for update checks. Local-only refers to what FISHHWB tools do with your model files.

## Limitations

- Only captures a real `MESHY.AI` binary response loaded via page Fetch or XMLHttpRequest while the Meshy tab is open; generation status is not monitored on Meshy's servers.
- Up to 128 MiB per captured response. Above that limit, use the [manual DevTools method](../GETTING_A_MESHY_FILE.md).
- It cannot guarantee compatibility if Meshy changes domain, asset format or network transport (e.g. WebSocket/worker). When in doubt, follow the [manual method](../GETTING_A_MESHY_FILE.md).
- Normal GLB/FBX/OBJ exports are already directly supported by 3D editors; they are not captured here.
- The model page heading/title is used for the filename, with a short SHA-256 prefix for uniqueness; generic headings fall back to `Meshy_Asset_<hash>.meshy`.
- Chrome 111+ and Chromium Edge are the supported targets. Firefox and Safari are not supported by this release.
- Only download models you are entitled to use and obey Meshy's terms. This extension neither bypasses accounts nor unlocks paid features.

## Development

A real Chromium smoke test against a local, synthetic Meshy response (no account required) runs in CI. A live authenticated Meshy session must still be verified by the maintainer because site internals can change.

Run `node --test tests/browser-extension.test.cjs` from the repository root. The release builder `python tools/build_zips.py --release dist` creates `Meshy-Importer-Browser.zip` with `manifest.json` at its root.
