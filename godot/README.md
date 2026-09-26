# Meshy Importer for Godot

**Version 1.5.0 · Godot 4.2+**

Import Meshy `.meshy` model payloads straight into Godot as scenes. Decoding happens locally in the editor, with no extra tools or downloads.

## Install

1. Download **Meshy-Importer-Godot.zip** from the [latest release](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest) and unzip it into your project folder, so you get `res://addons/meshy_importer/`. (Or copy `addons/meshy_importer` from this repository.)
2. Open **Project → Project Settings → Plugins** and enable **Meshy Importer**. Until you do, `.meshy` files show up as unknown files.

## Use

Put `.meshy` files anywhere in your project, or drag them into the FileSystem dock. Godot imports them like any other 3D scene: double-click one to open it, drag it into a scene, and reimport with **Advanced Import Settings**.

The importer:
1. decrypts the `.meshy` container;
2. decodes Meshy's `EXT_meshopt_compression` geometry and turns `KHR_mesh_quantization` data into plain floats. Godot's glTF importer supports neither, so it would reject every current Meshy file;
3. optionally repairs broken UVs;
4. hands the result to Godot's own glTF importer. Materials, WebP textures, texture transforms, skins and animations come through as they would for any glTF file.

### Import options (Import dock)

- **meshy/auto_repair_uvs** (on): fixes broken UVs (NaN values, wild outliers, collapsed triangles) and leaves valid Meshy UVs alone. Meshes without UVs get a box projection. The number of repaired UVs is stored on the scene root as the `meshy_uv_bad_vertices` metadata.
- **meshy/save_decoded_glb** (off): also writes `<name>.glb` next to the `.meshy` file. An existing file is never overwritten.

All of Godot's usual scene import options still apply. In particular:

- **Nodes → Root Scale** scales the whole model.
- **Advanced Import Settings** (double-click the file) adds collision shapes, LODs and per-mesh options.

If a file can't be imported, the Output panel says why in plain words (for example "This is a web page (HTML), not the model") and links to the fix.

### Tools menu

**Project → Tools → Meshy Importer**: How Do I Get a .meshy File?, Troubleshooting, Documentation, Copy Diagnostics (includes the last error), Report a Bug..., Check for Updates, Discord. An **Update Available** entry appears at the top when a newer release exists.

### Update check

Once a day the plugin asks GitHub for the latest release and prints a line in the Output panel if it's newer. Only the public release record is downloaded; nothing about you or your project is sent. Turn it off in **Editor → Editor Settings → Meshy Importer → Check For Updates**.

## Compatibility

Tested headless on Godot 4.2.2 and 4.3. Godot 3.x is not supported.
