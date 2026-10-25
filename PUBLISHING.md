# Publishing

How to get each package into the places users look for it. The code side is ready; each store needs a one-time submission by the maintainer's account.

## 1. GitHub Releases (every version)

1. Finish `RELEASE_CHECKLIST.md` and merge to `main`.
2. Tag and push: `git tag v1.5.2 && git push origin v1.5.2`.
3. The **Release** workflow (`.github/workflows/release.yml`) checks that the tag matches every version, runs the tests, builds `Meshy-Importer-Blender.zip`, `Meshy-Importer-Unreal.zip`, `Meshy-Importer-Godot.zip` and `Meshy-Importer-Browser.zip`, and publishes the release with the matching `CHANGELOG.md` section as its notes.

README download links are pinned to the v1.5.2 assets; publish and verify that release before shipping those links. In-app update checks still use the newest public release metadata.

## 2. OpenUPM (Unity)

Lets users install with `openupm add com.fishhwb.meshy-importer` or a scoped registry, and get updates in the Package Manager.

1. Go to https://openupm.com/packages/add/ and enter the repository URL.
2. The package is `com.fishhwb.meshy-importer` in the `unity/` folder. OpenUPM builds from git tags (`v1.5.2`), so every GitHub release above becomes an OpenUPM version.
3. Check the submitted package page after the first build. If the build can't find `package.json` in the subfolder, OpenUPM's docs describe the options for packages that aren't at the repository root.

## 3. Blender Extensions (extensions.blender.org)

Lets users install and update from **Edit > Preferences > Get Extensions** inside Blender.

1. Sign in at https://extensions.blender.org and choose **Upload Extension**.
2. Upload `Meshy-Importer-Blender.zip` from the release. It already passes `blender --command extension validate`.
3. The manifest declares its permissions: `files` (import), `network` (update check; skipped when Blender's **Allow Online Access** is off) and `clipboard` (Copy Diagnostics).
4. For later versions, upload the new ZIP to the same listing.

Reviewers check the name, tagline and permissions. The platform may also ask about the `.meshy` format, because it is decoded from Meshy's website rather than a documented export. Have `DISCLAIMER.md` ready.

## 4. Godot Asset Library (godotengine.org/asset-library)

Lets users install from the **AssetLib** tab inside the Godot editor.

1. Add an icon (at least 128×128 PNG) to the repository, e.g. `godot/icon.png`. The Asset Library requires one.
2. Submit at https://godotengine.org/asset-library/asset/submit: category **3D Tools**, Godot **4.2**, license **MIT**, the repository URL, and the release tag as the version.
3. **Folder layout caveat:** the Asset Library installs files from the repository ZIP, where the plugin sits at `godot/addons/meshy_importer`. Users would have to change the install folder in Godot's installer. Choose one before submitting:
   - Use the **Custom** download provider and point it at the release asset `Meshy-Importer-Godot.zip`, which already contains `addons/meshy_importer/` at its root; or
   - keep a small mirror repository that has `addons/meshy_importer` at its root.

## 5. Unreal (Fab)

Code plugins on Fab need a C++ module and per-engine-version builds. This plugin is Python only, so ship it through GitHub Releases for now.

## 6. Browser extension (Chrome / Edge)

Official GitHub v1.5.2 download: [Meshy-Importer-Browser.zip](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/download/v1.5.2/Meshy-Importer-Browser.zip).

GitHub distributes the ZIP, but browsers cannot install an unpacked extension directly from GitHub. For now, users must extract it, enable Developer mode and select **Load unpacked**.

For a genuine one-click installation:

1. Sign in to a Chrome Web Store developer account and/or Microsoft Edge Add-ons developer account.
2. Upload the release ZIP with the Manifest V3 root, correct extension icons and complete permissions explanation.
3. Add screenshots, a support URL, privacy disclosures, any required publisher verification and store listing metadata.
4. Test the extension with a real authorised Meshy model in Chrome and Edge before submitting for review.
5. Wait for store approval and obtain the actual listing URLs.
6. **Only after approval**, change the README badge from Download to Install and point it to the published store listings. Never invent a store URL or extension ID.

Privacy disclosure: model content is held and saved locally; the extension stores only settings and recent hashes/names/timestamps. No model uploads or telemetry occur. Meshy's own site continues to contact its services.
