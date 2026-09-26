# Troubleshooting

Every importer error says what went wrong and ends with a link to the matching section here. If this page doesn't help, use **Copy Diagnostics** in your engine's Meshy menu and paste it into a [bug report](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/issues/new?template=bug_report.yml) or on [Discord](https://discord.gg/vCcsnX4HQP). **Report a Bug** opens a pre-filled report for you.

## Wrong file errors

These mean the file isn't a `.meshy` model. The message says what the file actually is:

| Message starts with | What happened | Fix |
|---|---|---|
| "This is a web page (HTML)" | You saved the Meshy page instead of the model request. | Follow [the guide](GETTING_A_MESHY_FILE.md) again and save the model request's response (usually the largest `model` request). |
| "This is a JSON API response" | You saved a data request, not the model. | Pick a different `model` request; the right one starts with `MESHY.AI`. |
| "This is a plain GLB" | It's a normal `.glb` (from Meshy's Download button, or renamed). | Rename it back to `.glb` and import it with your engine's normal importer. You don't need this importer. |
| "This is an FBX file" / "This is an OBJ file" | It's a normal download renamed to `.meshy`. | Rename it back and import it directly. |
| "This is a ZIP archive" | Meshy downloads can come zipped. | Unzip it and import the `.glb`/`.fbx`/`.obj` inside. |
| "This is an image" | You saved a texture request. | Pick the model request instead. |
| "The .meshy file is cut off" | The download stopped early. | Save the response again and check the file size (usually several MB). |
| "The file is empty" | The download failed. | Save the response again. |
| "This is not a .meshy file" | Something else entirely. | Save the model request's response again; a real `.meshy` starts with `MESHY.AI`. |

## Meshy changed its web format

If the message says "Meshy decryption produced an invalid GLB header", the file does start with `MESHY.AI` but can't be decoded. Meshy has probably changed its (undocumented) web format.

1. Update the importer. Each Meshy menu has **Check for Updates**.
2. If you're already on the latest version, keep the original file and use **Report a Bug** with your engine and importer versions.

## Unity

### I dropped a `.meshy` file into Assets and nothing happened
1. Select the file. If the import failed, the Inspector says why, with an **Open Help** button.
2. **Tools > Meshy > Meshy Importer** lists every `.meshy` file that failed and why.
3. **Tools > Meshy > Help > Validate Installation** checks the package itself.
4. If it says "Native import not available for this file", the payload uses a glTF extension outside the native builder's coverage. This is rare: meshopt geometry and WebP textures are both handled natively. Run **Tools > Meshy > Advanced > Install UnityGLTF (Optional Fallback)** so that file can still import via the `.glb` fallback path.

### The model imports but materials/textures look off
The native importer repacks glTF's metallic/roughness texture channels for Unity and targets `Standard`, URP `Lit` or `HDRP/Lit` depending on your render pipeline. Normal maps are used as-is, with each mip level renormalized the way Unity's own "Normal map" texture setting does it. If a model still looks wrong compared with Blender, reimport it (see below), then report it with **Copy Diagnostics**.

### Blender shows the correct texture but Unity shows a flat, plain-colored blob
This is a stale import. Unity caches `.meshy` assets, so a model imported with an older version of this package can keep showing that older result. Blender imports fresh from the file every time, which is why the two look different. Run **Tools > Meshy > Reimport All .meshy In Assets**, or **Reimport Selected .meshy** for one file. Updates that change the import result also trigger this automatically the first time the Editor reloads.

### A high-poly model looks shredded or has missing triangles
Before 1.4.1, meshes over 65,535 vertices were corrupted by Unity's default 16-bit index format. Update to 1.4.1 or later; the update reimports existing assets automatically.

### My own `.glb` next to a `.meshy` file disappeared
Before 1.4.1, the importer deleted any `<Name>.glb` next to `<Name>.meshy`. From 1.4.1 it only deletes `.glb` files it wrote itself. Restore the file from version control or backup.

