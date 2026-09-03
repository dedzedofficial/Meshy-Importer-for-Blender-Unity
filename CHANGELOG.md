# Changelog

## 1.3.1

### Unity: models no longer render black in dim/unlit scenes
- Diagnosed a real user report of imported models appearing solid black. Root cause: a
  physically-based metallic/roughness material genuinely can render black under a scene
  with little ambient light and no reflection probes -- confirmed this by showing the
  same test model correctly textured from a camera angle facing its lit side, and by
  showing the scene's own pre-existing objects were equally dark under the same camera.
  The imported texture data itself was already verified byte-correct (1.3.0's WebP work).
- Since a `.meshy` drag-and-drop importer can't assume the destination scene has proper
  lighting/reflection probes set up, `GetOrBuildMaterial` now always feeds the base color
  texture back in as emission (at unit strength) when the glTF material doesn't already
  define a real emissive channel. This puts a floor under the material's brightness at
  exactly the base color texture, so the model is never invisible in a dim scene; a
  properly lit scene still adds direct/specular highlights on top as before.

## 1.3.0

### Unity: native WebP textures and meshopt geometry, zero remaining dependencies for real-world exports
- Added a from-scratch VP8 lossy decoder (`MeshyWebpVp8.cs` + supporting files), ported from and verified byte-exact against the reference libwebp decoder, so `EXT_texture_webp` textures (Meshy's default texture format) decode natively -- no UnityGLTF, no glTFast, no system WebP codec.
- Added a from-scratch meshopt decoder (`MeshyMeshopt.cs`), ported from and verified byte-exact against the reference meshoptimizer decoder, so `EXT_meshopt_compression` geometry decodes natively.
- Between this and 1.2.0's native glTF builder, a real Meshy export using `KHR_mesh_quantization` + `EXT_meshopt_compression` + `EXT_texture_webp` together -- the combination Meshy's exporter actually produces -- now imports fully natively end-to-end, with the `.glb` + UnityGLTF/glTFast fallback reserved for extensions outside that common case.

## 1.2.0

### Unity: native import, no glTF package required
- Added a dependency-free glTF/GLB reader (`MeshyMiniJson.cs`, `MeshyGltfBuilder.cs`) that builds Unity `Mesh`, `Material`, `Texture2D`, and `GameObject` objects directly from the decrypted `.meshy` payload -- no UnityGLTF, no glTFast, no `.glb` companion file for the normal path.
- Covers triangle meshes (positions/normals/tangents/UV0+UV1/vertex color), pbrMetallicRoughness materials with embedded textures (base color, metallic/roughness with correct channel repacking for Unity's `_MetallicGlossMap`, normal, occlusion, emissive + `KHR_materials_emissive_strength`), alpha modes, double-sided flag, and skinning (joints/weights/bind poses) for rigged models.
- Automatically targets URP `Lit` or the built-in `Standard` shader depending on the project's render pipeline.
- Files that use a glTF extension the native builder doesn't implement yet (chiefly `EXT_meshopt_compression`) automatically fall back to the previous `.glb` + UnityGLTF/glTFast path for just that file, with a clear console warning, instead of failing.
- `MeshyScriptedImporter` now builds everything as sub-assets of the `.meshy` import itself (bumped importer version forces existing assets to reimport); `MeshyAssetPostprocessor` is trimmed down to cleaning up stale generated `.glb` files. The Project-window inspector for a `.meshy` file is now a proper `ScriptedImporterEditor`.
- "Install UnityGLTF" is now clearly optional/fallback-only in the menu, first-run dialog, and Validate Installation report.

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
