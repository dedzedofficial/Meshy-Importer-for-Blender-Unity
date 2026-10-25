"""One-time v1.5.2 release migration. Run on the release branch only."""
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]
URL = "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity"
ASSET = URL + "/releases/download/v1.5.2/Meshy-Importer-Browser.zip"

def read(path):
    return (ROOT / path).read_text(encoding="utf-8")

def write(path, value):
    file = ROOT / path
    file.parent.mkdir(parents=True, exist_ok=True)
    file.write_text(value, encoding="utf-8")

def replace(path, old, new):
    value = read(path)
    if old not in value:
        raise RuntimeError(path + ": missing " + repr(old[:85]))
    write(path, value.replace(old, new))

for path in ("browser/manifest.json", "unity/package.json", "unreal/MeshyImporter/MeshyImporter.uplugin", "FISHHWB_METADATA.json"):
    value = json.loads(read(path))
    if "version" in value:
        value["version"] = "1.5.2"
    if "VersionName" in value:
        value["VersionName"] = "1.5.2"
        value["Version"] = 152
    if path == "unity/package.json":
        value["description"] = value["description"].replace(" -- ", ", ")
    write(path, json.dumps(value, indent=2, ensure_ascii=False) + "\n")

for path in (
    "blender/meshy_blender_importer/blender_manifest.toml",
    "core/python/meshy_core/__init__.py",
    "godot/addons/meshy_importer/plugin.cfg",
    "godot/addons/meshy_importer/meshy_scene_importer.gd",
    "godot/addons/meshy_importer/meshy_support.gd",
    "unreal/MeshyImporter/Content/Python/meshy_unreal.py",
    "unity/Editor/MeshyImporterMenu.cs",
):
    replace(path, "1.5.1", "1.5.2")

replace("blender/meshy_blender_importer/__init__.py", '"version": (1, 5, 1)', '"version": (1, 5, 2)')
for path in ("blender/meshy_blender_importer/README.md", "godot/README.md",
             "unreal/README.md", "unity/README.md", "COMPATIBILITY.md"):
    replace(path, "1.5.1", "1.5.2")

# Consolidate the standalone 1.5.1 workflow notes into the current Unity guide.
unity = read("unity/README.md").rstrip() + """

## Advanced workflows in v1.5.2

- **Material Overrides:** Select a standalone .mat asset per source material slot and Apply. Unique names remain matched even if slot order changes. Duplicate names use the source slot index and name; missing material overrides must be fixed before reimport.
- **Editable copies:** Create Editable Copy (Prefab + Assets) generates independent meshes, textures, materials and a prefab in a unique folder under Assets/MeshyCustom. It never overwrites existing edited assets. Copies are deliberately independent of future source changes.
- **Settings recovery:** Settings are stored in the model's .meta file, with the last successful native import snapshot also saved under ProjectSettings/MeshyImporter/LastSuccessful. Preserve .meta files and project backups to retain the original asset GUID and restore saved material references.
- **Preflight:** Run Preflight checks structures, ranges, indices and scene references and identifies unsupported features. Unsupported content may require the GLB fallback. This structural check is not a full glTF security validator.
- **Import summaries:** The inspector reports dimensions, bones, texture sizes, geometry counts and an estimated uncompressed texture memory footprint. The estimate is not measured GPU VRAM.

v1.5.2 preserves Unity's existing ScriptedImporter cache revision 9 because the actual asset decode and build behaviour has not changed.
"""
write("unity/README.md", unity)

repair = read("UPGRADE_1.5.1.md")
repair = repair.replace("Meshy Importer 1.5.1 — install and fix the glTFast conflict",
                        "Unity glTFast dependency conflict repair")
repair = repair.replace("1.5.1", "1.5.2")
repair = repair.replace("## New workflow features (same 1.5.2 version)",
    "## Advanced workflows")
repair = re.sub(r"Read \`WORKFLOW_1\.5\.1\.md\`[^\n]*",
                "See the advanced workflow section in unity/README.md for editable copies, overrides, recovery, preflight and summaries.",
                repair)
write("UNITY_GLTFast_REPAIR.md", repair)
for path in ("WORKFLOW_1.5.1.md", "UPGRADE_1.5.1.md", "blender/README.md"):
    (ROOT / path).unlink()

