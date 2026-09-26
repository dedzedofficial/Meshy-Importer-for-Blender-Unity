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
        private SerializedProperty _scaleFactor, _generateColliders, _optimizeMeshes, _autoRepairUvs, _materialRemaps;
        private bool _showPreflight;

        public override void OnEnable()
        {
            base.OnEnable();
            _scaleFactor = serializedObject.FindProperty("scaleFactor");
            _generateColliders = serializedObject.FindProperty("generateColliders");
            _optimizeMeshes = serializedObject.FindProperty("optimizeMeshes");
            _autoRepairUvs = serializedObject.FindProperty("autoRepairUvs");
            _materialRemaps = serializedObject.FindProperty("materialRemaps");
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
            if (source != null) DrawMaterialOverrides(source, importer.assetPath);
            EditorGUILayout.HelpBox("Applied settings and material choices stay with this model during reimport. Keep the source .meta file when moving it outside Unity.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
            if (GUILayout.Button("Restore Last Successful Settings"))
            {
                try
                {
                    var saved = MeshySettingsMemory.Load(importer.assetPath);
                    if (EditorUtility.DisplayDialog("Restore Settings", "Replace current settings with the last successful native import settings and reimport?", "Restore", "Cancel"))
                    {
                        Undo.RecordObject(importer, "Restore Meshy import settings");
                        importer.RestoreSettings(saved);
                        EditorUtility.SetDirty(importer);
                        importer.SaveAndReimport();
                        serializedObject.Update();
                        GUIUtility.ExitGUI();
                    }
                }
                catch (System.Exception ex)
                {
                    if (ex is ExitGUIException) throw;
                    MeshyImporterMenu.ShowError("Restore Settings", ex.Message);
                }
            }

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
                if (source.NativeSuccess && !MeshySupport.IsFailure(source.Status))
                {
                    EditorGUILayout.LabelField("Dimensions (Unity units)", source.Dimensions.ToString("F3"));
                    EditorGUILayout.LabelField("Bones used", source.BoneCount.ToString());
                    EditorGUILayout.LabelField("Largest imported texture", source.LargestTextureWidth + " × " + source.LargestTextureHeight);
                    EditorGUILayout.LabelField("Texture memory estimate", FormatBytes(source.EstimatedTextureBytes));
                    EditorGUILayout.LabelField("Uncompressed RGBA + mipmaps; generated textures only, excludes CPU copies and overrides.", EditorStyles.miniLabel);
                    if (GUILayout.Button("Create Editable Copy (Prefab + Assets)")) MeshyEditableCopy.Create(importer.assetPath);
                }
                if (!string.IsNullOrEmpty(source.PreflightReport))
                {
                    _showPreflight = EditorGUILayout.Foldout(_showPreflight, "Preflight Report", true);
                    if (_showPreflight) EditorGUILayout.HelpBox(source.PreflightReport, MessageType.Info);
                }
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
                if (GUILayout.Button("Run Preflight")) MeshyImporterMenu.ValidateOne(importer.assetPath);
            }
        }

        private void DrawMaterialOverrides(MeshySourceAsset source, string assetPath)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Material Overrides", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Leave empty to use generated materials. Apply to save your choices.", EditorStyles.miniLabel);
            var keys = source.MaterialKeys ?? new string[0];
            var labels = source.MaterialLabels ?? new string[0];
            for (int slot = 0; slot < keys.Length; slot++)
            {
                SerializedProperty entry = null;
                for (int i = 0; i < _materialRemaps.arraySize; i++)
                {
                    var candidate = _materialRemaps.GetArrayElementAtIndex(i);
                    if (candidate.FindPropertyRelative("key").stringValue == keys[slot]) { entry = candidate; break; }
                }
                var existing = entry == null ? null : entry.FindPropertyRelative("material").objectReferenceValue as Material;
                string label = (slot < labels.Length ? labels[slot] : "Material") + " (" + (slot + 1) + ")";
                var chosen = EditorGUILayout.ObjectField(label, existing, typeof(Material), false) as Material;
                bool clearMissing = existing == null && entry != null && !string.IsNullOrEmpty(entry.FindPropertyRelative("materialGuid").stringValue)
                    && GUILayout.Button("Use Generated Material for " + label);
                if (chosen == existing && !clearMissing) continue;
                string problem = MeshyScriptedImporter.MaterialProblem(chosen, assetPath);
                if (problem != null) { EditorUtility.DisplayDialog("Material Override", problem, "OK"); continue; }
                if (entry == null)
                {
                    int index = _materialRemaps.arraySize;
                    _materialRemaps.arraySize++;
                    entry = _materialRemaps.GetArrayElementAtIndex(index);
                    entry.FindPropertyRelative("key").stringValue = keys[slot];
                }
                entry.FindPropertyRelative("material").objectReferenceValue = chosen;
                string materialGuid = ""; long materialId = 0;
                if (chosen != null) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(chosen, out materialGuid, out materialId);
                entry.FindPropertyRelative("materialGuid").stringValue = materialGuid;
                entry.FindPropertyRelative("materialFileId").longValue = materialId;
            }
            int unmatched = 0;
            for (int i = 0; i < _materialRemaps.arraySize; i++)
            {
                var entry = _materialRemaps.GetArrayElementAtIndex(i);
                if (System.Array.IndexOf(keys, entry.FindPropertyRelative("key").stringValue) < 0 &&
                    (entry.FindPropertyRelative("material").objectReferenceValue != null || !string.IsNullOrEmpty(entry.FindPropertyRelative("materialGuid").stringValue))) unmatched++;
            }
            if (unmatched > 0)
            {
                EditorGUILayout.HelpBox(unmatched + " saved material choice(s) no longer match this source. They are retained in case you restore the previous source model.", MessageType.Warning);
                if (GUILayout.Button("Remove Unmatched Material Choices"))
                    for (int i = _materialRemaps.arraySize - 1; i >= 0; i--)
                        if (System.Array.IndexOf(keys, _materialRemaps.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue) < 0)
                            _materialRemaps.DeleteArrayElementAtIndex(i);
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
