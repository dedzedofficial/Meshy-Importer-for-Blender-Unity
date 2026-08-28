#if UNITY_EDITOR
using System.IO;
using FISHHWB.MeshyImporter;
using UnityEditor;
using UnityEngine;
namespace FISHHWB.MeshyImporter.Editor
{
    [CustomEditor(typeof(MeshySourceAsset))]
    public sealed class MeshySourceAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (MeshySourceAsset)target;
            EditorGUILayout.LabelField("Meshy Importer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This .meshy file is a first-class Unity custom asset. A local GLB companion is generated for UnityGLTF.", MessageType.Info);
            EditorGUILayout.LabelField("Source", asset.SourcePath);
            EditorGUILayout.LabelField("Size", FormatBytes(asset.SourceSize));
            EditorGUILayout.LabelField("Status", asset.Status);
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reimport")) MeshyImporterMenu.ReimportAsset(asset.SourcePath);
                if (GUILayout.Button("Open Folder"))
                {
                    string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, asset.SourcePath.Replace('\\', Path.DirectorySeparatorChar));
                    if (File.Exists(full)) EditorUtility.RevealInFinder(full);
                }
            }
            if (GUILayout.Button("Validate This .meshy")) MeshyImporterMenu.ValidateOne(asset.SourcePath);
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
