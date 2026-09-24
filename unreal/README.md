# Meshy Importer for Unreal Engine

**Version 1.4.1 · Unreal Engine 5.3+ · Editor only**

Import Meshy `.meshy` model payloads into Unreal Engine. Decoding happens locally in the editor's Python and needs no C++ compile.

## Install

1. Unzip **`Meshy Importer for Unreal.zip`** into your project's `Plugins/` folder, so you end up with `Plugins/MeshyImporter/MeshyImporter.uplugin`.
2. Restart the editor. It enables the required **Python Editor Script Plugin** and **Editor Scripting Utilities** automatically. If not, enable them under **Edit → Plugins**.
3. A **Meshy** section appears in the **Tools** menu.

Use the ZIP, not the `MeshyImporter/` folder from the repository. The ZIP bundles the shared `meshy_core` decoder.

## Use

- **Tools → Meshy → Import .meshy Files...**: pick one or more `.meshy` files. Each model is imported into `<current Content Browser folder>/<ModelName>`, or `/Game/Meshy/<ModelName>` if no folder is open.
- **Right-click a folder in the Content Browser → Import .meshy Files Here...**: same, into that folder.
- **Tools → Meshy → Import Inbox Folder**: imports every `.meshy` file in `<Project>/MeshyInbox/`. **Open Inbox Folder** opens that folder. Use this on systems without a native file dialog.
- **Tools → Meshy → Validate Meshy Importer**: shows the version, which WebP decoder is in use, and the inbox path.

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

- Dragging a raw `.meshy` file into the Content Browser is not supported. That needs a compiled C++ Interchange translator; use the menu entries instead.
- Editor only. Nothing from this plugin is included in packaged games.

## Scripting

```python
import meshy_unreal
meshy_unreal.import_meshy_files([r"C:/Downloads/model.meshy"], "/Game/Meshy")
```
