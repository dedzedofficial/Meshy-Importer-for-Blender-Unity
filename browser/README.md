# FISHHWB Meshy Asset Capture

**Version 1.5.2 | Chrome and Microsoft Edge**

**[Download v1.5.2](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/download/v1.5.2/Meshy-Importer-Browser.zip)**

## Install from GitHub

For a future browser-aware install flow, see the [smart installer setup and platform support guide](../site/README.md). The direct ZIP method remains the currently published and verified installation route.


1. Download the ZIP from the official v1.5.2 release.
2. Extract it to a folder you will keep.
3. Open `chrome://extensions` or `edge://extensions`.
4. Enable **Developer mode** and click **Load unpacked**.
5. Select the extracted folder containing `manifest.json`.
6. Optionally pin the extension to the browser toolbar.

GitHub cannot silently install an unpacked extension. A real one-click browser installation requires publishing the extension to the Chrome Web Store or Microsoft Edge Add-ons. Those listings are not yet published.

## Usage

Open or reload a completed Meshy model page. When the tab fetches a binary response beginning with `MESHY.AI`, the extension checks the payload, generates a safe name such as `Meshy_Red_Dragon_123abc4567.meshy`, and requests a local download.

Automatic downloading and desktop notifications are configurable in the extension popup. You can download the latest model again while it remains in that tab's memory. If your browser blocks automatic downloads or notifications, check browser permissions.

## Privacy

- Model bytes stay in the active browser tab and local Downloads. The extension never uploads model content or credentials.
- Recent filenames, content hashes, timestamps and preferences are kept in local extension storage to prevent duplicate downloads.
- The extension only has Meshy-site access plus local storage, notification and active-tab permissions. No history or cookie permission is requested.
- Meshy's site naturally contacts Meshy's servers. FISHHWB's local processing does not turn Meshy into an offline service.

## Limits

- Detects only models fetched by an open Meshy tab using supported Fetch/XHR responses, not cloud job completion.
- Capture is limited to 128 MiB per response.
- If Meshy changes its internal format or transport, use the [manual file saving guide](../GETTING_A_MESHY_FILE.md).
- Firefox and Safari are not currently supported.
- Only capture models you are authorised to use.

## Validation and stores

Automated CI validates the ZIP, metadata, naming rules, and a Chromium browser test with a synthetic local payload. A live authenticated Meshy account still requires manual verification before publishing to browser stores. See [publishing instructions](../PUBLISHING.md).
