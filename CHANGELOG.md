# Changelog

## 1.3.4

### Unity: fixed the texture looking nothing like the correct Blender import -- KHR_texture_transform was detected but never applied
- After the black-rendering and orange-emission bugs were fixed, the model in Unity was
  still visibly wrong compared to the same file imported in Blender: Blender showed a
  richly detailed textured surface, Unity showed a flat, plain-colored blob with none of
  the pattern detail.
- Root cause: Meshy's exported materials carry a `KHR_texture_transform` extension on
  their base color / metallic-roughness / normal texture references (offset + scale, no
  rotation in this file). This extension is Meshy's compensation for quantizing
  `TEXCOORD_0` to a normalized `UNSIGNED_SHORT`: the raw stored UVs only span a tiny
  corner of [0,1] (about a 1/16 x 1/16 patch), and the transform's ~16x scale is what maps
  them back out across the full texture at render time -- exactly like the node
  translation/scale used for quantized `POSITION` data (see 1.3.3), but for UVs instead of
  vertices. `MeshyGltfBuilder.cs` already listed `KHR_texture_transform` as a supported
  extension, but the comment next to it said "detected but not applied; harmless to ignore
  for a first pass" -- that assumption was wrong: without applying it, every triangle
  sampled from the same tiny corner of the base color texture, which is what produced the
  flat, mostly-uniform color instead of the actual pattern.
- Fixed by resolving the transform (offset/scale/rotation) from whichever of the
  material's texture references defines it first -- baseColorTexture, then
  metallicRoughnessTexture, normalTexture, emissiveTexture, occlusionTexture, in that
  order, since Meshy puts an identical transform on all of them -- and applying it to the
  raw glTF-space UV before the existing top-left-to-bottom-left V flip, matching how a
  standard glTF importer (glTFast/UnityGLTF) samples the texture. `TEXCOORD_1` is handled
  the same way when a texture reference targets it via `texCoord: 1`.
- Verified against the raw glTF data extracted from the `.meshy` container: the accessor's
  raw (quantized, meshopt-compressed) `TEXCOORD_0` decodes to U/V both confined to
  `[0, 0.0625]`, and applying the material's `KHR_texture_transform` (scale ~15.99, offset
  ~0.00003/0.000166) maps that up to `U:[0,0.999] V:[0.0002,0.9998]` -- matching the
  reimported mesh's actual `mesh.uv` exactly. Re-imported the model in the Editor and
  confirmed visually on the same placed scene object used for the 1.3.3 verification: it
  now shows the same varied teal/white/gold pattern as the Blender reference instead of a
  flat blob.

## 1.3.3

### Unity: fixed the actual cause of models rendering black -- a floating-point precision bug, not lighting
- After 1.3.2 restored plain PBR (matching glTFast/UnityGLTF), the model still rendered
  solid black under any light, from any angle, at any brightness -- confirmed with a
  bright point light placed right next to it, which produced no visible response
  whatsoever, while the exact same light next to another object in the same scene lit it
  normally.
- Root cause found by systematic elimination: Meshy exports pair `KHR_mesh_quantization`
  with a per-node "dequantization" scale, often around 1e-4 to 1e-5, that converts a mesh
  authored in large integer-ish local coordinates (thousands of units) back down to
  real-world size. Left as a literal Unity `Transform.localScale`, that combination --
  huge local vertex magnitudes times a near-zero scale -- is numerically pathological for
  Unity's realtime lighting: position math, UVs, and `Renderer.bounds` all come out
  correct (which is why the geometry and textures looked fine), but the world-space
  *normal* the lighting pipeline computes collapses to zero, so every light's contribution
  is zero regardless of brightness or angle. Reproduced this in isolation by putting a
  plain built-in sphere mesh on the same transform: lit normally at scale 1e-4, went
  solid black at the real ~6e-5 scale, with nothing else changed.
- Fixed at the root in `MeshyGltfBuilder.BuildMeshOnNode`: a non-skinned mesh node's own
  local translation/rotation/scale is now baked directly into the vertex, normal, and
  tangent data (with correct non-uniform-scale normal transform and winding-flip handling
  for mirrored scales), and the node's Transform is reset to identity afterward. Parent
  transforms are untouched. Skinned meshes are left as-is, since their vertices already
  live in a shared skeleton-relative space driven by bone transforms rather than this
  node's own TRS.
- Also cleaned up a separate, unrelated issue found while diagnosing this: a stale,
  separately-extracted material file could end up referenced by a placed prefab instance
  instead of the current import output, silently masking any material-side fixes. Not a
  code bug, but worth knowing about if a placed instance ever seems to ignore a reimport.

## 1.3.2

### Unity: reverted the 1.3.1 emission fallback -- it produced wrong colors
- 1.3.1's "feed the base color texture back in as emission" fallback (added so models
  wouldn't go invisible in dim scenes) had a real bug: stacking the texture on top of
  itself as emission pushed already-bright channels past 1.0 while a comparatively dark
  channel didn't, desaturating and hue-shifting the result toward orange -- visibly wrong
  compared to the actual texture.
- Reverted to plain PBR material construction with no synthetic emission unless the glTF
  file defines a real emissive channel, matching exactly what a standard glTF importer
  (glTFast/UnityGLTF) would produce for the same file. A model that renders dim in a scene
  with little ambient light and no reflection probes is correct PBR behavior in that
  scene -- a real glTFast import of the same file would look the same way there, so this
  importer shouldn't diverge from that just to look brighter.

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
