using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>Automatically reconstructs .meshy files whenever Unity imports or moves them.</summary>
    public sealed class MeshyAssetPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool scheduled;

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var asset in importedAssets) QueueIfMeshy(asset);
            for (int i = 0; i < movedAssets.Length; i++)
            {
                QueueIfMeshy(movedAssets[i]);
                DeleteOldGeneratedGlb(movedFromAssetPaths[i]);
            }
            foreach (var asset in deletedAssets) DeleteOldGeneratedGlb(asset);
            Schedule();
        }

        private static void QueueIfMeshy(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".meshy", StringComparison.OrdinalIgnoreCase)) return;
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return;
            Pending.Add(assetPath);
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

        private static void Schedule()
        {
            if (scheduled || Pending.Count == 0) return;
            scheduled = true;
            EditorApplication.delayCall += ProcessPending;
        }

        private static void ProcessPending()
        {
            scheduled = false;
            var work = new List<string>(Pending);
            Pending.Clear();
            int success = 0;
            foreach (var assetPath in work)
            {
                try
                {
                    if (!File.Exists(ProjectPath(assetPath))) continue;
                    string glbPath = Path.ChangeExtension(assetPath, ".glb");
                    byte[] glb = MeshyImporterMenu.DecodeFileForEditor(assetPath);
                    File.WriteAllBytes(ProjectPath(glbPath), glb);
                    success++;
                    Debug.Log("Meshy Importer: " + assetPath + " -> " + glbPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Meshy Importer: failed to import " + assetPath + ". " + ex.Message);
                }
            }
            if (success > 0) AssetDatabase.Refresh();
        }

        private static string ProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('\\', Path.DirectorySeparatorChar));
        }
    }
}
