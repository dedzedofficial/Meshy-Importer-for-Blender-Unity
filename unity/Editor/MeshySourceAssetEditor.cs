#if UNITY_EDITOR
using System.IO;
using FISHHWB.MeshyImporter;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// Custom inspector shown when a .meshy asset is selected in the Project window
    /// (the standard Unity hook point for a ScriptedImporter, same as the built-in
    /// model/texture importers use).
    /// </summary>
    [CustomEditor(typeof(MeshyScriptedImporter))]
    public sealed class MeshySourceAssetEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            var importer = (MeshyScriptedImporter)target;
            var meta = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath);
            MeshySourceAsset source = null;
            foreach (var obj in meta)
            {
                if (obj is MeshySourceAsset s) { source = s; break; }
            }

            EditorGUILayout.LabelField("Meshy Importer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds meshes, materials, textures, and skinning directly from the .meshy payload. " +
                "The importer also performs lightweight asset analysis and editor-time mesh optimization " +
                "to reduce repeated setup work and improve runtime mesh locality.",
                MessageType.Info);

            if (source != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Source", source.SourcePath);
                EditorGUILayout.LabelField("Size", FormatBytes(source.SourceSize));
                EditorGUILayout.LabelField("Status", source.Status);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Asset Analysis", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Type", string.IsNullOrEmpty(source.AssetType) ? "Unknown" : source.AssetType);
                EditorGUILayout.LabelField("Meshes", source.MeshCount.ToString());
                EditorGUILayout.LabelField("Vertices", source.VertexCount.ToString("N0"));
                EditorGUILayout.LabelField("Triangles", source.TriangleCount.ToString("N0"));
                EditorGUILayout.LabelField("Materials", source.MaterialCount.ToString());
                EditorGUILayout.LabelField("Textures", source.TextureCount.ToString());
                EditorGUILayout.LabelField("Missing UV sets", source.MissingUvCount.ToString());
                EditorGUILayout.LabelField("Skinning", source.Skinned ? "Detected" : "None");
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reimport")) MeshyImporterMenu.ReimportAsset(importer.assetPath);
                if (GUILayout.Button("Open Folder"))
                {
                    string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, importer.assetPath.Replace('\\', Path.DirectorySeparatorChar));
                    if (File.Exists(full)) EditorUtility.RevealInFinder(full);
                }
                if (GUILayout.Button("Validate")) MeshyImporterMenu.ValidateOne(importer.assetPath);
            }

            ApplyRevertGUI();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.0") + " KB";
            return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
        }
    }
}
#endif
