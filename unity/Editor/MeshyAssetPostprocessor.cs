using UnityEditor;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// The native importer (MeshyScriptedImporter) builds everything a .meshy file
    /// needs directly inside OnImportAsset. This postprocessor only tidies up a .glb the
    /// importer itself generated on the fallback path once its source .meshy is deleted
    /// or moved. Files the importer did not write -- a user's own GLB, or one made with
    /// Tools > Meshy > Convert -- are never touched (see MeshyGeneratedGlbRegistry).
    /// </summary>
    public sealed class MeshyAssetPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var asset in deletedAssets) DeleteGeneratedGlb(asset);
            foreach (var asset in movedFromAssetPaths) DeleteGeneratedGlb(asset);
        }

        private static void DeleteGeneratedGlb(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".meshy", System.StringComparison.OrdinalIgnoreCase)) return;
            string stem = assetPath.Substring(0, assetPath.Length - ".meshy".Length);
            foreach (var glb in new[] { stem + ".glb", stem + "_meshy.glb" })
                if (MeshyGeneratedGlbRegistry.DeleteIfGenerated(glb))
                    UnityEngine.Debug.Log("Meshy Importer: removed generated GLB " + glb);
        }
    }
}
