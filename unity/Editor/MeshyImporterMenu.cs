using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    public static class MeshyImporterMenu
    {
        private const string FirstRunKey = "FISHHWB.MeshyImporter.FirstRunShown.1.1.1";

        [InitializeOnLoadMethod]
        private static void FirstRun()
        {
            if (EditorPrefs.GetBool(FirstRunKey, false)) return;
            EditorApplication.delayCall += ShowFirstRun;
        }

        private static void ShowFirstRun()
        {
            if (EditorPrefs.GetBool(FirstRunKey, false)) return;
            EditorPrefs.SetBool(FirstRunKey, true);
            int choice = EditorUtility.DisplayDialogComplex(
                "Meshy Importer for Blender & Unity",
                "Installed successfully. Drop a real .meshy file anywhere under Assets and it will be reconstructed locally for UnityGLTF.\n\n" +
                "Recommended: install the compatible UnityGLTF dependency from Tools > Meshy.",
                "Setup UnityGLTF", "Later", "Documentation");
            if (choice == 0) InstallUnityGLTF();
            else if (choice == 2) Application.OpenURL(RepositoryUrl);
        }

        private const string RepositoryUrl =
            "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity";

        [MenuItem("Tools/Meshy/Show Welcome Again")]
        public static void ShowWelcomeAgain()
        {
            EditorPrefs.DeleteKey(FirstRunKey);
            ShowFirstRun();
        }

        [MenuItem("Tools/Meshy/About Meshy Importer for Blender & Unity")]
        public static void About()
        {
            EditorUtility.DisplayDialog(
                "Meshy Importer for Blender & Unity",
                "Created by FISHHWB\n\n" +
                "Meshy .meshy -> GLB importer for Unity.\n" +
                "UnityGLTF is installed separately at the project level.\n" +
                "Use Tools > Meshy > Install UnityGLTF (Compatible Version) before importing models.\n\n" +
                RepositoryUrl + "\n\nSupport the project:\n" + PatreonUrl,
                "OK");
        }

        private const string PatreonUrl = "https://www.patreon.com/cw/DedZed";

        [MenuItem("Tools/Meshy/Support / Donate on Patreon") ]
        public static void Donate()
        {
            Application.OpenURL(PatreonUrl);
        }

        private const string UnityGLTFModernUrl =
            "https://github.com/KhronosGroup/UnityGLTF.git#release/2.21.0";
        private const string UnityGLTFLegacyUrl =
            "https://github.com/KhronosGroup/UnityGLTF.git#release/2.9.1-rc";

        [MenuItem("Tools/Meshy/Install UnityGLTF (Compatible Version)")]
        public static void InstallUnityGLTF()
        {
            string unityVersion = Application.unityVersion;
            string versionPrefix = unityVersion.Length >= 6 ? unityVersion.Substring(0, 6) : unityVersion;
            bool legacy2020 = versionPrefix.StartsWith("2020.3", StringComparison.Ordinal);
            string selectedVersion = legacy2020 ? "2.9.1-rc" : "2.21.0";
            string selectedUrl = legacy2020 ? UnityGLTFLegacyUrl : UnityGLTFModernUrl;

            var request = Client.Add(selectedUrl);
            void CheckRequest()
            {
                if (!request.IsCompleted)
                    return;

                EditorApplication.update -= CheckRequest;

                if (request.Status == StatusCode.Success)
                {
                    EditorUtility.DisplayDialog(
                        "UnityGLTF Installed",
                        "UnityGLTF " + selectedVersion + " has been added to this project.\n\n" +
                        "You can now drop .meshy files into Assets and import the resulting model.",
                        "OK");
                }
                else
                {
                    string message = request.Error != null ? request.Error.message : "Unknown Package Manager error.";
                    Debug.LogError("FISHHWB Meshy Importer: UnityGLTF installation failed.\n" + message);
                    EditorUtility.DisplayDialog("UnityGLTF Installation Failed", message, "OK");
                }
            }

            EditorApplication.update += CheckRequest;
        }

        [MenuItem("Tools/Meshy/Validate Installation")]
        public static void ValidateInstallation()
        {
            string manifest = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages/manifest.json");
            bool packagePresent = File.Exists(manifest) && File.ReadAllText(manifest).IndexOf("org.khronos.unitygltf", StringComparison.OrdinalIgnoreCase) >= 0;
            string message = "Meshy Importer 1.1.1: OK\n" +
                "Unity: " + Application.unityVersion + "\n" +
                "UnityGLTF: " + (packagePresent ? "detected in Packages/manifest.json" : "NOT detected") + "\n" +
                ".meshy Asset Pipeline: " + (typeof(ScriptedImporter) != null ? "available" : "unavailable") + "\n" +
                "Decoder: local/editor only\n" +
                "Patreon: https://www.patreon.com/cw/DedZed";
            EditorUtility.DisplayDialog("Meshy Importer Diagnostics", message, "OK");
        }

        [MenuItem("Tools/Meshy/Validate All .meshy In Assets")]
        public static void ValidateAll()
        {
            string[] files = Directory.GetFiles(Application.dataPath, "*.meshy", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("Meshy Validation", "No .meshy files were found inside Assets.", "OK");
                return;
            }
            int valid = 0;
            foreach (string file in files)
            {
                string assetPath = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                if (ValidateOne(assetPath, false)) valid++;
            }
            EditorUtility.DisplayDialog("Meshy Validation", $"Valid: {valid}\nInvalid: {files.Length - valid}\nTotal: {files.Length}", "OK");
        }

        public static void ValidateOne(string assetPath)
        {
            ValidateOne(assetPath, true);
        }

        private static bool ValidateOne(string assetPath, bool showDialog)
        {
            try
            {
                DecodeFileForEditor(assetPath);
                if (showDialog) EditorUtility.DisplayDialog("Meshy Validation", "Valid .meshy payload. The decoder produced a valid GLB.", "OK");
                return true;
            }
            catch (Exception ex)
            {
                if (showDialog) EditorUtility.DisplayDialog("Meshy Validation Failed", ex.Message, "OK");
                return false;
            }
        }

        [MenuItem("Tools/Meshy/Reimport Selected .meshy")]
        public static void ReimportSelected()
        {
            string path = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".meshy", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Meshy Importer", "Select a .meshy asset in the Project window first.", "OK");
                return;
            }
            ReimportAsset(path);
        }

        public static void ReimportAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".meshy", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                string full = ProjectPath(assetPath);
                File.WriteAllBytes(Path.ChangeExtension(full, ".glb"), DecodeFileForEditor(assetPath));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                Debug.Log("Meshy Importer: reimported " + assetPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("Meshy Importer: reimport failed for " + assetPath + "\n" + ex);
                EditorUtility.DisplayDialog("Meshy Reimport Failed", ex.Message, "OK");
            }
        }

        [MenuItem("Tools/Meshy/Convert .meshy to GLB...")]
        public static void ConvertOne()
        {
            string path = EditorUtility.OpenFilePanel("Select Meshy model", "", "meshy");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string output = Path.ChangeExtension(path, ".glb");
                byte[] glb = DecodeFileForEditor(path);
                File.WriteAllBytes(output, glb);
                AssetDatabase.Refresh();
                Debug.Log($"Meshy import complete: {output}");
                EditorUtility.DisplayDialog(
                    "Meshy Importer",
                    "Converted successfully.\n\n" + output +
                    "\n\nUnity will now import the GLB using your installed glTF importer.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Meshy Importer", ex.Message, "OK");
            }
        }

        [MenuItem("Tools/Meshy/Convert All .meshy In Assets")]
        public static void ConvertAll()
        {
            string[] files = Directory.GetFiles(Application.dataPath, "*.meshy",
                SearchOption.AllDirectories);

            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("Meshy Importer",
                    "No .meshy files were found inside Assets.", "OK");
                return;
            }

            int converted = 0;
            foreach (string file in files)
            {
                try
                {
                    File.WriteAllBytes(Path.ChangeExtension(file, ".glb"), DecodeFileForEditor(file));
                    converted++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed: {file}\n{ex.Message}");
                }
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Meshy Importer",
                $"Converted {converted} of {files.Length} .meshy files.",
                "OK");
        }

        private static string ProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('\\', Path.DirectorySeparatorChar));
        }

        public static byte[] DecodeFileForEditor(string path)
        {
            string fullPath = path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? ProjectPath(path) : path;
            byte[] data = File.ReadAllBytes(fullPath);
            if (data.Length < 32 + 8192 + 16)
                throw new InvalidDataException("The .meshy file is too small.");

            if (Encoding.ASCII.GetString(data, 0, 8) != "MESHY.AI")
                throw new InvalidDataException("Missing MESHY.AI header.");

            byte[] nonce = new byte[12];
            Buffer.BlockCopy(data, 10, nonce, 0, 12);

            byte[] encrypted = new byte[8192];
            Buffer.BlockCopy(data, 32, encrypted, 0, 8192);

            byte[] tail = new byte[data.Length - (32 + 8192 + 16)];
            Buffer.BlockCopy(data, 32 + 8192 + 16, tail, 0, tail.Length);

            byte[] first = AesCtr(encrypted,
                Encoding.UTF8.GetBytes("JSON{\"accessors\":[{\"bufferView\":"),
                nonce);

            byte[] glb = new byte[first.Length + tail.Length];
            Buffer.BlockCopy(first, 0, glb, 0, first.Length);
            Buffer.BlockCopy(tail, 0, glb, first.Length, tail.Length);

            if (glb.Length < 12 || Encoding.ASCII.GetString(glb, 0, 4) != "glTF")
                throw new InvalidDataException(
                    "Decoded data is not a GLB. Meshy may have changed its format.");

            Buffer.BlockCopy(BitConverter.GetBytes((uint)glb.Length), 0, glb, 8, 4);
            return glb;
        }

        private static byte[] AesCtr(byte[] data, byte[] key, byte[] nonce)
        {
            byte[] roundKeys = ExpandKey(key);
            byte[] counter = new byte[16];
            Buffer.BlockCopy(nonce, 0, counter, 0, 12);
            counter[15] = 2;

            byte[] output = new byte[data.Length];

            for (int offset = 0; offset < data.Length; offset += 16)
            {
                byte[] stream = EncryptBlock(counter, roundKeys);
                int count = Math.Min(16, data.Length - offset);
                for (int i = 0; i < count; i++)
                    output[offset + i] = (byte)(data[offset + i] ^ stream[i]);

                for (int i = 15; i >= 0; i--)
                {
                    counter[i]++;
                    if (counter[i] != 0) break;
                }
            }
            return output;
        }

        private static readonly byte[] SBox = new byte[] {
            99,124,119,123,242,107,111,197,48,1,103,43,254,215,171,118,
            202,130,201,125,250,89,71,240,173,212,162,175,156,164,114,192,
            183,253,147,38,54,63,247,204,52,165,229,241,113,216,49,21,
            4,199,35,195,24,150,5,154,7,18,128,226,235,39,178,117,
            9,131,44,26,27,110,90,160,82,59,214,179,41,227,47,132,
            83,209,0,237,32,252,177,91,106,203,190,57,74,76,88,207,
            208,239,170,251,67,77,51,133,69,249,2,127,80,60,159,168,
            81,163,64,143,146,157,56,245,188,182,218,33,16,255,243,210,
            205,12,19,236,95,151,68,23,196,167,126,61,100,93,25,
            115,96,129,79,220,34,42,144,136,70,238,184,20,222,94,11,
            219,224,50,58,10,73,6,36,92,194,211,172,98,145,149,228,
            121,231,200,55,109,141,213,78,169,108,86,244,234,101,122,
            174,8,186,120,37,46,28,166,180,198,232,221,116,31,75,
            189,139,138,112,62,181,102,72,3,246,14,97,53,87,185,134,
            193,29,158,225,248,152,17,105,217,142,148,155,30,135,
            233,206,85,40,223,140,161,137,13,191,230,66,104,65,
            153,45,15,176,84,187,22
        };

        private static readonly byte[] Rcon =
            { 0,1,2,4,8,16,32,64,128,27,54,108,216,171,77 };

        private static byte[] ExpandKey(byte[] key)
        {
            byte[] words = new byte[240];
            Buffer.BlockCopy(key, 0, words, 0, 32);
            int bytes = 32, rcon = 1;
            byte[] temp = new byte[4];

            while (bytes < 240)
            {
                for (int i = 0; i < 4; i++) temp[i] = words[bytes - 4 + i];

                if (bytes % 32 == 0)
                {
                    byte t = temp[0];
                    temp[0] = temp[1]; temp[1] = temp[2];
                    temp[2] = temp[3]; temp[3] = t;
                    for (int i = 0; i < 4; i++) temp[i] = SBox[temp[i]];
                    temp[0] ^= Rcon[rcon++];
                }
                else if (bytes % 32 == 16)
                {
                    for (int i = 0; i < 4; i++) temp[i] = SBox[temp[i]];
                }

                for (int i = 0; i < 4; i++)
                {
                    words[bytes] = (byte)(words[bytes - 32] ^ temp[i]);
                    bytes++;
                }
            }
            return words;
        }

        private static byte[] EncryptBlock(byte[] input, byte[] rk)
        {
            byte[] s = new byte[16];
            Buffer.BlockCopy(input, 0, s, 0, 16);
            AddRoundKey(s, rk, 0);

            for (int round = 1; round <= 14; round++)
            {
                for (int i = 0; i < 16; i++) s[i] = SBox[s[i]];
                ShiftRows(s);
                if (round != 14) MixColumns(s);
                AddRoundKey(s, rk, round * 16);
            }
            return s;
        }

        private static void AddRoundKey(byte[] s, byte[] rk, int off)
        {
            for (int i = 0; i < 16; i++) s[i] ^= rk[off + i];
        }

        private static void ShiftRows(byte[] s)
        {
            byte[] t = (byte[])s.Clone();
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    s[r + 4 * c] = t[r + 4 * ((c + r) % 4)];
        }

        private static byte GMul(byte a, byte b)
        {
            int aa = a, bb = b, r = 0;
            for (int i = 0; i < 8; i++)
            {
                if ((bb & 1) != 0) r ^= aa;
                aa = (aa & 0x80) != 0 ? ((aa << 1) ^ 0x11B) : aa << 1;
                bb >>= 1;
            }
            return (byte)(r & 255);
        }

        private static void MixColumns(byte[] s)
        {
            for (int c = 0; c < 4; c++)
            {
                int i = c * 4;
                byte a0 = s[i], a1 = s[i + 1], a2 = s[i + 2], a3 = s[i + 3];
                s[i] = (byte)(GMul(a0,2)^GMul(a1,3)^a2^a3);
                s[i+1] = (byte)(a0^GMul(a1,2)^GMul(a2,3)^a3);
                s[i+2] = (byte)(a0^a1^GMul(a2,2)^GMul(a3,3));
                s[i+3] = (byte)(GMul(a0,3)^a1^a2^GMul(a3,2));
            }
        }
    }
}
