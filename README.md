# Meshy Importer for Blender & Unity

**Version 1.5.0 (Unity, Blender, Godot and Unreal) — created and maintained by FISHHWB**

[![Validate repository](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/actions/workflows/validate.yml/badge.svg)](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/actions/workflows/validate.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Import Meshy `.meshy` models into **Unity, Blender, Godot and Unreal Engine**. Everything is decoded on your own computer; nothing is uploaded.

> **Do you need this?** If you used Meshy's normal **Download** button, you already have a `.glb`, `.fbx` or `.obj` file. Import that directly; you don't need this importer. This importer is for `.meshy` files saved from the Meshy website ([how](GETTING_A_MESHY_FILE.md)).

## 🚀 Quick start

### 1. Download for your engine

| Engine | Download | Install |
|---|---|---|
| **Unity** 2020.3+ | Nothing to download | **Window > Package Manager > + > Add package from git URL...** and paste:<br>`https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity` |
| **Blender** 3.6+ | [Meshy-Importer-Blender.zip](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest/download/Meshy-Importer-Blender.zip) | Blender 4.2+: **Edit > Preferences > Get Extensions > ⌄ > Install from Disk**.<br>Blender 3.6–4.1: **Edit > Preferences > Add-ons > Install...** |
| **Godot** 4.2+ | [Meshy-Importer-Godot.zip](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest/download/Meshy-Importer-Godot.zip) | Unzip into your project (you get `addons/meshy_importer`), then turn it on in **Project > Project Settings > Plugins**. |
| **Unreal** 5.3+ | [Meshy-Importer-Unreal.zip](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest/download/Meshy-Importer-Unreal.zip) | Unzip into your project's `Plugins/` folder and restart the editor. |
| Anything else | [Command-line converter](#-command-line-converter) | Turns a `.meshy` into a normal `.glb`. |

All downloads are also on the [Releases page](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases). The same ZIPs are kept in this repository at `blender/` and `unreal/`.

### 2. Get a `.meshy` file

Open the model on the Meshy website in Chrome, press **F12**, go to **Network**, reload, and save the model's response. The **[step-by-step guide](GETTING_A_MESHY_FILE.md)** walks through it.

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

Models are decoded locally in every engine and are never uploaded to FISHHWB or a conversion server.

The plugins check GitHub for a newer release at most once a day. The check downloads only the public release record; nothing about you or your project is sent. Turn it off in the Unity **Meshy Importer** window, Blender's add-on preferences (it also respects Blender's **Allow Online Access** setting), Godot's **Editor Settings > Meshy Importer**, or Unreal's **Meshy Importer Help** menu.

## ❤️ Support

If this saves you time, support continued development on Patreon: https://www.patreon.com/cw/DedZed

## Community

GitHub: https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity

Discord: https://discord.gg/vCcsnX4HQP

## 🧰 Repository development

GitHub Actions runs the Python test-suite (Python 3.9 and 3.12), a Unity compile check against the real UnityEngine reference assemblies, C#/GDScript/Python parity checks (UV repair and wrong-file messages), and headless Blender and Godot imports of a synthetic `.meshy` payload. It also checks that versions agree across every host and that the committed ZIPs match their sources. Pushing a `v*` tag builds the release ZIPs and publishes a GitHub release. See [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`PUBLISHING.md`](PUBLISHING.md).

## 🔎 Find this project

Useful search terms include **Meshy Importer**, **Meshy AI importer**, **.meshy Unity importer**, **.meshy Blender importer**, **.meshy Godot importer**, **.meshy Unreal importer**, **Meshy to Unity**, **Meshy to Blender**, **Meshy to Godot**, **Meshy to Unreal**, and **Meshy 3D model importer**. See **SEO_KEYWORDS.md**.

## Disclaimer

This project is not affiliated with or endorsed by Meshy unless explicitly stated by the creator. The `.meshy` payload is a web application format and may change without notice.
