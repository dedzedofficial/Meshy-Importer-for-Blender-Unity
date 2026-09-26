using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>Diagnostics text, "Report a Bug" links and other help shared by the menu, the
    /// Meshy Importer window and the .meshy Inspector.</summary>
    public static class MeshySupport
    {
        public const string RepositoryUrl = MeshyImporterMenu.RepositoryUrl;
        public const string GettingAFileUrl = RepositoryUrl + "/blob/main/GETTING_A_MESHY_FILE.md";
        public const string TroubleshootingUrl = MeshyFileCheck.TroubleshootingUrl;
        public const string ChangelogUrl = RepositoryUrl + "/blob/main/CHANGELOG.md";
        public const string DiscordUrl = "https://discord.gg/vCcsnX4HQP";

        /// <summary>Everything a bug report needs, as plain text. Optionally includes one asset's status.</summary>
        public static string Diagnostics(string assetPath = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Meshy Importer: " + MeshyImporterMenu.Version + " (Unity package)");
            sb.AppendLine("Unity: " + Application.unityVersion);
            sb.AppendLine("OS: " + SystemInfo.operatingSystem);
            string pipeline;
            try { pipeline = MeshyGltfBuilder.DetectPipeline().ToString(); }
            catch (Exception) { pipeline = "unknown"; }
            sb.AppendLine("Render pipeline: " + pipeline);
            sb.AppendLine("UnityGLTF fallback installed: " + (UnityGltfInstalled() ? "yes" : "no"));
            string latest = MeshyUpdateCheck.LatestKnownVersion;
            if (!string.IsNullOrEmpty(latest)) sb.AppendLine("Latest release seen: " + latest);

            if (!string.IsNullOrEmpty(assetPath))
            {
                sb.AppendLine();
                AppendAsset(sb, assetPath);
            }
            else
            {
                var files = MeshyImporterMenu.FindMeshyAssets();
                int failed = 0;
                foreach (var f in files)
                {
                    var s = LoadSource(f);
                    if (s != null && IsFailure(s.Status)) failed++;
                }
                sb.AppendLine(".meshy files in project: " + files.Length + (failed > 0 ? " (" + failed + " failed to import)" : ""));
                foreach (var f in files)
                {
                    var s = LoadSource(f);
                    if (s != null && IsFailure(s.Status)) { sb.AppendLine(); AppendAsset(sb, f); }
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static void AppendAsset(StringBuilder sb, string assetPath)
        {
            sb.AppendLine("File: " + Path.GetFileName(assetPath));
            string full = MeshyPaths.ProjectPath(assetPath);
            if (File.Exists(full))
            {
                var data = File.ReadAllBytes(full);
                sb.AppendLine("Size: " + data.Length.ToString("N0") + " bytes");
                sb.AppendLine("Header: " + DescribeHeader(data));
            }
            var source = LoadSource(assetPath);
            if (source != null)
            {
                sb.AppendLine("Status: " + source.Status);
                if (!IsFailure(source.Status))
                    sb.AppendLine($"Analysis: {source.MeshCount} mesh(es), {source.VertexCount:N0} vertices, {source.TriangleCount:N0} triangles, " +
                                  $"{source.MaterialCount} material(s), {source.TextureCount} texture(s), skinned={source.Skinned}");
            }
        }

        private static string DescribeHeader(byte[] data)
        {
            int n = Math.Min(8, data.Length);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(data[i] >= 32 && data[i] < 127 ? (char)data[i] : '.');
            return "\"" + sb + "\"";
        }

        public static MeshySourceAsset LoadSource(string assetPath)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (all == null) return null;
            foreach (var obj in all)
                if (obj is MeshySourceAsset s) return s;
            return null;
        }

        public static bool IsFailure(string status) =>
            status != null && status.StartsWith("Import failed", StringComparison.Ordinal);

        /// <summary>The "Help: https://..." link at the end of an error message, if any.</summary>
        public static string HelpLink(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            int i = message.IndexOf("Help: http", StringComparison.Ordinal);
            if (i < 0) return null;
            string url = message.Substring(i + "Help: ".Length).Trim();
            int space = url.IndexOfAny(new[] { ' ', '\n', '\r' });
            return space < 0 ? url : url.Substring(0, space);
        }

        public static bool UnityGltfInstalled()
        {
            try
            {
                string manifest = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages/manifest.json");
                return File.Exists(manifest) && File.ReadAllText(manifest).IndexOf("org.khronos.unitygltf", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception) { return false; }
        }

        public static void CopyDiagnostics(string assetPath = null)
        {
            EditorGUIUtility.systemCopyBuffer = Diagnostics(assetPath);
            Debug.Log("Meshy Importer: diagnostics copied to the clipboard.\n" + EditorGUIUtility.systemCopyBuffer);
        }

        /// <summary>Opens a pre-filled GitHub bug report (the fields match .github/ISSUE_TEMPLATE/bug_report.yml).</summary>
        public static void ReportBug(string assetPath = null)
        {
            string diagnostics = Diagnostics(assetPath);
            EditorGUIUtility.systemCopyBuffer = diagnostics;
            if (diagnostics.Length > 1500) diagnostics = diagnostics.Substring(0, 1500) + "\n...";
            string url = RepositoryUrl + "/issues/new?template=bug_report.yml" +
                         "&importer=" + Uri.EscapeDataString(MeshyImporterMenu.Version + " (Unity)") +
                         "&host=" + Uri.EscapeDataString("Unity " + Application.unityVersion + ", " + SystemInfo.operatingSystem) +
                         "&logs=" + Uri.EscapeDataString(diagnostics);
            Application.OpenURL(url);
        }
    }
}