### HDRP: the model is pink or has no metallic/roughness detail
1.4.1 and later build `HDRP/Lit` materials; earlier versions used `Standard`, which renders pink in HDRP. From 1.5.0, metallic, roughness and occlusion are packed into HDRP's mask map. Reimport older assets to get it.

### UVs look different from before / the Inspector says UVs were repaired
**Auto-repair UVs** only changes UVs that are NaN, far outside the rest of the mesh's UVs, or on triangles collapsed to zero UV area. If you need the untouched UVs, choose the **Keep original data** preset in the Inspector and click **Apply**.

### The welcome dialog / update messages
The welcome dialog appears once, on first install. Updates only log a one-line note in the Console. **Tools > Meshy > Advanced > Show Welcome Again** brings the dialog back. The once-a-day update check can be turned off in **Tools > Meshy > Meshy Importer > Advanced**.

## Blender

- Blender 4.2+: install the ZIP with **Edit > Preferences > Get Extensions > ⌄ > Install from Disk**.
- Blender 3.6–4.1: use **Edit > Preferences > Add-ons > Install...**.
- Import with **File > Import > Meshy Model (.meshy)**, or drag `.meshy` files into the viewport (4.1+).
- When an import fails, a popup explains why with **Open Help**, **Copy Diagnostics** and **Report a Bug**. The same tools are under **Help > Meshy Importer**.
- **"Extension EXT_meshopt_compression is not available"**: you are on add-on 1.3.0 or older, or you imported a `.glb` directly. Update the add-on. To get a `.glb`, use **Save Decoded .glb** in the import panel or `python -m meshy_core model.meshy`, which write a plain `.glb`.
- **The model is too big or too small**: set **Scale** in the import panel.
- UV-repair results are stored on each imported object as `FISHHWB_Meshy_UV_*` custom properties. Choose the **Keep Original Data** preset to import the UVs untouched.
- **Check for Updates says online access is off**: Blender 4.2+ blocks add-ons from going online unless **Edit > Preferences > System > Network > Allow Online Access** is on. The importer itself works offline either way.

## Godot

- Enable the plugin under **Project > Project Settings > Plugins** after copying `addons/meshy_importer`. `.meshy` files show as unknown until it is enabled.
- Errors appear in the Output panel, prefixed with `Meshy Importer:`. **Project > Tools > Meshy Importer > Copy Diagnostics** includes the last error.
- **"required extension 'KHR_mesh_quantization' is not supported"**: you imported a raw decoded `.glb`, not the `.meshy`. Import the `.meshy` through the plugin, or convert with `python -m meshy_core`.
- **The model is too big or too small**: select the `.meshy` file, set **Nodes > Root Scale** in the Import dock and click **Reimport**.

## Unreal Engine

- **No Tools > Meshy menu:** make sure the plugin sits at `Plugins/MeshyImporter/MeshyImporter.uplugin`, then restart the editor. The plugin turns on **Python Editor Script Plugin** and **Editor Scripting Utilities** by itself; if they were turned off by hand, turn them back on in **Edit > Plugins**. Check the Output Log for `Meshy Importer`.
- **"glTF importer is not enabled" / "did not create any assets":** turn on the **Interchange** and **glTF Importer** plugins in **Edit > Plugins**, then restart.
- **The model is too big or too small:** use **Tools > Meshy > Import .meshy Files (Show Options)...** and set the scale in Unreal's import dialog.
- **Import is slow:** WebP textures are decoded in pure Python. Install Pillow into the editor's Python (see `unreal/README.md`).
- **No file dialog on Linux:** install `zenity` or `kdialog`, or use **Tools > Meshy > Auto-Import Inbox Folder**.
- **Auto-import didn't pick up a file:** files are imported once they stop growing (a few seconds after copying). Imported files move to `MeshyInbox/Imported`, failed ones to `MeshyInbox/Failed`; the Output Log says why.

## Support

- GitHub: https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity
- Discord: https://discord.gg/vCcsnX4HQP
- Patreon: https://www.patreon.com/cw/DedZed
