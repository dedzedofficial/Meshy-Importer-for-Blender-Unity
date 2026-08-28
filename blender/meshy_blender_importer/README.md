# Meshy Importer for Blender & Unity — Blender

**Version 1.1.0**

This Blender extension adds:

**File → Import → Meshy Model (.meshy)**

It reads the Meshy `.meshy` container locally, decrypts the encrypted GLB prefix, reconstructs the GLB, fixes the GLB total-length field, and hands the result to Blender's native glTF importer.

## Install

### Blender 4.2+
Use the included **`Meshy Importer for Blender & Unity - Blender.zip`** with:

**Edit → Preferences → Get Extensions → ⋯ → Install from Disk**

Then enable **Meshy Importer for Blender & Unity**.

The package includes a `blender_manifest.toml`, so it can be installed through Blender's modern Extensions workflow. Blender's extension format supports updating by installing a newer package over the existing extension.

### Older Blender versions
The add-on code retains `bl_info` for legacy installation. Use Blender's normal **Install from Disk / Install Add-on** workflow with the same ZIP.

## Use

**File → Import → Meshy Model (.meshy)**

Select a `.meshy` file. The add-on decrypts it locally, creates a temporary GLB, imports it with Blender's native glTF importer, then removes the temporary file.

## Compatibility

- Blender 4.2+.
- Blender 5.2+ is recommended for Meshy files that use `EXT_meshopt_compression`.

## Security / privacy

The decoder runs locally. The `.meshy` model is not uploaded by this add-on.

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
