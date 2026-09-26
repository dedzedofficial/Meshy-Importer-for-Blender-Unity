# The five workflow upgrades — still version 1.5.1

Install `unity/package.json` as described in UPGRADE_1.5.1.md, then select a `.meshy`
asset in Unity. These upgrades are for the Unity importer. Blender, Godot and Unreal
keep the behavior already included in this release.

## Material overrides

The Inspector has a **Material Overrides** section with one field per imported
material slot. Assign a standalone `.mat` asset and press **Apply**. Empty fields
use the generated material. Your choices are serialized with this model's importer
settings, so they survive ordinary reimports and source-file updates.

Uniquely named materials match by source name, even when their order changes.
Duplicate or unnamed materials match by slot index and name; if an exporter
reorders duplicate-name slots, review those choices manually. Renamed/removed slots
are reported and retained until you choose **Remove Unmatched Material Choices**.
No guess is made about a renamed material.

Use a saved `.mat`, not a generated material embedded in a `.meshy` or FBX. Materials
that refer back to the source model's generated textures are rejected to prevent a
circular import dependency. Use the editable-copy action below to create separate
textures and materials first.

A missing assigned material produces an error instead of silently discarding your
choice. Restore the missing asset, choose a replacement, or use **Use Generated
Material** for that slot and Apply.

Overrides apply to native `.meshy` imports. When a file uses the GLB fallback,
configure materials/settings on that GLB's external importer instead.

## Protected editable assets

After a successful native import, click **Create Editable Copy (Prefab + Assets)**.
It creates a uniquely named folder under `Assets/MeshyCustom`, with copies of the
imported meshes, textures and materials plus an independent prefab. The copied
materials' texture references and the prefab's mesh/material references are rewired
to the copies. Existing external material overrides remain external references.
The command checks that the saved prefab has no dependency on the source `.meshy`
before reporting success.

Edit these copied assets freely. Source reimports do not regenerate them. Repeating
the action creates a new folder instead of overwriting previous edits. To use a
copied material on the original imported model too, assign its `.mat` in Material
Overrides and Apply. The independent copy deliberately does not receive future
source changes automatically.

## Remembered settings and recovery

Applied scale, UV repair, optimisation, collider settings and material choices stay
in the model's Unity `.meta` file. Move assets inside Unity or move their `.meta`
files alongside them when working outside the Editor.

After a successful native import, a recovery snapshot is also written to
`ProjectSettings/MeshyImporter/LastSuccessful/<asset-guid>.json`. This survives
ordinary Unity Library/cache deletion and moves that retain the source GUID.
Include these files in project backups or version control if you want recovery on
another machine. They contain settings and asset identifiers, not model geometry.

**Restore Last Successful Settings** resolves all saved material references first,
then asks to restore and reimport. Failed imports and GLB fallback generation do
not replace the last successful native snapshot. If a saved material is missing,
restore stops before changing settings. Deleting a model's `.meta` creates a new
GUID and breaks the association with its old snapshot.

## Preflight scan

Every import performs a lightweight structural scan after decoding the container
and before constructing Unity model objects. Use **Run Preflight** to see the scan
on demand, or expand **Preflight Report** in the Inspector. Batch validation uses
this scan too.

The scan checks GLB headers/chunk ranges, embedded buffer and accessor ranges,
reference indices and node cycles/multiple parents. It flags large declared geometry
and payload sizes. Unsupported required extensions, animation clips, morph targets,
sparse accessors, non-triangle topology and external image/buffer files are routed
to the external GLB importer rather than silently dropped by the native builder.
External sidecar files must actually exist alongside the generated GLB.

This is a structural preflight, not a full glTF conformance/security validator or a
promise that an external importer supports every feature. Image decoding and native
geometry building may still discover additional errors. Large models are warned
about without automatically reducing their quality.

## Import summary

The Inspector now combines existing geometry/material counts with imported model
dimensions in Unity units, unique bones used, largest generated texture dimensions
and an estimated uncompressed RGBA texture footprint including mipmaps.

The texture figure excludes external material overrides, driver overhead and CPU
copies; it is not a measured VRAM figure. Skinned bounds are the imported/rest-state
bounds. GLB fallback models must be inspected through their external importer.

The release version remains **1.5.1**. Only the internal ScriptedImporter cache
revision increases to 9 so previously cached assets receive the workflow changes.
