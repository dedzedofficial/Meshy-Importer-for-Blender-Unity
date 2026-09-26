#if UNITY_2020_2_OR_NEWER
using System;
using System.IO;
using FISHHWB.MeshyImporter;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    // IMPORTANT: bump the version integer below whenever a change to the decode/build
    // pipeline (MeshyGltfBuilder, MeshyUvRepair, MeshyWebpVp8, MeshyMeshopt, ...) alters
    // what gets baked into an already-imported .meshy asset. With AllowCaching = true,
    // Unity only reimports a cached asset when its source file, its import settings or
    // this number changes -- editing the importer's C# alone does nothing to .meshy files
    // a user already imported under an older version of this package. (1.3.1-1.3.4
    // shipped fixes that never reached existing assets because this was not bumped.)
    // 6 = 1.4.1: 32-bit indices for large meshes, UV auto-repair, HDRP materials.
    // 7 = 1.5.0: HDRP mask maps (metallic/AO/smoothness), renormalized normal-map mips.
    // 8 = 1.5.1: unique subasset IDs, primitive nodes, explicit fallback main object.
    // 9 = 1.5.1 workflow update: preflight, material maps and detailed summary.
    [ScriptedImporter(9, new[] { "meshy" }, AllowCaching = true)]
    public sealed class MeshyScriptedImporter : ScriptedImporter
    {
        [Tooltip("Uniform scale applied to the imported model's root.")]
        [SerializeField] private float scaleFactor = 1f;

        [Tooltip("Add a MeshCollider to every static (non-skinned) mesh.")]
        [SerializeField] private bool generateColliders;

        [Tooltip("Reorder vertex/index data for GPU cache locality (MeshUtility.Optimize). The mesh looks identical.")]
        [SerializeField] private bool optimizeMeshes = true;

        [Tooltip("Fix broken UVs (NaN, wild outliers, collapsed triangles) and give UV-less meshes a box projection. Valid UVs are never changed.")]
        [SerializeField] private bool autoRepairUvs = true;

        [SerializeField] private MeshyMaterialRemap[] materialRemaps = new MeshyMaterialRemap[0];

        internal MeshySettingsSnapshot CaptureSettings()
        {
            var snapshot = new MeshySettingsSnapshot { scale = scaleFactor, colliders = generateColliders,
                optimize = optimizeMeshes, repairUvs = autoRepairUvs };
            foreach (var map in materialRemaps ?? new MeshyMaterialRemap[0])
                if (map != null && map.material != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(map.material, out string guid, out long id))
                    snapshot.materials.Add(new MeshySettingsSnapshot.MaterialReference { key = map.key, guid = guid, fileId = id });
            return snapshot;
        }

        internal void RestoreSettings(MeshySettingsSnapshot snapshot)
        {
            // Resolve everything before altering any settings, so missing materials cannot
            // produce a partially restored model.
            var maps = new System.Collections.Generic.List<MeshyMaterialRemap>();
            foreach (var reference in snapshot.materials ?? new System.Collections.Generic.List<MeshySettingsSnapshot.MaterialReference>())
                maps.Add(new MeshyMaterialRemap { key = reference.key, material = MeshySettingsMemory.Resolve(reference), materialGuid = reference.guid, materialFileId = reference.fileId });
            scaleFactor = snapshot.scale; generateColliders = snapshot.colliders;
            optimizeMeshes = snapshot.optimize; autoRepairUvs = snapshot.repairUvs;
            materialRemaps = maps.ToArray();
        }

        internal static string MaterialProblem(Material material, string sourcePath)
        {
            if (material == null) return null;
            string path = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(path)) return "Choose a saved material asset, not a scene-only material.";
            if (path.EndsWith(".meshy", StringComparison.OrdinalIgnoreCase))
                return "Create editable materials first; a generated .meshy subasset is rebuilt during import.";
            // Standalone .mat files prevent hidden cross-model dependency cycles.
            if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                return "Choose a standalone .mat asset. Extract embedded materials before remapping.";
            foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                if (string.Equals(dependency, sourcePath, StringComparison.OrdinalIgnoreCase))
                    return "This material references this model's generated assets. Create an editable copy of its textures first.";
            return null;
        }

        private void ApplyMaterials(MeshyGltfBuilder.BuildResult build, AssetImportContext ctx)
        {
            var replacements = new System.Collections.Generic.Dictionary<Material, Material>();
            foreach (var map in materialRemaps ?? new MeshyMaterialRemap[0])
            {
                if (map == null || map.material == null || string.IsNullOrEmpty(map.key)) continue;
                if (!build.MaterialSlots.TryGetValue(map.key, out var original))
                { build.Notes.Add("A saved material slot no longer matches this source: " + map.key + "."); continue; }
                string problem = MaterialProblem(map.material, ctx.assetPath);
                if (problem != null) throw new InvalidOperationException(problem);
                ctx.DependsOnSourceAsset(AssetDatabase.GetAssetPath(map.material));
                replacements[original] = map.material;
            }
            foreach (var renderer in build.Root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && replacements.TryGetValue(materials[i], out var replacement)) materials[i] = replacement;
                renderer.sharedMaterials = materials;
            }
        }

        private void RecordWorkflow(MeshyGltfBuilder.BuildResult build, MeshySourceAsset meta)
        {
            var keys = new System.Collections.Generic.List<string>(build.MaterialSlots.Keys);
            keys.Sort(StringComparer.Ordinal);
            var labels = new string[keys.Count]; var originals = new Material[keys.Count];
            for (int i = 0; i < keys.Count; i++) { originals[i] = build.MaterialSlots[keys[i]]; labels[i] = originals[i].name; }
            var bones = new System.Collections.Generic.HashSet<Transform>();
            foreach (var skin in build.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var bone in skin.bones) if (bone != null) bones.Add(bone);
            bool haveBounds = false; Bounds bounds = new Bounds();
            foreach (var renderer in build.Root.GetComponentsInChildren<Renderer>(true))
            { if (!haveBounds) { bounds = renderer.bounds; haveBounds = true; } else bounds.Encapsulate(renderer.bounds); }
            long textureBytes = 0; int maxWidth = 0, maxHeight = 0;
            foreach (var asset in build.SubAssets)
                if (asset is Texture2D texture)
                {
                    // Deliberately an uncompressed RGBA estimate; not claimed as actual VRAM.
                    long total = 0; int w = texture.width, h = texture.height;
                    for (int mip = 0; mip < texture.mipmapCount; mip++)
                    { total += (long)w * h * 4; w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
                    textureBytes += total;
                    if ((long)texture.width * texture.height > (long)maxWidth * maxHeight)
                    { maxWidth = texture.width; maxHeight = texture.height; }
                }
            meta.SetWorkflow(keys.ToArray(), labels, originals, JsonUtility.ToJson(CaptureSettings(), true),
                haveBounds ? bounds.size : Vector3.zero, bones.Count, textureBytes, maxWidth, maxHeight);
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var meta = ScriptableObject.CreateInstance<MeshySourceAsset>();
            long size = 0;
            string status;
            string generatedGlb = null;
            string assetName = Path.GetFileNameWithoutExtension(ctx.assetPath);
            bool hasMainObject = false;

            try
            {
                string fullPath = MeshyPaths.ProjectPath(ctx.assetPath);
                if (File.Exists(fullPath)) size = new FileInfo(fullPath).Length;

                byte[] glb = MeshyImporterMenu.DecodeFileForEditor(ctx.assetPath);

                var preflight = MeshyPreflight.Scan(glb, MeshyGltfBuilder.SupportedExtensions);
                meta.SetPreflight(preflight.Text);
                var sourceMaterials = MeshyMiniJson.GetArray(preflight.Root, "materials") ?? new System.Collections.Generic.List<object>();
                var catalogKeys = new System.Collections.Generic.List<string>();
                var catalogLabels = new System.Collections.Generic.List<string>();
                foreach (int index in preflight.MaterialIndices)
                {
                    catalogKeys.Add(MeshyPreflight.MaterialKey(sourceMaterials, index));
                    catalogLabels.Add(index < 0 ? "Default" : MeshyMiniJson.GetString(MeshyMiniJson.AsObject(sourceMaterials[index]), "name", "Material " + index));
                }
                meta.SetMaterialCatalog(catalogKeys.ToArray(), catalogLabels.ToArray());
                if (float.IsNaN(scaleFactor) || float.IsInfinity(scaleFactor) || scaleFactor <= 0f)
                    throw new InvalidOperationException("Scale Factor must be a finite number greater than zero.");
                foreach (var map in materialRemaps ?? new MeshyMaterialRemap[0])
                {
                    if (map != null && map.material == null && !string.IsNullOrEmpty(map.materialGuid))
                        map.material = MeshySettingsMemory.Resolve(new MeshySettingsSnapshot.MaterialReference { key = map.key, guid = map.materialGuid, fileId = map.materialFileId });
                    string problem = map == null ? null : MaterialProblem(map.material, ctx.assetPath);
                    if (problem != null) throw new InvalidOperationException(problem);
                }

                var options = new MeshyGltfBuilder.BuildOptions
                {
                    ScaleFactor = scaleFactor > 0f ? scaleFactor : 1f,
                    GenerateColliders = generateColliders,
                    OptimizeMeshes = optimizeMeshes,
                    AutoRepairUvs = autoRepairUvs,
                };
                string unsupportedReason = string.Join("; ", preflight.FallbackReasons);
                var build = preflight.NeedsFallback ? null : MeshyGltfBuilder.Build(glb, assetName, options, out unsupportedReason);

                if (build != null)
                {
                    ApplyMaterials(build, ctx);
                    // Native path: no UnityGLTF/glTFast/.glb companion file involved.
                    // Every GameObject in the node hierarchy must be registered
                    // individually -- AssetImportContext only persists objects it
                    // was explicitly given, even if they're parented under one that was.
                    var identifiers = new System.Collections.Generic.HashSet<string>();
                    foreach (var sub in build.SubAssets)
                    {
                        string subName = string.IsNullOrEmpty(sub.name) ? sub.GetType().Name : sub.name;
                        string baseId = sub.GetType().Name + "_" + subName;
                        string id = baseId;
                        int duplicate = 1;
                        while (!identifiers.Add(id)) id = baseId + "#duplicate" + duplicate++;
                        ctx.AddObjectToAsset(id, sub);
                    }
                    foreach (var kv in build.Nodes)
                        ctx.AddObjectToAsset("Node_" + kv.Key + "_" + kv.Value.name, kv.Value);
                    for (int i = 0; i < build.PrimitiveNodes.Count; i++)
                        ctx.AddObjectToAsset("PrimitiveNode_" + i, build.PrimitiveNodes[i]);
                    ctx.AddObjectToAsset("MeshyRoot", build.Root);
                    ctx.SetMainObject(build.Root);
                    hasMainObject = true;

                    meta.SetAnalysis(build.AssetType, build.MeshCount, build.MaterialCount, build.TextureCount,
                        build.VertexCount, build.TriangleCount, build.MissingUvCount, build.Skinned);
                    meta.SetUvRepair(build.UvBadVertices, build.UvRepairedVertices, build.UvRegeneratedMeshes);
                    meta.SetRenderPipeline(build.Pipeline.ToString());
                    RecordWorkflow(build, meta);

                    status = $"Imported natively: {build.MeshCount} mesh(es), {build.MaterialCount} material(s), " +
                             $"{build.TextureCount} texture(s){(build.Skinned ? ", skinned" : "")}.";
                    if (build.UvBadVertices > 0 || build.UvRegeneratedMeshes > 0)
                        status += $" UV repair: {build.UvBadVertices} bad vertex UV(s) fixed" +
                                  (build.UvRegeneratedMeshes > 0 ? $", {build.UvRegeneratedMeshes} mesh(es) given new UVs" : "") + ".";
                    foreach (var note in build.Notes) status += " " + note;

                    // Only removes a .glb this importer itself wrote earlier (fallback path).
                    MeshyGeneratedGlbRegistry.DeleteIfGenerated(Path.ChangeExtension(ctx.assetPath, ".glb"));
                    MeshyGeneratedGlbRegistry.DeleteIfGenerated(Path.ChangeExtension(ctx.assetPath, null) + "_meshy.glb");
                }
                else
                {
                    // Fallback: something in this specific file isn't implemented by the native
                    // builder. Write the reconstructed .glb next to it and let whatever glTF
                    // importer is installed (UnityGLTF/glTFast) handle just this file.
                    // Write the file now (plain disk IO is safe here), but defer asking Unity to
                    // import it -- calling AssetDatabase.ImportAsset for another asset from inside
                    // this asset's own OnImportAsset is unsafe/unsupported. Never overwrite a
                    // .glb the user made themselves: use a distinct name instead.
                    string glbPath = Path.ChangeExtension(ctx.assetPath, ".glb");
                    if (!MeshyGeneratedGlbRegistry.TryWrite(glbPath, glb))
                    {
                        glbPath = Path.ChangeExtension(ctx.assetPath, null) + "_meshy.glb";
                        if (!MeshyGeneratedGlbRegistry.TryWrite(glbPath, glb))
                            throw new IOException("Both " + Path.ChangeExtension(ctx.assetPath, ".glb") + " and " + glbPath +
                                                  " already exist and were not created by the Meshy importer; not overwriting them.");
                    }
                    generatedGlb = glbPath;
                    string alternate = glbPath == Path.ChangeExtension(ctx.assetPath, ".glb")
                        ? Path.ChangeExtension(ctx.assetPath, null) + "_meshy.glb"
                        : Path.ChangeExtension(ctx.assetPath, ".glb");
                    MeshyGeneratedGlbRegistry.DeleteIfGenerated(alternate);
                    var packages = MeshyGltfPackages.Inspect();
                    // Let the asset refresh discover this file. Import workers cannot safely
                    // schedule a main-editor delayCall or recursively import a sibling asset.

                    status = $"Native import not available for this file ({unsupportedReason}). " +
                             "Generated " + Path.GetFileName(glbPath) + " for an external glTF importer. ";
                    if (packages.Problem != null) status += packages.Problem;
                    else if (!packages.HasFallback) status += "No glTF importer was detected; install ONE optional fallback importer to use this GLB.";
                    else status += "Select the generated GLB to check the external import result.";
                    status += " Meshy material mappings and import settings apply only to native imports; configure the generated GLB's own importer for this fallback.";
                    Debug.LogWarning("Meshy Importer: " + ctx.assetPath + " " + status);
                }
            }
            catch (Exception ex)
            {
                status = "Import failed: " + ex.Message;
                Debug.LogError("Meshy Importer: failed to import " + ctx.assetPath + "\n" + ex);
            }

            meta.SetMetadata(ctx.assetPath, generatedGlb, size, status);
            ctx.AddObjectToAsset("MeshySource", meta);
            if (!hasMainObject) ctx.SetMainObject(meta);
        }
    }
}
#endif
