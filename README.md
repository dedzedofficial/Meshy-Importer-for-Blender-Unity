# Meshy Importer for Blender & Unity

**Version 1.4.1 (Unity, Blender, Godot and Unreal) — created and maintained by FISHHWB**

[![Validate repository](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/actions/workflows/validate.yml/badge.svg)](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/actions/workflows/validate.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Import real Meshy `.meshy` model payloads into **Unity, Blender, Godot and Unreal Engine** with a local decoder. Nothing is uploaded anywhere.

| Host | Folder | How you import |
|---|---|---|
| Unity 2020.3+ | [`unity/`](unity/README.md) | Drop `.meshy` into `Assets/` |
| Blender 3.6+ | [`blender/`](blender/README.md) | **File > Import > Meshy Model (.meshy)**, or drag and drop (4.1+) |
| Godot 4.2+ | [`godot/`](godot/README.md) | Put `.meshy` in the project; it imports like any 3D scene |
| Unreal 5.3+ | [`unreal/`](unreal/README.md) | **Tools > Meshy > Import .meshy Files...** |
| Anything else | [`core/python`](#-command-line-converter) | `python -m meshy_core model.meshy`, then use the `.glb` |

> **Important:** Meshy's normal Download workflow uses standard formats such as GLB, FBX, and OBJ. This importer is for the `.meshy` model payload obtained through the browser Network workflow. Do not rename a GLB to `.meshy`.

### What every importer does

- **Decrypts** the `.meshy` container locally.
- **Decodes Meshy's compression** (`EXT_meshopt_compression` geometry, `KHR_mesh_quantization`, WebP textures) wherever the host's own glTF importer can't. Stock Blender, Godot and Unreal importers all reject current Meshy files without this step.
- **Auto-repairs broken UVs** (on by default): NaN values, wild outliers and collapsed triangles are fixed from neighbouring UVs, and valid Meshy UVs are never changed. Meshes with no UVs get generated ones: Smart UV Project in Blender, box projection elsewhere.

## ⭐ Unity: drop in `.meshy` and go

1. Install this package with Unity Package Manager.
2. Drop a real `.meshy` file anywhere under `Assets/`.
3. Unity registers `.meshy` as a custom Asset Pipeline type and builds the mesh, materials, textures, and skinning **directly -- no UnityGLTF or glTFast package required.** Built-in, URP and HDRP are supported.
4. Select the `.meshy` asset to see its **import settings** (scale factor, auto-repair UVs, colliders, mesh optimization), its status and analysis, and **Reimport** / **Validate** buttons in the Inspector.

Unity's Scripted Importer system is specifically intended for custom file extensions and automatically invokes the importer when supported files are added or changed.

### Unity menu

- **Tools > Meshy > Validate Installation**
- **Tools > Meshy > Validate All .meshy In Assets**
- **Tools > Meshy > Reimport Selected .meshy**
- **Tools > Meshy > Reimport All .meshy In Assets**
- **Tools > Meshy > Convert .meshy to GLB...**
- **Tools > Meshy > Convert All .meshy In Assets**
- **Tools > Meshy > Install UnityGLTF (Optional Fallback)**
- **Tools > Meshy > Support / Donate on Patreon**
- **Tools > Meshy > Show Welcome Again**

### Install Unity package

In Unity, open **Window > Package Manager**, choose **+ > Add package from git URL...**, and enter:

```text
https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity
```

You can also clone/download the repository and add the `unity` folder as a local package.

### Unity dependency

None. The importer natively builds meshes (including meshopt-compressed and quantized geometry), PBR materials, WebP textures and skinning. UnityGLTF is only used as a fallback, written as a `.glb` next to the file, when a payload needs a glTF extension the native builder doesn't implement. Its package ID is `org.khronos.unitygltf`.

## 🧩 How to get a `.meshy` file

See **[GETTING_A_MESHY_FILE.md](GETTING_A_MESHY_FILE.md)** for the full Chrome workflow:

**Chrome > F12 > Network > reload model > search `model` > save the actual model response > verify `MESHY.AI`.**

The exact request name or URL can change when Meshy updates its web application.

## 🎨 Blender

The main distribution contains:

`blender/Meshy Importer for Blender & Unity - Blender.zip`

### Blender 4.2+
Use **Preferences > Extensions > Install from Disk**. Blender introduced the Extensions system in 4.2 and continues to support legacy add-ons for compatibility.

### Blender 3.6–4.1
Use the legacy **Preferences > Add-ons > Install...** workflow.

Then use **File > Import > Meshy Model (.meshy)**.

The Blender extension reconstructs the GLB locally, decodes the meshopt geometry that Blender's own glTF importer rejects, and passes the result to Blender's native glTF importer. Import options: auto-repair UVs, remove unused material slots, save the decoded `.glb`.

## 🎮 Godot

Copy `godot/addons/meshy_importer` into your project and enable **Meshy Importer** under **Project Settings > Plugins**. `.meshy` files then import like any other 3D scene: reimport, Advanced Import Settings, instancing. See [`godot/README.md`](godot/README.md).

## 🛠️ Unreal Engine

Unzip `unreal/Meshy Importer for Unreal.zip` into your project's `Plugins/` folder and restart the editor. Use **Tools > Meshy > Import .meshy Files...**, or right-click a Content Browser folder and choose **Import .meshy Files Here...**. The plugin is Editor Python only, with no C++ build. See [`unreal/README.md`](unreal/README.md).

## 🧪 Command-line converter

The shared decoder (`core/python/meshy_core`) is plain Python 3.9+ with no dependencies:

```text
cd core/python
python -m meshy_core path/to/model.meshy                 # writes model.glb (plain glTF 2.0)
python -m meshy_core *.meshy -o out/                      # several files
python -m meshy_core model.meshy --raw                    # decrypt only, keep Meshy's extensions
```

The converted `.glb` has no required extensions: meshopt decoded, attributes dequantized, texture transforms baked, WebP converted to PNG, and UVs repaired. It passes the Khronos glTF validator and imports into any glTF tool. Pillow is used for WebP when installed; otherwise a built-in decoder is used.

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

See **COMPATIBILITY.md** for details.

## 🧰 Repository development

GitHub Actions runs the Python test-suite (Python 3.9 and 3.12), a Unity compile check against the real UnityEngine reference assemblies, a C#/GDScript/Python UV-repair parity check, and headless Blender and Godot imports of a synthetic `.meshy` payload. It also checks that versions agree across every host and that the committed ZIPs match their sources. See `CONTRIBUTING.md` before making changes.

## 🔒 Privacy

The `.meshy` decoder operates locally in every host. The importers do not upload model files to FISHHWB or a conversion server.

## ❤️ Support

If this saves you time, support continued development on Patreon:

https://www.patreon.com/cw/DedZed

## 🔎 Find this project

Useful search terms include **Meshy Importer**, **Meshy AI importer**, **.meshy Unity importer**, **.meshy Blender importer**, **.meshy Godot importer**, **.meshy Unreal importer**, **Meshy to Unity**, **Meshy to Blender**, **Meshy to Godot**, **Meshy to Unreal**, and **Meshy 3D model importer**. See **SEO_KEYWORDS.md**.

## Troubleshooting

See **TROUBLESHOOTING.md** for installation, dependency, invalid payload, and import troubleshooting.

## Community

GitHub: https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity

Discord: https://discord.gg/vCcsnX4HQP

## Disclaimer

This project is not affiliated with or endorsed by Meshy unless explicitly stated by the creator. The `.meshy` payload is a web application format and may change without notice.
