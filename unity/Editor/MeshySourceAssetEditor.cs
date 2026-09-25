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
    /// model/texture importers use). Import settings are applied with Apply/Revert
    /// like any other importer; changing them reimports the asset.
    /// </summary>
    [CustomEditor(typeof(MeshyScriptedImporter))]
    public sealed class MeshySourceAssetEditor : ScriptedImporterEditor
    {
        private SerializedProperty _scaleFactor, _generateColliders, _optimizeMeshes, _autoRepairUvs;

        public override void OnEnable()
        {
            base.OnEnable();
            _scaleFactor = serializedObject.FindProperty("scaleFactor");
            _generateColliders = serializedObject.FindProperty("generateColliders");
            _optimizeMeshes = serializedObject.FindProperty("optimizeMeshes");
            _autoRepairUvs = serializedObject.FindProperty("autoRepairUvs");
        }

        public override void OnInspectorGUI()
        {
            var importer = (MeshyScriptedImporter)target;
            MeshySourceAsset source = null;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(importer.assetPath))
            {
                if (obj is MeshySourceAsset s) { source = s; break; }
            }

            EditorGUILayout.LabelField("Meshy Importer " + MeshyImporterMenu.Version, EditorStyles.boldLabel);

            serializedObject.Update();
            EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_scaleFactor, new GUIContent("Scale Factor"));
            EditorGUILayout.PropertyField(_autoRepairUvs, new GUIContent("Auto-repair UVs"));
            EditorGUILayout.PropertyField(_generateColliders, new GUIContent("Generate Colliders"));
            EditorGUILayout.PropertyField(_optimizeMeshes, new GUIContent("Optimize Meshes"));
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();

            if (source != null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Source", source.SourcePath);
                EditorGUILayout.LabelField("Size", FormatBytes(source.SourceSize));
                EditorGUILayout.HelpBox(source.Status ?? "", source.Status != null && source.Status.StartsWith("Import failed") ? MessageType.Error : MessageType.Info);
                if (!string.IsNullOrEmpty(source.GeneratedGlbPath))
                    EditorGUILayout.LabelField("Generated GLB", source.GeneratedGlbPath);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Asset Analysis", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Type", string.IsNullOrEmpty(source.AssetType) ? "Unknown" : source.AssetType);
                EditorGUILayout.LabelField("Render pipeline", string.IsNullOrEmpty(source.RenderPipeline) ? "-" : source.RenderPipeline);
                EditorGUILayout.LabelField("Meshes", source.MeshCount.ToString());
                EditorGUILayout.LabelField("Vertices", source.VertexCount.ToString("N0"));
                EditorGUILayout.LabelField("Triangles", source.TriangleCount.ToString("N0"));
                EditorGUILayout.LabelField("Materials", source.MaterialCount.ToString());
                EditorGUILayout.LabelField("Textures", source.TextureCount.ToString());
                EditorGUILayout.LabelField("Missing UV sets", source.MissingUvCount.ToString());
                EditorGUILayout.LabelField("UVs repaired", source.UvBadVertices == 0 && source.UvRegeneratedMeshes == 0
                    ? "None needed"
                    : $"{source.UvBadVertices:N0} vertex UV(s), {source.UvRegeneratedMeshes} mesh(es) regenerated");
                EditorGUILayout.LabelField("Skinning", source.Skinned ? "Detected" : "None");
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reimport")) MeshyImporterMenu.ReimportAsset(importer.assetPath);
                if (GUILayout.Button("Open Folder"))
                {
                    string full = MeshyPaths.ProjectPath(importer.assetPath);
                    if (File.Exists(full)) EditorUtility.RevealInFinder(full);
                }
                if (GUILayout.Button("Validate")) MeshyImporterMenu.ValidateOne(importer.assetPath);
            }
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
