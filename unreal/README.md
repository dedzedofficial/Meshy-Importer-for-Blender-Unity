# Meshy Importer for Unreal Engine

**Version 1.5.0 · Unreal Engine 5.3+ · Editor only**

Import Meshy `.meshy` model payloads into Unreal Engine. Decoding happens locally in the editor's Python and needs no C++ compile.

## Install

1. Download **Meshy-Importer-Unreal.zip** from the [latest release](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest) (the same file is in this folder as `Meshy Importer for Unreal.zip`) and unzip it into your project's `Plugins/` folder, so you end up with `Plugins/MeshyImporter/MeshyImporter.uplugin`.
2. Restart the editor. The plugin turns on the **Python Editor Script Plugin** and **Editor Scripting Utilities** it needs by itself. If Unreal's glTF importer (**Interchange** / **glTF Importer**) is switched off, it tells you once, with the fix.
3. A **Meshy** section appears in the **Tools** menu.

Use the ZIP, not the `MeshyImporter/` folder from the repository. The ZIP bundles the shared `meshy_core` decoder.

## Use

- **Tools → Meshy → Import .meshy Files...**: pick one or more `.meshy` files. Each model is imported into `<current Content Browser folder>/<ModelName>`, or `/Game/Meshy/<ModelName>` if no folder is open.
- **Tools → Meshy → Import .meshy Files (Show Options)...**: same, but shows Unreal's own import options dialog first (scale, collision, materials and so on).
- **Right-click a folder in the Content Browser → Import .meshy Files Here...**: same, into that folder.
- **Tools → Meshy → Auto-Import Inbox Folder (On/Off)**: while on, any `.meshy` file you drop into `<Project>/MeshyInbox/` is imported into the current Content Browser folder within a few seconds. Imported files move to `MeshyInbox/Imported`, failed ones to `MeshyInbox/Failed`. The setting is remembered per project.
- **Tools → Meshy → Import Inbox Folder Now** / **Open Inbox Folder**: import the inbox once, or open it in your file browser.
- **Tools → Meshy → Meshy Importer Help →** How Do I Get a .meshy File?, Troubleshooting, Documentation, Validate Installation, Copy Diagnostics, Report a Bug..., Check for Updates, Check for Updates Daily (On/Off), Discord.

If a file can't be imported, the result dialog says why in plain words and offers to open the matching help page.

For every file, the plugin:
1. decrypts the `.meshy` container;
2. rewrites it as plain glTF 2.0: meshopt geometry decoded, quantized attributes converted to floats, texture transforms baked into the UVs, WebP textures converted to PNG. Unreal's glTF importer supports none of those Meshy features;
3. repairs broken UVs (NaN values, wild outliers, collapsed triangles) and leaves valid UVs alone;
4. imports the result through Unreal's own glTF (Interchange) pipeline, then deletes the temporary file.

### Speed

WebP textures are decoded in pure Python unless [Pillow](https://pypi.org/project/Pillow/) is installed in the editor's Python. A 2K texture takes around 10–20 seconds without Pillow and well under a second with it. To install Pillow, run this from the engine's `Engine/Binaries/ThirdParty/Python3/<platform>/` folder:

```text
python -m pip install pillow
```

## Limitations

- Dragging a raw `.meshy` file into the Content Browser is not supported. That needs a compiled C++ Interchange translator, which would mean a per-engine-version C++ build. Use **Auto-Import Inbox Folder** instead: drag files into `MeshyInbox/` in your file browser.
- Editor only. Nothing from this plugin is included in packaged games.

## Update check

Once a day the plugin asks GitHub for the latest release and logs a warning in the Output Log if it's newer. Only the public release record is downloaded; nothing about you or your project is sent. Turn it off with **Tools → Meshy → Meshy Importer Help → Check for Updates Daily (On/Off)**. Settings live in `<Project>/Saved/MeshyImporter/settings.json`.

## Scripting

```python
import meshy_unreal
meshy_unreal.import_meshy_files([r"C:/Downloads/model.meshy"], "/Game/Meshy")
meshy_unreal.start_inbox_watch()   # or stop_inbox_watch()
```