# Keep a focused project overview with one real download link per package.
write("README.md", f"""# Meshy Importer for Unity, Blender, Godot and Unreal

**Version 1.5.2 | FISHHWB / Ded Zed**

[![Validation]({URL}/actions/workflows/validate.yml/badge.svg)]({URL}/actions/workflows/validate.yml) [![MIT](https://img.shields.io/badge/License-MIT-blue)](LICENSE)

Import and convert Meshy `.meshy` assets on your own device. Unity, Blender, Godot, Unreal and Chrome/Edge are supported. The project does not upload model files to an external conversion service.

## Browser extension

[![Download Browser Extension v1.5.2](https://img.shields.io/badge/DOWNLOAD-Browser_Extension_v1.5.2-087ea4?style=for-the-badge&logo=googlechrome&logoColor=white)]({ASSET})

**[Download the browser extension v1.5.2]({ASSET})** | [Installation and privacy details](browser/README.md)

1. Download and extract the ZIP.
2. Open `chrome://extensions` or `edge://extensions`.
3. Enable **Developer mode**, choose **Load unpacked**, and select the extracted folder containing `manifest.json`.

Chrome and Edge do not permit a GitHub site to install an unpublished extension with a single click. An actual one-click **Install** button can be added once approved Chrome Web Store or Edge Add-ons listings exist. The current button correctly says **Download**.

When an open Meshy model page loads a genuine `MESHY.AI` payload, the extension validates it, names it, offers a popup and can download it locally. It does not poll Meshy's cloud generation jobs in the background.

## Download the importers

| Platform | v1.5.2 download | Setup |
|---|---|---|
| Chrome / Edge | [Browser ZIP]({ASSET}) | Extract, then Extensions > Developer mode > Load unpacked |
| Unity 2020.3+ | [Unity package source]({URL}/tree/v1.5.2/unity) | Add package from Git URL: `https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity#v1.5.2` |
| Blender 3.6+ | [Blender ZIP]({URL}/releases/download/v1.5.2/Meshy-Importer-Blender.zip) | Preferences > Extensions or Add-ons > Install from Disk |
| Godot 4.2+ | [Godot ZIP]({URL}/releases/download/v1.5.2/Meshy-Importer-Godot.zip) | Unzip into project and enable the addon |
| Unreal 5.3+ | [Unreal ZIP]({URL}/releases/download/v1.5.2/Meshy-Importer-Unreal.zip) | Unzip into the project's Plugins folder and restart |

Normal `.glb`, `.fbx` and `.obj` files from Meshy's ordinary Download action are already supported by your 3D tools. This importer is specifically for Meshy's `.meshy` payload. See [how to obtain it](GETTING_A_MESHY_FILE.md).

## Import and conversion

- **Unity:** Drop a `.meshy` into `Assets/`. Configure scale, UV repair, materials, colliders, optimisation and editable copies in the Inspector. [Unity guide](unity/README.md).
- **Blender:** File > Import > Meshy Model (.meshy), with repair and optional GLB export settings. [Blender guide](blender/meshy_blender_importer/README.md).
- **Godot:** Add `.meshy` files to the FileSystem dock after enabling the plugin. [Godot guide](godot/README.md).
- **Unreal:** Tools > Meshy > Import .meshy Files, or use the optional auto-import inbox. [Unreal guide](unreal/README.md).
- **Command line:** Use Python 3.9+ with `cd core/python && python -m meshy_core path/to/model.meshy` to produce an ordinary GLB.

## Privacy

FISHHWB model capture, decoding and conversion run on the user's device. No model content, source asset or credential is uploaded by these tools. The Meshy website still communicates with Meshy's own servers to display your models. Engine update checks can optionally request public GitHub release metadata. Downloading/installing from GitHub also communicates with GitHub. [Security policy](SECURITY.md).

## Guides and support

[Compatibility](COMPATIBILITY.md) | [Troubleshooting](TROUBLESHOOTING.md) | [glTFast dependency repair](UNITY_GLTFast_REPAIR.md) | [Changelog](CHANGELOG.md) | [Publishing and store submission](PUBLISHING.md) | [Contributing](CONTRIBUTING.md) | [Release checklist](RELEASE_CHECKLIST.md)

[Discord](https://discord.gg/vCcsnX4HQP) | [Patreon](https://www.patreon.com/cw/DedZed)

This is an unofficial project and is not affiliated with or endorsed by Meshy. Use only models you are authorised to access.
""")

write("browser/README.md", f"""# FISHHWB Meshy Asset Capture

**Version 1.5.2 | Chrome and Microsoft Edge**

**[Download v1.5.2]({ASSET})**

## Install from GitHub

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
""")

# Update current support docs, preserving historical changelog entries.
replace("GETTING_A_MESHY_FILE.md", "[FISHHWB Meshy Asset Capture](browser/README.md)",
        "[FISHHWB Meshy Asset Capture v1.5.2](browser/README.md)")
