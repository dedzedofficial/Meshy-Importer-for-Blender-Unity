# Meshy Importer for Unity — v1.2.0

## What it does

Drop a real Meshy `.meshy` payload into your Unity project's `Assets` folder. The package automatically detects it, decodes it locally, and builds the mesh, materials, textures, and skinning directly -- **no UnityGLTF or glTFast required**. Nothing is written to disk as a `.glb`; everything imports as sub-assets of the `.meshy` file itself, the same way Unity's built-in FBX importer works.

### One-time setup

1. Install this package from Unity Package Manager.
2. Drop `model.meshy` into `Assets/`. That's it.

Meshy's real-world exports use `EXT_meshopt_compression` for geometry and `EXT_texture_webp` for textures -- both are decoded natively (from-scratch decoders verified byte-exact against the reference implementations), so this covers real `.meshy` files end-to-end. If a payload ever uses some other glTF extension the native builder doesn't implement, the importer automatically falls back to writing a `.glb` companion and importing it with whatever glTF package is installed -- run **Tools → Meshy → Install UnityGLTF (Optional Fallback)** only if you hit that case.

## Getting the `.meshy` payload

Meshy's regular download options are not the same thing as the `.meshy` container used by this importer. To obtain the payload used here, open the model in Chrome, press **F12**, open **Network**, reload the model, and search/filter for **`model`**. Save the actual model response as `.meshy`.

Do not rename a GLB/FBX/OBJ/HTML/JSON response to `.meshy`. A valid payload begins with `MESHY.AI`.

## Manual tools

- **Tools → Meshy → Convert `.meshy` to GLB...** -- export a standalone `.glb` for use outside this importer (e.g. another engine).
- **Tools → Meshy → Convert All `.meshy` In Assets**
- **Tools → Meshy → Install UnityGLTF (Optional Fallback)** -- only needed if a payload uses a glTF extension outside the native builder's coverage (meshopt geometry and WebP textures are both handled natively).
- **Tools → Meshy → Support / Donate on Patreon**

## Support

Patreon: https://www.patreon.com/cw/DedZed

GitHub: https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity

## Unity compatibility

- Unity 2020.3 LTS: supported with UnityGLTF 2.9.1-rc
- Unity 2021.3 LTS: supported with UnityGLTF 2.21.0
- Unity 2022.3 LTS: supported with UnityGLTF 2.21.0
- Unity 6+: supported with UnityGLTF 2.21.0

For the best supported path, use a current Unity LTS release. See `../COMPATIBILITY.md` for details.
