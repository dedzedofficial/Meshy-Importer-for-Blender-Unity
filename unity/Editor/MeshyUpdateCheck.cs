using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// Looks up the latest GitHub release at most once a day and says so in the Console and
    /// the Meshy Importer window when a newer version exists. It only downloads the public
    /// release information (no data about you or your project is sent) and can be turned
    /// off in Tools > Meshy > Meshy Importer.
    /// </summary>
    public static class MeshyUpdateCheck
    {
        private const string ApiUrl = "https://api.github.com/repos/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest";
        public const string ReleasesUrl = MeshyImporterMenu.RepositoryUrl + "/releases/latest";
        private const string EnabledKey = "FISHHWB.MeshyImporter.UpdateCheck.Enabled";
        private const string LastCheckKey = "FISHHWB.MeshyImporter.UpdateCheck.LastUtc";
        private const string LatestKey = "FISHHWB.MeshyImporter.UpdateCheck.Latest";
        private const string AnnouncedKey = "FISHHWB.MeshyImporter.UpdateCheck.Announced";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        /// <summary>The newest release version seen so far ("" when unknown).</summary>
        public static string LatestKnownVersion => EditorPrefs.GetString(LatestKey, "");

        public static bool UpdateAvailable => IsNewer(LatestKnownVersion, MeshyImporterMenu.Version);

        /// <summary>Raised on the main thread when a check finishes.</summary>
        public static event Action Checked;

        [InitializeOnLoadMethod]
        private static void AutoCheck()
        {
            if (!Enabled || Application.isBatchMode) return;
            if (DateTime.TryParse(EditorPrefs.GetString(LastCheckKey, ""), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var last) &&
                (DateTime.UtcNow - last).TotalHours < 24)
                return;
            EditorApplication.delayCall += () => CheckNow(false);
        }

        public static void CheckNow(bool userInitiated)
        {
            UnityWebRequest request;
            try
            {
                request = UnityWebRequest.Get(ApiUrl);
                request.SetRequestHeader("User-Agent", "MeshyImporter-Unity/" + MeshyImporterMenu.Version);
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.timeout = 15;
                request.SendWebRequest();
            }
            catch (Exception ex)
            {
                if (userInitiated) EditorUtility.DisplayDialog("Meshy Importer", "Could not check for updates: " + ex.Message, "OK");
                return;
            }

            void Poll()
            {
                if (!request.isDone) return;
                EditorApplication.update -= Poll;
                EditorPrefs.SetString(LastCheckKey, DateTime.UtcNow.ToString("o"));
                string latest = null;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var json = MeshyMiniJson.AsObject(MeshyMiniJson.Parse(request.downloadHandler.text));
                        latest = MeshyMiniJson.GetString(json, "tag_name", "").TrimStart('v', 'V');
                    }
                    catch (Exception) { latest = null; }
                }
                request.Dispose();

                if (!string.IsNullOrEmpty(latest)) EditorPrefs.SetString(LatestKey, latest);
                Report(latest, userInitiated);
                Checked?.Invoke();
            }
            EditorApplication.update += Poll;
        }

        private static void Report(string latest, bool userInitiated)
        {
            string current = MeshyImporterMenu.Version;
            if (string.IsNullOrEmpty(latest))
            {
                if (userInitiated)
                    EditorUtility.DisplayDialog("Meshy Importer", "Could not reach GitHub to check for updates. Try again later.", "OK");
                return;
            }
            if (IsNewer(latest, current))
            {
                // Log each new release once; the window keeps showing the banner.
                if (EditorPrefs.GetString(AnnouncedKey, "") != latest)
                {
                    EditorPrefs.SetString(AnnouncedKey, latest);
                    Debug.Log($"Meshy Importer {latest} is available (you have {current}). " +
                              "Update it in Window > Package Manager, or see " + ReleasesUrl);
                }
                if (userInitiated &&
                    EditorUtility.DisplayDialog("Meshy Importer", $"Version {latest} is available (you have {current}).",
                        "Open Release Page", "Later"))
                    Application.OpenURL(ReleasesUrl);
            }
            else if (userInitiated)
            {
                EditorUtility.DisplayDialog("Meshy Importer", $"You have the latest version ({current}).", "OK");
            }
        }

        /// <summary>True when dotted version <paramref name="a"/> is newer than <paramref name="b"/>.</summary>
        public static bool IsNewer(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string[] pa = a.Split('.', '-'), pb = b.Split('.', '-');
            for (int i = 0; i < 3; i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out var xi) ? xi : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out var yi) ? yi : 0;
                if (x != y) return x > y;
            }
            return false;
        }
    }
}