replace("PUBLISHING.md", "v1.5.1", "v1.5.2")
pub = read("PUBLISHING.md")
start = pub.index("## 6. Browser extension (Chrome / Edge)")
pub = pub[:start] + f"""## 6. Browser extension (Chrome / Edge)

Official GitHub v1.5.2 download: [Meshy-Importer-Browser.zip]({ASSET}).

GitHub distributes the ZIP, but browsers cannot install an unpacked extension directly from GitHub. For now, users must extract it, enable Developer mode and select **Load unpacked**.

For a genuine one-click installation:

1. Sign in to a Chrome Web Store developer account and/or Microsoft Edge Add-ons developer account.
2. Upload the release ZIP with the Manifest V3 root, correct extension icons and complete permissions explanation.
3. Add screenshots, a support URL, privacy disclosures, any required publisher verification and store listing metadata.
4. Test the extension with a real authorised Meshy model in Chrome and Edge before submitting for review.
5. Wait for store approval and obtain the actual listing URLs.
6. **Only after approval**, change the README badge from Download to Install and point it to the published store listings. Never invent a store URL or extension ID.

Privacy disclosure: model content is held and saved locally; the extension stores only settings and recent hashes/names/timestamps. No model uploads or telemetry occur. Meshy's own site continues to contact its services.
"""
pub = pub.replace("The README's download buttons use `releases/latest/download/<asset>`, so they point at the newest release automatically. **They return 404 until the first release is published.** The in-app update checks read the same \"latest release\", so they stay silent until then too.",
"README download links are pinned to the v1.5.2 assets; publish and verify that release before shipping those links. In-app update checks still use the newest public release metadata.")
write("PUBLISHING.md", pub)

release = read("RELEASE_CHECKLIST.md")
release = release.replace("the prominent install badge", "the download badge").replace("Test the prominent install badge", "Test the v1.5.2 download badge")
release = release.replace("The Release workflow builds the Blender, Unreal and Godot ZIPs",
                          "The Release workflow builds the Blender, Unreal, Godot and browser ZIPs")
release += "\n- [ ] Confirm the v1.5.2 GitHub release exists and every README download URL returns its actual binary asset.\n- [ ] Check no active Markdown contains em dashes or links to retired standalone guides.\n"
write("RELEASE_CHECKLIST.md", release)

trouble = read("TROUBLESHOOTING.md")
trouble = trouble.replace("## Browser extension installation or capture issues",
f"## Browser extension installation or capture issues\n\n- **The install link gave a 404:** Download the v1.5.2 extension ZIP from {ASSET}. Extract it and use **Load unpacked** in Chrome/Edge Extensions. GitHub cannot install an unpublished extension with one click.")
write("TROUBLESHOOTING.md", trouble)

old = read("CHANGELOG.md")
old = old.replace("## Unreleased — browser capture and local-data clarity", "## 1.5.2 - 2026-10-25", 1)
old = old.replace("## 1.5.2 - 2026-10-25",
"""## 1.5.2 - 2026-10-25

- Fix extension download instructions by using a version-pinned published release ZIP instead of pretending GitHub can directly install a browser extension.
- Update all engine, decoder, browser and package versions to 1.5.2.
- Clean up duplicated documentation, consolidate the Unity workflow notes, and rename the still-relevant glTFast repair guide.
- Remove em dashes from active Markdown while preserving historical changelog version numbers.

""", 1)
write("CHANGELOG.md", old)

# Do not rewrite past release numbers in changelogs, but remove obsolete guide links.
for file in ROOT.rglob("*.md"):
    if any(part.startswith(".") for part in file.relative_to(ROOT).parts):
        continue
    value = file.read_text(encoding="utf-8")
    value = value.replace("WORKFLOW_1.5.1.md", "unity/README.md")
    value = value.replace("UPGRADE_1.5.1.md", "UNITY_GLTFast_REPAIR.md")
    value = value.replace("downloads/Meshy-Importer-Browser.zip?raw=1", "releases/download/v1.5.2/Meshy-Importer-Browser.zip")
    value = value.replace("—", ":").replace("–", "-")
    file.write_text(value, encoding="utf-8")

# A versioned download is not a committed repo tree ZIP, so update the test expectation.
testpath = "tests/browser-extension.test.cjs"
test = read(testpath)
test = test.replace("downloads\\/Meshy-Importer-Browser.zip\\?raw=1",
                    "releases\\/download\\/v1\\.5\\.2\\/Meshy-Importer-Browser\\.zip")
test = test.replace("source browser ZIP is configured for committed direct installation",
                    "browser ZIP is version-pinned to release download")
write(testpath, test)
print("v1.5.2 version and documentation migration complete.")
