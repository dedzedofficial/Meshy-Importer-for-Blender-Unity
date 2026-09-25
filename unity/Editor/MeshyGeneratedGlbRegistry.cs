using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    internal static class MeshyPaths
    {
        /// <summary>Absolute path for an "Assets/..." path (other paths are returned unchanged).</summary>
        public static string ProjectPath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath)) return assetPath;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('\\', Path.DirectorySeparatorChar));
        }
    }

    /// <summary>
    /// Remembers which .glb files the importer itself wrote (the rare fallback path), so
    /// cleanup only ever deletes those. Before 1.4.1 any "&lt;Name&gt;.glb" sitting next to a
    /// "&lt;Name&gt;.meshy" was deleted on import, move or delete -- including a user's own GLB
    /// or one made with Tools > Meshy > Convert. A file is deleted only when it still has
    /// the exact size and SHA-256 recorded when it was written; with no record (e.g. the
    /// Library folder was wiped) nothing is deleted.
    /// </summary>
    internal static class MeshyGeneratedGlbRegistry
    {
        [Serializable]
        private sealed class Entry
        {
            public string path;
            public long size;
            public string sha256;
        }

        [Serializable]
        private sealed class Store
        {
            public List<Entry> entries = new List<Entry>();
        }

        private static string StorePath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "FISHHWB.MeshyImporter", "generated-glbs.json");

        private static Store Load()
        {
            try
            {
                if (File.Exists(StorePath))
                    return JsonUtility.FromJson<Store>(File.ReadAllText(StorePath)) ?? new Store();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Meshy Importer: could not read the generated-GLB registry: " + ex.Message);
            }
            return new Store();
        }

        private static void Save(Store store)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                File.WriteAllText(StorePath, JsonUtility.ToJson(store, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Meshy Importer: could not write the generated-GLB registry: " + ex.Message);
            }
        }

        private static string Normalize(string assetPath) => assetPath.Replace('\\', '/');

        private static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }

        /// <summary>True when the file at assetPath is one this importer wrote and has not been changed since.</summary>
        public static bool IsGenerated(string assetPath)
        {
            string full = MeshyPaths.ProjectPath(assetPath);
            if (!File.Exists(full)) return false;
            var entry = Load().entries.Find(e => e.path == Normalize(assetPath));
            if (entry == null) return false;
            var info = new FileInfo(full);
            if (info.Length != entry.size) return false;
            return Hash(File.ReadAllBytes(full)) == entry.sha256;
        }

        /// <summary>Write a generated GLB and record it. Never overwrites a file the importer did not write.</summary>
        public static bool TryWrite(string assetPath, byte[] data)
        {
            string full = MeshyPaths.ProjectPath(assetPath);
            if (File.Exists(full) && !IsGenerated(assetPath)) return false;
            File.WriteAllBytes(full, data);
            var store = Load();
            store.entries.RemoveAll(e => e.path == Normalize(assetPath));
            store.entries.Add(new Entry { path = Normalize(assetPath), size = data.LongLength, sha256 = Hash(data) });
            Save(store);
            return true;
        }

        /// <summary>Delete assetPath (and its .meta) only if the importer generated it. Plain file IO only.</summary>
        public static bool DeleteIfGenerated(string assetPath)
        {
            if (!IsGenerated(assetPath)) return false;
            string full = MeshyPaths.ProjectPath(assetPath);
            try
            {
                File.Delete(full);
                if (File.Exists(full + ".meta")) File.Delete(full + ".meta");
                var store = Load();
                store.entries.RemoveAll(e => e.path == Normalize(assetPath));
                Save(store);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Meshy Importer: could not remove generated GLB " + assetPath + ": " + ex.Message);
                return false;
            }
        }
    }
}
