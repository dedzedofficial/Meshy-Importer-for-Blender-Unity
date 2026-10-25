# FISHHWB Meshy Asset Capture — Chrome / Edge

This optional Manifest V3 extension saves genuine `.meshy` files when Meshy's open browser tab loads them. It does **not** poll cloud jobs or intercept login sessions; it merely observes the responses the existing Meshy site already downloads.

## Install

1. Download or clone this repository; open `chrome://extensions` or `edge://extensions`.
2. Enable **Developer mode**, select **Load unpacked**, and choose the `browser/` folder containing `manifest.json`.
3. Open or reload a completed Meshy model page. When it actually loads a `MESHY.AI` model payload, an in-page popup appears; if allowed, a desktop notification appears too.
4. By default the extension requests a browser download named like `Meshy_Red_Dragon_123abc4567.meshy`. In the extension toolbar popup, turn automatic saving or notifications on/off. If off, use the page popup's **Download .meshy** button.
5. Import the saved `.meshy` file through Unity, Blender, Godot or Unreal. The toolbar popup lists recent capture names and dates and allows **Download latest model again** while that browser tab still has the bytes in memory.

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

Run `node --test tests/browser-extension.test.cjs` from the repository root. The release builder `python tools/build_zips.py --release dist` creates `Meshy-Importer-Browser.zip` with `manifest.json` at its root.
