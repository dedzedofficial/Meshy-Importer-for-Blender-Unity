#if UNITY_2020_2_OR_NEWER
using System;
using System.IO;
using FISHHWB.MeshyImporter;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    [ScriptedImporter(2, new[] { "meshy" }, AllowCaching = true)]
    public sealed class MeshyScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var meta = ScriptableObject.CreateInstance<MeshySourceAsset>();
            long size = 0;
            string status;
            string assetName = Path.GetFileNameWithoutExtension(ctx.assetPath);

            try
            {
                string fullPath = ProjectPath(ctx.assetPath);
                if (File.Exists(fullPath)) size = new FileInfo(fullPath).Length;

                byte[] glb = MeshyImporterMenu.DecodeFileForEditor(ctx.assetPath);

                var build = MeshyGltfBuilder.Build(glb, assetName, out string unsupportedReason);

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

                    status = $"Imported natively: {build.MeshCount} mesh(es), {build.MaterialCount} material(s), " +
                             $"{build.TextureCount} texture(s){(build.Skinned ? ", skinned" : "")}. " +
                             "No UnityGLTF/glTFast dependency used.";

                    CleanupStaleGlbCompanion(ctx.assetPath);
                }
                else
                {
                    // Fallback: something in this specific file isn't implemented by the native
                    // builder yet (e.g. EXT_meshopt_compression). Write the reconstructed .glb
                    // sibling and let whatever glTF importer is installed (UnityGLTF/glTFast)
                    // handle just this file, so it still imports instead of silently failing.
                    // Write the file now (plain disk IO is safe here), but defer asking
                    // Unity to import it -- calling AssetDatabase.ImportAsset for another
                    // asset from inside this asset's own OnImportAsset is unsafe/unsupported.
                    string glbPath = Path.ChangeExtension(ctx.assetPath, ".glb");
                    File.WriteAllBytes(ProjectPath(glbPath), glb);
                    EditorApplication.delayCall += () => AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceUpdate);

                    status = $"Native import not available for this file ({unsupportedReason}). " +
                              "Fell back to generating a .glb companion for UnityGLTF/glTFast.";
                    Debug.LogWarning("Meshy Importer: " + ctx.assetPath + " " + status);
                }
            }
            catch (Exception ex)
            {
                status = "Import failed: " + ex.Message;
                Debug.LogError("Meshy Importer: failed to import " + ctx.assetPath + "\n" + ex);
            }

            meta.SetMetadata(ctx.assetPath, Path.ChangeExtension(ctx.assetPath, ".glb"), size, status);
            ctx.AddObjectToAsset("MeshySource", meta);
        }

        private static void CleanupStaleGlbCompanion(string meshyAssetPath)
        {
            // Plain File.Delete only -- calling into AssetDatabase from inside another
            // asset's OnImportAsset is unsafe/unsupported. Unity notices the missing
            // file and drops the stale database entry on its own on the next refresh.
            string glbPath = Path.ChangeExtension(meshyAssetPath, ".glb");
            string full = ProjectPath(glbPath);
            if (File.Exists(full))
            {
                try
                {
                    File.Delete(full);
                    string metaPath = full + ".meta";
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Meshy Importer: could not remove stale generated GLB: " + ex.Message);
                }
            }
        }

        internal static string ProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('\\', Path.DirectorySeparatorChar));
        }
    }
}
#endif
