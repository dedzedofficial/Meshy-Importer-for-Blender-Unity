using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// The native importer (MeshyScriptedImporter) builds everything a .meshy file
    /// needs directly inside OnImportAsset, so this postprocessor no longer needs to
    /// generate or queue a .glb sidecar on import. It still cleans up a stale generated
    /// .glb (from the fallback path, or from before this version) when its source
    /// .meshy is deleted or moved, so old sidecars don't linger in the project.
    /// </summary>
    public sealed class MeshyAssetPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var asset in deletedAssets) DeleteOldGeneratedGlb(asset);
            for (int i = 0; i < movedFromAssetPaths.Length; i++) DeleteOldGeneratedGlb(movedFromAssetPaths[i]);
        }

        private static void DeleteOldGeneratedGlb(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".meshy", StringComparison.OrdinalIgnoreCase)) return;
            string glb = Path.ChangeExtension(assetPath, ".glb");
            string full = ProjectPath(glb);
            if (File.Exists(full))
            {
                try { File.Delete(full); Debug.Log("Meshy Importer: removed generated GLB " + glb); }
                catch (Exception ex) { Debug.LogWarning("Meshy Importer: could not remove generated GLB: " + ex.Message); }
            }
        }

        private static string ProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('\\', Path.DirectorySeparatorChar));
        }
    }
}
