# Changelog

## 1.1.1

### Fixes
- Reverted an accidental `bl_info` minimum-version bump (was requiring Blender 5.2+, contradicting the documented 3.6 LTS+ legacy support and the Extension manifest's 4.2.0 floor). Blender 5.2+ remains a *recommendation* for `EXT_meshopt_compression` payloads, not a hard requirement.
- Restored the Blender Help-menu (Support / Documentation) operators that CHANGELOG 1.1.0 already documented but had been dropped from source.
- Added `blender_manifest.toml` as a tracked source file (previously only hand-embedded in the shipped ZIP, which let it silently drift from `bl_info`) and rebuilt the distributed ZIP from current source so the shipped package matches the repo.
- Updated the Unity Package Manager git dependency pin, which had been stuck 2 commits behind and was missing `MeshyScriptedImporter.cs`, `MeshyAssetPostprocessor.cs`, `MeshySourceAsset.cs`, and `MeshySourceAssetEditor.cs` entirely — the reason `.meshy` files were importing as plain `DefaultAsset` instead of triggering automatic GLB reconstruction.

## 1.1.0

### Unity workflow
- Added a native Unity Scripted Importer registration for `.meshy`, so the file appears as a first-class custom asset in the Project window.
- Kept automatic GLB reconstruction as the compatibility bridge for UnityGLTF.
- Added a custom `.meshy` Inspector with status, file size, Reimport, Open Folder, and Validate controls.
- Added automatic reimport when `.meshy` files are added, replaced, or moved.
- Added cleanup of generated GLB companions when the source `.meshy` is deleted or moved.
- Added first-run setup guidance.
- Added Validate Installation, Validate All, Reimport Selected, Show Welcome Again, and Patreon support actions.
- Kept manual conversion tools as a fallback.

### Blender workflow
- Kept the Blender ZIP bundled inside the main distribution.
- Updated the bundled Blender documentation for legacy 3.6–4.1 and modern 4.2+ Extension workflows.
- Added Blender Help-menu links for documentation and Patreon support.

### Documentation / discoverability
- Added `GETTING_A_MESHY_FILE.md` with the Chrome DevTools Network workflow.
- Added `TROUBLESHOOTING.md`.
- Expanded `SEO_KEYWORDS.md` and Unity package keywords.
- Added clearer compatibility and dependency guidance.
- Kept version **1.1.0**.
