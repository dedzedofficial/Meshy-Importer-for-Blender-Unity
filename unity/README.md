# Meshy Importer for Unity — v1.5.0

## What it does

Drop a real Meshy `.meshy` payload into your Unity project's `Assets` folder. The package automatically detects it, decodes it locally, and builds the mesh, materials, textures, and skinning directly -- **no UnityGLTF or glTFast required**. Nothing is written to disk as a `.glb`; everything imports as sub-assets of the `.meshy` file itself, the same way Unity's built-in FBX importer works.

### One-time setup

1. In Unity, open **Window > Package Manager**, click **+ > Add package from git URL...** and paste:
   `https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity`
2. Drop `model.meshy` into `Assets/`. That's it.

A short welcome appears once, on first install. After that, **Tools > Meshy > Meshy Importer** has everything in one window.

Meshy's real-world exports use `EXT_meshopt_compression` for geometry and `EXT_texture_webp` for textures -- both are decoded natively (from-scratch decoders verified byte-exact against the reference implementations), so this covers real `.meshy` files end-to-end. If a payload ever uses some other glTF extension the native builder doesn't implement, the importer automatically falls back to writing a `.glb` companion and importing it with whatever glTF package is installed -- run **Tools → Meshy → Advanced → Install UnityGLTF (Optional Fallback)** only if you hit that case. The importer only ever deletes a `.glb` it wrote itself; your own `.glb` files, and ones made with **Convert**, are left alone.

### Import settings

Select a `.meshy` asset to see its settings in the Inspector. Click **Apply** to reimport with them.

- **Preset**: sets the options below in one go. **Default** (repair UVs, optimize meshes), **Game-ready** (Default plus colliders) or **Keep original data** (no UV repair or mesh reordering). Changing an option by hand shows **Custom**.
- **Scale Factor**: a uniform scale on the imported model's root.
- **Auto-repair UVs** (on): fixes broken UVs (NaN values, wild outliers, collapsed triangles) without changing valid Meshy UVs. Meshes with no UVs get a box projection. The Inspector shows how many UVs were repaired.
- **Generate Colliders** (off): adds a `MeshCollider` to every static mesh.
- **Optimize Meshes** (on): reorders vertex/index data for GPU cache efficiency. The mesh looks the same either way.

Meshes over 65,535 vertices use 32-bit indices automatically.

### Render pipelines

Materials are built for the pipeline the project uses:
- **Built-in:** `Standard`.
- **URP:** `Universal Render Pipeline/Lit`.
- **HDRP:** `HDRP/Lit`, with base colour, normal and emission maps, and metallic, occlusion and smoothness packed into HDRP's mask map.

Normal maps are renormalized at every mip level, the same way Unity's "Normal map" texture setting does it, so distant surfaces keep their detail.

## Getting the `.meshy` payload

Meshy's regular Download options give you `.glb`/`.fbx`/`.obj` files, which Unity imports without this package. The `.meshy` file is saved from the Meshy website with Chrome's DevTools; see the [step-by-step guide](../GETTING_A_MESHY_FILE.md).

If you drop in the wrong file (a saved web page, a JSON response, a renamed FBX/GLB...), the Inspector says exactly what it is and how to fix it.

## Menus and tools

**Tools → Meshy → Meshy Importer** opens one window with: the files that failed to import and why, **Reimport All**, **Convert All to .glb**, help links, **Copy Diagnostics**, **Report a Bug**, and update-check settings.

- **Tools → Meshy → Reimport Selected `.meshy`** / **Reimport All `.meshy` In Assets**: rebuild with the current importer. Use this after updating if an already-imported model doesn't reflect a fix.
- **Tools → Meshy → Convert `.meshy` to GLB...** / **Convert All `.meshy` In Assets**: export a standalone `.glb` (e.g. for another engine).
- **Tools → Meshy → Help →** How Do I Get a .meshy File?, Troubleshooting, Documentation, Validate Installation, Copy Diagnostics, Report a Bug..., Check for Updates, Discord, Support on Patreon, About.
- **Tools → Meshy → Advanced →** Validate All `.meshy` In Assets, Install UnityGLTF (Optional Fallback), Show Welcome Again.

When an import fails, the Inspector shows the reason with **Open Help**, **Copy Diagnostics** and **Report a Bug...** buttons. **Report a Bug...** opens a GitHub issue pre-filled with your versions and the error.

### Update check

Once a day the package asks GitHub for the latest release and, if it's newer, logs one line in the Console and shows a banner in the Meshy Importer window. Only the public release record is downloaded; nothing about you or your project is sent. Turn it off in **Tools → Meshy → Meshy Importer → Advanced**.

## Support

Patreon: https://www.patreon.com/cw/DedZed

GitHub: https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity

## Unity compatibility

Unity 2020.3 LTS, 2021.3 LTS, 2022.3 LTS and Unity 6+ are supported by the native importer with no other packages. The optional UnityGLTF fallback uses 2.9.1-rc on 2020.3 and 2.21.0 on newer versions.

For the best supported path, use a current Unity LTS release. See `../COMPATIBILITY.md` for details.
