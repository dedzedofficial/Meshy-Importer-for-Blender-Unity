using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    public static class MeshyImporterMenu
    {
        private const string RepositoryUrl =
            "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity";

        [MenuItem("Tools/Meshy/About FISHHWB Meshy Importer")]
        public static void About()
        {
            EditorUtility.DisplayDialog(
                "FISHHWB Meshy Importer",
                "Created by FISHHWB\\n\\n" +
                "Meshy .meshy -> GLB importer for Unity.\\n" +
                "UnityGLTF is installed automatically as a UPM Git dependency.\\n\\n" +
                RepositoryUrl,
                "OK");
        }

        private const string UnityGLTFUrl =
            "https://github.com/KhronosGroup/UnityGLTF.git#release/2.21.0";
[MenuItem("Tools/Meshy/Convert .meshy to GLB...")]
        public static void ConvertOne()
        {
            string path = EditorUtility.OpenFilePanel("Select Meshy model", "", "meshy");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string output = Path.ChangeExtension(path, ".glb");
                byte[] glb = Decode(path);
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
                    File.WriteAllBytes(Path.ChangeExtension(file, ".glb"), Decode(file));
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

        private static byte[] Decode(string path)
        {
            byte[] data = File.ReadAllBytes(path);
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
