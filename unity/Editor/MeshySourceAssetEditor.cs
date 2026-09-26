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

            if (source != null && MeshySupport.IsFailure(source.Status))
            {
                string message = source.Status;
                string help = MeshySupport.HelpLink(message);
                if (help != null) message = message.Substring(0, message.IndexOf("Help: ", System.StringComparison.Ordinal)).TrimEnd();
                EditorGUILayout.HelpBox(message, MessageType.Error);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (help != null && GUILayout.Button("Open Help")) Application.OpenURL(help);
                    if (GUILayout.Button("Copy Diagnostics")) MeshySupport.CopyDiagnostics(importer.assetPath);
                    if (GUILayout.Button("Report a Bug...")) MeshySupport.ReportBug(importer.assetPath);
                }
                EditorGUILayout.Space(6);
            }

            serializedObject.Update();
            EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);
            int current = CurrentPreset();
            int picked = EditorGUILayout.Popup(new GUIContent("Preset", PresetTooltip), current, PresetOptions);
            if (picked != current && picked < Presets.Length) ApplyPreset(Presets[picked]);
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
                if (!MeshySupport.IsFailure(source.Status))
                    EditorGUILayout.HelpBox(source.Status ?? "", MessageType.Info);
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

        // Presets set the on/off import options at once (Scale Factor is left alone).
        // "Custom" shows when the values match none.
        private struct Preset
        {
            public string Name;
            public bool RepairUvs, Colliders, Optimize;
        }

        private static readonly Preset[] Presets =
        {
            new Preset { Name = "Default", RepairUvs = true, Colliders = false, Optimize = true },
            new Preset { Name = "Game-ready (colliders)", RepairUvs = true, Colliders = true, Optimize = true },
            new Preset { Name = "Keep original data", RepairUvs = false, Colliders = false, Optimize = false },
        };

        private static readonly GUIContent[] PresetOptions =
        {
            new GUIContent("Default"), new GUIContent("Game-ready (colliders)"), new GUIContent("Keep original data"), new GUIContent("Custom"),
        };

        private const string PresetTooltip =
            "Default: repair broken UVs, optimize meshes.\n" +
            "Game-ready: Default plus a MeshCollider on every static mesh.\n" +
            "Keep original data: no UV repair or mesh reordering, exactly what the file contains.";

        private int CurrentPreset()
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                var p = Presets[i];
                if (_autoRepairUvs.boolValue == p.RepairUvs &&
                    _generateColliders.boolValue == p.Colliders && _optimizeMeshes.boolValue == p.Optimize)
                    return i;
            }
            return PresetOptions.Length - 1;
        }

        private void ApplyPreset(Preset p)
        {
            _autoRepairUvs.boolValue = p.RepairUvs;
            _generateColliders.boolValue = p.Colliders;
            _optimizeMeshes.boolValue = p.Optimize;
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
