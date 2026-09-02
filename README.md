# Meshy Importer for Blender & Unity

**Version 1.2.0 — created and maintained by FISHHWB**

Import real Meshy `.meshy` model payloads into **Unity and Blender** with a local decoder. Unity users can drop `.meshy` files into `Assets` and let the Unity Asset Pipeline handle the source file automatically; Blender users can use **File > Import > Meshy Model (.meshy)**.

> **Important:** Meshy's normal Download workflow uses standard formats such as GLB, FBX, and OBJ. This importer is for the `.meshy` model payload obtained through the browser Network workflow. Do not rename a GLB to `.meshy`.

## ⭐ Unity: drop in `.meshy` and go

1. Install this package with Unity Package Manager.
2. Drop a real `.meshy` file anywhere under `Assets/`.
3. Unity registers `.meshy` as a custom Asset Pipeline type and builds the mesh, materials, textures, and skinning **directly -- no UnityGLTF or glTFast package required.**
4. Select the `.meshy` asset to see status, size, **Reimport**, and **Validate** controls in the Inspector.

Unity's Scripted Importer system is specifically intended for custom file extensions and automatically invokes the importer when supported files are added or changed.

### Unity menu

- **Tools > Meshy > Validate Installation**
- **Tools > Meshy > Validate All .meshy In Assets**
- **Tools > Meshy > Reimport Selected .meshy**
- **Tools > Meshy > Convert .meshy to GLB...**
- **Tools > Meshy > Convert All .meshy In Assets**
- **Tools > Meshy > Install UnityGLTF (Optional Fallback)**
- **Tools > Meshy > Support / Donate on Patreon**
- **Tools > Meshy > Show Welcome Again**

### Unity dependency

None, for the normal path -- the importer builds meshes, PBR materials/textures, and skinning natively. UnityGLTF is only installed automatically as a fallback when a specific payload uses a glTF extension the native builder doesn't implement yet (currently just `EXT_meshopt_compression`); its package ID is `org.khronos.unitygltf`.

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

The Blender extension reconstructs the GLB locally and passes it to Blender's native glTF importer.

## Compatibility

| Host | Supported | Recommended |
|---|---|---|
| Unity 2020.3 LTS | Yes | Use UnityGLTF 2.9.1-rc |
| Unity 2021.3 LTS | Yes | Yes |
| Unity 2022.3 LTS | Yes | Yes |
| Unity 6+ | Yes | Yes |
| Blender 3.6 LTS–4.1 | Yes | Legacy add-on workflow |
| Blender 4.2+ | Yes | Extension workflow |
| Blender 5.x | Yes | Yes |

See **COMPATIBILITY.md** for details.

## 🔒 Privacy

The `.meshy` decoder operates locally. The importer does not upload model files to FISHHWB or a conversion server.

## ❤️ Support

If this saves you time, support continued development on Patreon:

https://www.patreon.com/cw/DedZed

## 🔎 Find this project

Useful search terms include **Meshy Importer**, **Meshy AI importer**, **.meshy Unity importer**, **.meshy Blender importer**, **Meshy to Unity**, **Meshy to Blender**, and **Meshy 3D model importer**. See **SEO_KEYWORDS.md**.

## Troubleshooting

See **TROUBLESHOOTING.md** for installation, dependency, invalid payload, and import troubleshooting.

## Community

GitHub: https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity

Discord: https://discord.gg/vCcsnX4HQP

## Disclaimer

This project is not affiliated with or endorsed by Meshy unless explicitly stated by the creator. The `.meshy` payload is a web application format and may change without notice.
