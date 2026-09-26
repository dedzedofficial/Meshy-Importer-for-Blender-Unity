using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// Tools > Meshy > Meshy Importer: one place for everything the menu offers, plus a list of
    /// the project's .meshy files that failed to import and why.
    /// </summary>
    public sealed class MeshyImporterWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _showAdvanced;
        private readonly List<KeyValuePair<string, string>> _failures = new List<KeyValuePair<string, string>>();
        private int _fileCount;

        public static void Open()
        {
            var window = GetWindow<MeshyImporterWindow>("Meshy Importer");
            window.minSize = new Vector2(340, 360);
            window.Show();
        }

        private void OnEnable()
        {
            MeshyUpdateCheck.Checked += Repaint;
            Scan();
        }

        private void OnDisable() => MeshyUpdateCheck.Checked -= Repaint;

        private void OnFocus() => Scan();

        private void Scan()
        {
            _failures.Clear();
            var files = MeshyImporterMenu.FindMeshyAssets();
            _fileCount = files.Length;
            foreach (var f in files)
            {
                var source = MeshySupport.LoadSource(f);
                if (source != null && MeshySupport.IsFailure(source.Status))
                    _failures.Add(new KeyValuePair<string, string>(f, source.Status));
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Meshy Importer " + MeshyImporterMenu.Version, EditorStyles.boldLabel);
            if (MeshyUpdateCheck.UpdateAvailable)
            {
                EditorGUILayout.HelpBox("Version " + MeshyUpdateCheck.LatestKnownVersion + " is available.", MessageType.Info);
                if (GUILayout.Button("Get the Update")) Application.OpenURL(MeshyUpdateCheck.ReleasesUrl);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "1. Get a .meshy file from the Meshy website.\n" +
                "2. Drop it anywhere under Assets.\n" +
                "3. Select it to change its import settings.", MessageType.None);
            if (GUILayout.Button("How Do I Get a .meshy File?")) Application.OpenURL(MeshySupport.GettingAFileUrl);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Your .meshy Files", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_fileCount == 0
                ? "None under Assets yet."
                : _fileCount + " file(s)" + (_failures.Count > 0 ? ", " + _failures.Count + " failed to import" : ", all imported"));
            foreach (var failure in _failures)
            {
                string message = failure.Value;
                string help = MeshySupport.HelpLink(message);
                if (help != null) message = message.Substring(0, message.IndexOf("Help: ", System.StringComparison.Ordinal)).TrimEnd();
                EditorGUILayout.HelpBox(Path.GetFileName(failure.Key) + ": " + message, MessageType.Error);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select")) Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(failure.Key);
                    if (help != null && GUILayout.Button("Open Help")) Application.OpenURL(help);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reimport All")) { MeshyImporterMenu.ReimportAll(); Scan(); }
                if (GUILayout.Button("Convert All to .glb")) MeshyImporterMenu.ConvertAll();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Help", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Troubleshooting")) Application.OpenURL(MeshySupport.TroubleshootingUrl);
                if (GUILayout.Button("Discord")) Application.OpenURL(MeshySupport.DiscordUrl);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy Diagnostics")) MeshySupport.CopyDiagnostics();
                if (GUILayout.Button("Report a Bug...")) MeshySupport.ReportBug();
            }

            EditorGUILayout.Space(8);
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true);
            if (_showAdvanced)
            {
                bool check = EditorGUILayout.ToggleLeft("Check for updates once a day", MeshyUpdateCheck.Enabled);
                if (check != MeshyUpdateCheck.Enabled) MeshyUpdateCheck.Enabled = check;
                if (GUILayout.Button("Check for Updates Now")) MeshyUpdateCheck.CheckNow(true);
                if (GUILayout.Button("Validate Installation")) MeshyImporterMenu.ValidateInstallation();
                if (GUILayout.Button("Validate All .meshy In Assets")) MeshyImporterMenu.ValidateAll();
                if (GUILayout.Button("Install UnityGLTF (Optional Fallback)")) MeshyImporterMenu.InstallUnityGLTF();
                EditorGUILayout.LabelField("Only needed if a file reports that native import is not available.", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
