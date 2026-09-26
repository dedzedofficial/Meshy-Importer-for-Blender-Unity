using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>Queue fallback GLBs on the main editor after source imports finish.</summary>
    public sealed class MeshyAssetPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);
        private static bool queued;
        private static bool removedCompanion;

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var asset in deletedAssets) DeleteGeneratedGlb(asset);
            foreach (var asset in movedFromAssetPaths) DeleteGeneratedGlb(asset);
            foreach (var asset in importedAssets)
            {
                if (!IsMeshy(asset)) continue;
                var source = MeshySupport.LoadSource(asset);
                MeshySettingsMemory.Remember(asset, source);
                if (source != null && !string.IsNullOrEmpty(source.GeneratedGlbPath))
                    Pending.Add(source.GeneratedGlbPath);
            }
            if (!queued && (Pending.Count > 0 || removedCompanion))
            {
                queued = true;
                EditorApplication.delayCall += Flush;
            }
        }

        private static void Flush()
        {
            queued = false;
            var paths = new List<string>(Pending);
            Pending.Clear();
            bool refresh = removedCompanion;
            removedCompanion = false;
            foreach (string path in paths)
            {
                if (!File.Exists(MeshyPaths.ProjectPath(path))) continue;
                try { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); }
                catch (Exception ex) { UnityEngine.Debug.LogError("Meshy Importer: fallback import failed for " + path + "\n" + ex); }
            }
            if (refresh) AssetDatabase.Refresh();
        }

        private static bool IsMeshy(string path) =>
            !string.IsNullOrEmpty(path) && path.EndsWith(".meshy", StringComparison.OrdinalIgnoreCase);

        private static void DeleteGeneratedGlb(string assetPath)
        {
            if (!IsMeshy(assetPath)) return;
            string stem = assetPath.Substring(0, assetPath.Length - ".meshy".Length);
            foreach (var glb in new[] { stem + ".glb", stem + "_meshy.glb" })
                if (MeshyGeneratedGlbRegistry.DeleteIfGenerated(glb))
                {
                    removedCompanion = true;
                    UnityEngine.Debug.Log("Meshy Importer: removed generated GLB " + glb);
                }
        }
    }
}
