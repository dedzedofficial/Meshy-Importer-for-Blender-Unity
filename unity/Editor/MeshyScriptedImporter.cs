#if UNITY_2020_2_OR_NEWER
using System;
using System.IO;
using FISHHWB.MeshyImporter;
using UnityEditor.AssetImporters;
using UnityEngine;
namespace FISHHWB.MeshyImporter.Editor
{
    [ScriptedImporter(1, new[] { "meshy" }, AllowCaching = true)]
    public sealed class MeshyScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<MeshySourceAsset>();
            string glbPath = Path.ChangeExtension(ctx.assetPath, ".glb");
            long size = 0;
            string status = "Ready for automatic GLB reconstruction.";
            try
            {
                string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ctx.assetPath.Replace('\\', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath)) size = new FileInfo(fullPath).Length;
                if (File.Exists(glbPath)) status = "Imported. UnityGLTF uses the generated GLB companion.";
            }
            catch (Exception ex) { status = "Diagnostic check failed: " + ex.Message; }
            asset.SetMetadata(ctx.assetPath, glbPath, size, status);
            ctx.AddObjectToAsset("MeshySource", asset);
            ctx.SetMainObject(asset);
        }
    }
}
#endif
