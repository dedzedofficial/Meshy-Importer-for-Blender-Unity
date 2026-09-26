# Meshy Importer for Blender & Unity — Blender

**Version 1.5.0**

This Blender extension adds:

**File → Import → Meshy Model (.meshy)**

It reads the Meshy `.meshy` container locally, decrypts the encrypted GLB prefix, reconstructs the GLB, fixes the GLB total-length field, decodes Meshy's `EXT_meshopt_compression` geometry (which Blender's own glTF importer rejects), and hands the result to Blender's native glTF importer.

## Install

### Blender 4.2+
Use the included **`Meshy Importer for Blender & Unity - Blender.zip`** with:

**Edit → Preferences → Get Extensions → ⌄ (top right) → Install from Disk**

Then enable **Meshy Importer for Blender & Unity**.

The package includes a `blender_manifest.toml`, so it can be installed through Blender's modern Extensions workflow. Blender's extension format supports updating by installing a newer package over the existing extension.

### Older Blender versions
The add-on code retains `bl_info` for legacy installation. Use Blender's normal **Install from Disk / Install Add-on** workflow with the same ZIP.

## Use

**File → Import → Meshy Model (.meshy)**

Select one or more `.meshy` files. On Blender 4.1+ you can also drag `.meshy` files straight into the 3D Viewport or Outliner.

The add-on decrypts each file locally, creates a temporary GLB, imports it with Blender's native glTF importer, then removes the temporary file. The imported objects stay selected.

### Import options (file browser side panel)

- **Preset**: **Default** (repair UVs, remove unused slots) or **Keep Original Data** (import exactly what the file contains). Changing an option by hand shows **Custom**.
- **Scale** (1.0): uniform scale for the imported model.
- **Auto-repair UVs** (on): fixes broken UVs (NaN values, wild outliers, collapsed triangles) and leaves valid Meshy UVs alone. A mesh with no UVs, or mostly broken ones, gets a Smart UV Project. Results are stored on each object as `FISHHWB_Meshy_UV_*` custom properties.
- **Remove Unused Material Slots** (on): drops slots no face uses. The materials themselves are kept.
- **Save Decoded .glb** (off): also writes `<name>.glb` next to the `.meshy` file. An existing file is never overwritten.

After an import the status bar shows what came in, for example "Imported dragon.meshy: 3 mesh(es), 48,210 faces, 2 material(s); UVs fixed on 1 mesh(es)."

If a file can't be imported, a popup says why in plain words (for example "This is a web page (HTML), not the model") with **Open Help**, **Copy Diagnostics** and **Report a Bug...** buttons.

### Help menu

**Help → Meshy Importer**: How Do I Get a .meshy File?, Troubleshooting, Documentation, Copy Diagnostics, Report a Bug..., Check for Updates, Discord, Support on Patreon. An **Update Available** entry appears at the top when a newer release exists.

### Update check

Once a day, if Blender's **Allow Online Access** (Preferences → System → Network, Blender 4.2+) is on, the add-on asks GitHub for the latest release. Only the public release record is downloaded; nothing about you is sent. Turn it off in the add-on's preferences.

## Compatibility

- Blender 4.2+ as an extension; Blender 3.6–4.1 as a legacy add-on.
- `EXT_meshopt_compression` is decoded by the add-on, so any supported Blender version can import current Meshy files.
- Blender versions before 4.0 get WebP textures converted to PNG automatically.

## Security / privacy

The decoder runs locally. The `.meshy` model is not uploaded by this add-on. The only network access is the optional update check described above.

## Format

The current reverse-engineered format describes:
- `MESHY.AI` magic at bytes 0–7
- 12-byte nonce at bytes 10–21
- 8192-byte AES-CTR encrypted prefix
- 16-byte authentication/tag area
- plaintext remainder
- `EXT_meshopt_compression` in the resulting GLB

The fixed AES-256 key used by the current format is the literal 32-byte prefix documented by the reverse-engineered implementation.

## Support

Created by FISHHWB.

Repository:
https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity
