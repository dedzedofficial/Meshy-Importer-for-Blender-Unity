# Publishing

How to get each package into the places users look for it. The code side is ready; each store needs a one-time submission by the maintainer's account.

## 1. GitHub Releases (every version)

1. Finish `RELEASE_CHECKLIST.md` and merge to `main`.
2. Tag and push: `git tag v1.5.0 && git push origin v1.5.0`.
3. The **Release** workflow (`.github/workflows/release.yml`) checks that the tag matches every version, runs the tests, builds `Meshy-Importer-Blender.zip`, `Meshy-Importer-Unreal.zip` and `Meshy-Importer-Godot.zip`, and publishes the release with the matching `CHANGELOG.md` section as its notes.

The README's download buttons use `releases/latest/download/<asset>`, so they point at the newest release automatically. **They return 404 until the first release is published.** The in-app update checks read the same "latest release", so they stay silent until then too.

## 2. OpenUPM (Unity)

Lets users install with `openupm add com.fishhwb.meshy-importer` or a scoped registry, and get updates in the Package Manager.

1. Go to https://openupm.com/packages/add/ and enter the repository URL.
2. The package is `com.fishhwb.meshy-importer` in the `unity/` folder. OpenUPM builds from git tags (`v1.5.0`), so every GitHub release above becomes an OpenUPM version.
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
