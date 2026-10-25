# Meshy Importer for Blender & Unity

**Workflow upgrades:** material overrides, protected editable copies, remembered settings, preflight and import summaries. See `WORKFLOW_1.5.1.md` in the release archive.

**Version 1.5.1 (Unity, Blender, Godot and Unreal) — created and maintained by FISHHWB | Ded Zed**

**1.5.1 glTFast fixes:** [Install and repair an existing collision](UPGRADE_1.5.1.md).

[![Validate repository](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/actions/workflows/validate.yml/badge.svg)](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/actions/workflows/validate.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Import Meshy `.meshy` models into **Unity, Blender, Godot and Unreal Engine**. Everything is decoded on your own computer; nothing is uploaded.

> **Do you need this?** If you used Meshy's normal **Download** button, you already have a `.glb`, `.fbx` or `.obj` file. Import that directly; you don't need this importer. This importer is for `.meshy` files saved from the Meshy website ([how](GETTING_A_MESHY_FILE.md)).

## 🔒 Local-first asset privacy

**Local-only model data:** This project captures, decodes, converts and imports asset bytes on your device. No asset bytes, project files or credentials are uploaded by the FISHHWB tools. Meshy's own website still communicates with Meshy's servers, and optional GitHub release checks access public update metadata; disable those checks in each host if desired.

## 🌐 Browser extension: save models without DevTools

[![Install Browser Extension](https://img.shields.io/badge/INSTALL-Chrome_%26_Edge_Extension-087ea4?style=for-the-badge&logo=googlechrome&logoColor=white)](downloads/Meshy-Importer-Browser.zip?raw=1)

**[Download the extension ZIP](downloads/Meshy-Importer-Browser.zip?raw=1)** · [Installation guide](browser/README.md)

Extract the ZIP, open `chrome://extensions` (Chrome) or `edge://extensions` (Edge), enable **Developer mode**, click **Load unpacked**, and select the extracted folder containing `manifest.json`. Browsers do not permit direct installation of an unpublished extension just by clicking a GitHub link.

Install the [Chrome/Edge browser extension](browser/README.md) (developer mode, Load unpacked) and open a finished model on **meshy.ai**. When the Meshy page **actually loads a genuine .meshy payload**, the extension shows a ready popup, can show a desktop notification, and automatically requests a local download with a cleaned name like `Meshy_Red_Dragon_123abc4567.meshy`. Auto-download and notifications can each be switched off in the extension popup. It remembers local filename/hash history to avoid duplicates; model bytes never enter extension storage or any FISHHWB server.

This is an **on-page capture**, not a cloud generation-status checker: keep the model tab open or reload the finished model. [Manual saving instructions](GETTING_A_MESHY_FILE.md) remain available. Supported browser builds: Chrome/Edge Manifest V3; Firefox and Safari are not currently supported.

## 🚀 Quick start

### 1. Download for your engine

| Engine | Download | Install |
|---|---|---|
| **Chrome / Edge** | **[Install Browser Extension](downloads/Meshy-Importer-Browser.zip?raw=1)** | Download the ZIP, extract it, then **Extensions > Developer mode > Load unpacked**. [Instructions](browser/README.md). |
| **Unity** 2020.3+ | Nothing to download | **Window > Package Manager > + > Add package from git URL...** and paste:<br>`https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity` |
| **Blender** 3.6+ | [Meshy-Importer-Blender.zip](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest/download/Meshy-Importer-Blender.zip) | Blender 4.2+: **Edit > Preferences > Get Extensions > ⌄ > Install from Disk**.<br>Blender 3.6–4.1: **Edit > Preferences > Add-ons > Install...** |
| **Godot** 4.2+ | [Meshy-Importer-Godot.zip](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest/download/Meshy-Importer-Godot.zip) | Unzip into your project (you get `addons/meshy_importer`), then turn it on in **Project > Project Settings > Plugins**. |
| **Unreal** 5.3+ | [Meshy-Importer-Unreal.zip](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest/download/Meshy-Importer-Unreal.zip) | Unzip into your project's `Plugins/` folder and restart the editor. |
| Anything else | [Command-line converter](#-command-line-converter) | Turns a `.meshy` into a normal `.glb`. |

The browser extension ZIP is also [available directly in this repository](downloads/Meshy-Importer-Browser.zip?raw=1), even when no GitHub release exists.

All downloads are also on the [Releases page](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases). The same ZIPs are kept in this repository at `blender/` and `unreal/`.

### 2. Get a `.meshy` file

**Recommended:** install the [browser extension](browser/README.md), open the model on Meshy, and let it detect and save the model locally. Or [save the model request manually from browser DevTools](GETTING_A_MESHY_FILE.md) without installing an extension.

### 3. Import it

| Engine | How |
|---|---|
| Unity | Drop the file anywhere under `Assets/`. Select it to change its settings. |
| Blender | **File > Import > Meshy Model (.meshy)**, or drag it into the viewport (4.1+). |
| Godot | Drop it anywhere in the FileSystem dock. It imports like any 3D scene. |
| Unreal | **Tools > Meshy > Import .meshy Files...**, or turn on **Auto-Import Inbox Folder** and drop files into `<Project>/MeshyInbox/`. |

Something went wrong? The error message says what and how to fix it, and links to the matching part of **[Troubleshooting](TROUBLESHOOTING.md)**.

## What every importer does

- **Decrypts** the `.meshy` file locally.
- **Decodes Meshy's compression** (`EXT_meshopt_compression` geometry, `KHR_mesh_quantization`, WebP textures) wherever the engine's own glTF importer can't. Stock Blender, Godot and Unreal all reject current Meshy files without this step.
- **Repairs broken UVs** (on by default): NaN values, wild outliers and collapsed triangles are fixed from neighbouring UVs. Valid Meshy UVs are never changed. Meshes with no UVs get new ones.
- **Explains wrong files** in plain words: a saved web page, a JSON response, an FBX/OBJ/GLB download, a ZIP or an image each get their own message and fix.

### Import options

The same options, with the same names, wherever the engine allows:

| Option | Unity | Blender | Godot | Unreal |
|---|---|---|---|---|
| Preset (Default / Keep Original Data) | Inspector | Import panel | – | – |
| Scale | Scale Factor | Scale | Nodes > Root Scale (built in) | **Import .meshy Files (Show Options)...** |
| Auto-repair UVs | ✔ | ✔ | ✔ | on |
| Generate colliders | ✔ | – | Advanced Import Settings (built in) | Show Options dialog |
| Save decoded `.glb` | **Convert .meshy to GLB** | ✔ | ✔ | – |

## ⭐ Unity

Drop `.meshy` files anywhere under `Assets/`. Unity builds the meshes, materials, textures and skinning itself: **no UnityGLTF or glTFast needed**. Built-in, URP and HDRP are supported.

Select a `.meshy` asset to see its **import settings** (preset, scale factor, auto-repair UVs, colliders, mesh optimization), what was imported, and **Reimport** / **Validate** buttons. If an import failed, the Inspector shows why, with **Open Help**, **Copy Diagnostics** and **Report a Bug** buttons.

**Tools > Meshy > Meshy Importer** opens one window with everything: a list of files that failed to import, Reimport All, Convert All to `.glb`, help links, diagnostics and update settings. The rest of the menu:

- **Tools > Meshy > Reimport Selected .meshy** / **Reimport All .meshy In Assets**
- **Tools > Meshy > Convert .meshy to GLB...** / **Convert All .meshy In Assets**
- **Tools > Meshy > Help >** How Do I Get a .meshy File?, Troubleshooting, Documentation, Validate Installation, Copy Diagnostics, Report a Bug, Check for Updates, Discord, Support on Patreon, About
- **Tools > Meshy > Advanced >** Validate All .meshy In Assets, Install UnityGLTF (Optional Fallback), Show Welcome Again

UnityGLTF (`org.khronos.unitygltf`) is only used as a fallback, written as a `.glb` next to the file, for a rare payload using a glTF extension the native builder doesn't implement. See [`unity/README.md`](unity/README.md).

## 🎨 Blender

Install the ZIP (see [Quick start](#-quick-start)), then use **File > Import > Meshy Model (.meshy)**. Options in the import panel: **Preset**, **Scale**, **Auto-repair UVs**, **Remove Unused Material Slots**, **Save Decoded .glb**. After an import, the status bar shows what came in (meshes, faces, materials, UV fixes). If a file fails, a popup explains why with **Open Help**, **Copy Diagnostics** and **Report a Bug**.

**Help > Meshy Importer** has the guides, diagnostics, bug report and update check. See [`blender/README.md`](blender/README.md).

## 🎮 Godot

Unzip `addons/meshy_importer` into your project and enable **Meshy Importer** under **Project > Project Settings > Plugins**. `.meshy` files then import like any other 3D scene: reimport, Advanced Import Settings, instancing. **Project > Tools > Meshy Importer** has the guides, diagnostics, bug report and update check. See [`godot/README.md`](godot/README.md).

## 🛠️ Unreal Engine

Unzip into your project's `Plugins/` folder and restart the editor. The plugin turns on the Python and Editor Scripting plugins it needs by itself, and tells you once if Unreal's glTF importer is switched off.

- **Tools > Meshy > Import .meshy Files...**, or right-click a Content Browser folder > **Import .meshy Files Here...**
- **Tools > Meshy > Import .meshy Files (Show Options)...** shows Unreal's own import options (scale, collision, materials).
- **Tools > Meshy > Auto-Import Inbox Folder (On/Off)**: drop `.meshy` files into `<Project>/MeshyInbox/` and they are imported within a few seconds.
- **Tools > Meshy > Meshy Importer Help >** guides, diagnostics, bug report and update check.

The plugin is Editor Python only, with no C++ build. See [`unreal/README.md`](unreal/README.md).

## 🧪 Command-line converter

The shared decoder (`core/python/meshy_core`) is plain Python 3.9+ with no dependencies:

```text
cd core/python
python -m meshy_core path/to/model.meshy                 # writes model.glb (plain glTF 2.0)
python -m meshy_core *.meshy -o out/                      # several files
python -m meshy_core model.meshy --scale 0.01             # scale the whole model
python -m meshy_core model.meshy --raw                    # decrypt only, keep Meshy's extensions
```

The converted `.glb` has no required extensions: meshopt decoded, attributes dequantized, texture transforms baked, WebP converted to PNG, and UVs repaired. It passes the Khronos glTF validator and imports into any glTF tool. Pillow is used for WebP when installed; otherwise a built-in decoder is used.

## 🆘 Getting help

1. Read the error message: it says what's wrong and links to the fix.
2. Check **[TROUBLESHOOTING.md](TROUBLESHOOTING.md)**.
3. Use **Copy Diagnostics** in your engine's Meshy menu and paste it into a [bug report](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/issues/new?template=bug_report.yml) or on [Discord](https://discord.gg/vCcsnX4HQP). **Report a Bug** opens a pre-filled report for you.

## Compatibility

| Host | Supported | Recommended |
|---|---|---|
| Unity 2020.3 LTS | Yes | Yes |
| Unity 2021.3 LTS | Yes | Yes |
| Unity 2022.3 LTS | Yes | Yes |
| Unity 6+ | Yes | Yes |
| Blender 3.6 LTS–4.1 | Yes | Legacy add-on workflow |
| Blender 4.2+ | Yes | Extension workflow |
| Blender 5.x | Yes | Yes |
| Godot 4.2+ | Yes | Yes |
| Unreal Engine 5.3+ | Yes | Yes |

See **[COMPATIBILITY.md](COMPATIBILITY.md)** for details.

## 🔒 Privacy

**Your model files are handled locally in all five integrations:** the browser extension, Unity, Blender, Godot, Unreal, and the command-line converter never upload model data, source assets or credentials to FISHHWB. The browser extension reads only binary responses already loaded by the Meshy page, verifies their `.meshy` signature and saves them through the browser's Downloads workflow. Its persistent storage contains preferences and filename/hash history, not model bytes.

**External connections still exist outside that local processing:** Meshy's own website communicates with Meshy's servers to load models. Unity, Blender, Godot and Unreal can make an **optional public GitHub release metadata check** (at most daily). They do not send model bytes; disable the checks in each integration's settings. Installing/updating this project from GitHub also naturally involves a GitHub download. See [Security](SECURITY.md) and [browser privacy details](browser/README.md).

## ❤️ Support

If this saves you time, support continued development on Patreon: https://www.patreon.com/cw/DedZed

## Community

GitHub: https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity

Discord: https://discord.gg/vCcsnX4HQP

## 🧰 Repository development

GitHub Actions runs the Python test-suite (Python 3.9 and 3.12), a Unity compile check against the real UnityEngine reference assemblies, C#/GDScript/Python parity checks (UV repair and wrong-file messages), and headless Blender and Godot imports of a synthetic `.meshy` payload. It also checks that versions agree across every host and that the committed ZIPs match their sources. Pushing a `v*` tag builds the release ZIPs and publishes a GitHub release. See [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`PUBLISHING.md`](PUBLISHING.md).

## Disclaimer

This project is not affiliated with or endorsed by Meshy unless explicitly stated by the creator. The `.meshy` payload is a web application format and may change without notice.
