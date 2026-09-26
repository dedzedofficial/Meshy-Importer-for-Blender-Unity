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
    [ScriptedImporter(7, new[] { "meshy" }, AllowCaching = true)]
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

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var meta = ScriptableObject.CreateInstance<MeshySourceAsset>();
            long size = 0;
            string status;
            string generatedGlb = null;
            string assetName = Path.GetFileNameWithoutExtension(ctx.assetPath);

            try
            {
                string fullPath = MeshyPaths.ProjectPath(ctx.assetPath);
                if (File.Exists(fullPath)) size = new FileInfo(fullPath).Length;

                byte[] glb = MeshyImporterMenu.DecodeFileForEditor(ctx.assetPath);

                var options = new MeshyGltfBuilder.BuildOptions
                {
                    ScaleFactor = scaleFactor > 0f ? scaleFactor : 1f,
                    GenerateColliders = generateColliders,
                    OptimizeMeshes = optimizeMeshes,
                    AutoRepairUvs = autoRepairUvs,
                };
                var build = MeshyGltfBuilder.Build(glb, assetName, options, out string unsupportedReason);

                if (build != null)
                {
                    // Native path: no UnityGLTF/glTFast/.glb companion file involved.
                    // Every GameObject in the node hierarchy must be registered
                    // individually -- AssetImportContext only persists objects it
                    // was explicitly given, even if they're parented under one that was.
                    foreach (var sub in build.SubAssets)
                    {
                        string subName = string.IsNullOrEmpty(sub.name) ? sub.GetType().Name : sub.name;
                        ctx.AddObjectToAsset(sub.GetType().Name + "_" + subName, sub);
                    }
                    foreach (var kv in build.Nodes)
                        ctx.AddObjectToAsset("Node_" + kv.Key + "_" + kv.Value.name, kv.Value);
                    ctx.AddObjectToAsset("MeshyRoot", build.Root);
                    ctx.SetMainObject(build.Root);

                    meta.SetAnalysis(build.AssetType, build.MeshCount, build.MaterialCount, build.TextureCount,
                        build.VertexCount, build.TriangleCount, build.MissingUvCount, build.Skinned);
                    meta.SetUvRepair(build.UvBadVertices, build.UvRepairedVertices, build.UvRegeneratedMeshes);
                    meta.SetRenderPipeline(build.Pipeline.ToString());

                    status = $"Imported natively: {build.MeshCount} mesh(es), {build.MaterialCount} material(s), " +
                             $"{build.TextureCount} texture(s){(build.Skinned ? ", skinned" : "")}.";
                    if (build.UvBadVertices > 0 || build.UvRegeneratedMeshes > 0)
                        status += $" UV repair: {build.UvBadVertices} bad vertex UV(s) fixed" +
                                  (build.UvRegeneratedMeshes > 0 ? $", {build.UvRegeneratedMeshes} mesh(es) given new UVs" : "") + ".";
                    foreach (var note in build.Notes) status += " " + note;

                    // Only removes a .glb this importer itself wrote earlier (fallback path).
                    MeshyGeneratedGlbRegistry.DeleteIfGenerated(Path.ChangeExtension(ctx.assetPath, ".glb"));
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
                    EditorApplication.delayCall += () => AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceUpdate);

                    status = $"Native import not available for this file ({unsupportedReason}). " +
                             "Fell back to generating " + Path.GetFileName(glbPath) + " for UnityGLTF/glTFast.";
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
        }
    }
}
#endif
