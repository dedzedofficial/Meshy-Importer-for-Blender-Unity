# Meshy Importer for Blender

The ready-to-install Blender ZIP is included in this repository:

`Meshy Importer for Blender & Unity - Blender.zip`

## Supported Blender versions

- **Blender 3.6 LTS through 4.1:** install the ZIP as a legacy add-on.
- **Blender 4.2+:** install the ZIP from **Edit > Preferences > Get Extensions > ⌄ > Install from Disk**.
- **Blender 5.x:** supported.

Blender 4.2+ is recommended for current Meshy payloads.

Import with **File > Import > Meshy Model (.meshy)**, or drag `.meshy` files into the viewport (Blender 4.1+). Help, diagnostics and the update check are under **Help > Meshy Importer**. See [`meshy_blender_importer/README.md`](meshy_blender_importer/README.md) for the options.

The ZIP is built from `meshy_blender_importer/` plus the shared `core/python/meshy_core` package by `python tools/build_zips.py`. Do not edit the ZIP by hand.

See `../COMPATIBILITY.md` for the full compatibility notes.

Support the project: https://www.patreon.com/cw/DedZed
