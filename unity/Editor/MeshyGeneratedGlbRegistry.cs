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
                {
                    var store = JsonUtility.FromJson<Store>(File.ReadAllText(StorePath)) ?? new Store();
                    if (store.entries == null) store.entries = new List<Entry>();
                    store.entries.RemoveAll(e => e == null || string.IsNullOrEmpty(e.path));
                    return store;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Meshy Importer: could not read the generated-GLB registry: " + ex.Message);
            }
            return new Store();
        }

        // Import workers are separate processes; a C# lock would not serialize them.
        private static FileStream AcquireLock()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
            for (int attempt = 0; ; attempt++)
            {
                try { return new FileStream(StorePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException)
                {
                    if (attempt >= 100) throw;
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        private static void Save(Store store)
        {
            string temporary = StorePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(store, true));
                if (File.Exists(StorePath)) File.Replace(temporary, StorePath, null);
                else File.Move(temporary, StorePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string Normalize(string assetPath) => assetPath.Replace('\\', '/');

        private static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }

        internal static void Forget(string assetPath)
        {
            using (AcquireLock())
            {
                var store = Load();
                if (store.entries.RemoveAll(e => e.path == Normalize(assetPath)) > 0) Save(store);
            }
        }

        private static bool IsGenerated(string assetPath, Store store)
        {
            string full = MeshyPaths.ProjectPath(assetPath);
            if (!File.Exists(full)) return false;
            var entry = store.entries.Find(e => e.path == Normalize(assetPath));
            if (entry == null || new FileInfo(full).Length != entry.size) return false;
            return Hash(File.ReadAllBytes(full)) == entry.sha256;
        }

        public static bool IsGenerated(string assetPath)
        {
            using (AcquireLock()) return IsGenerated(assetPath, Load());
        }

        /// <summary>Only overwrites a file with matching recorded ownership and content.</summary>
        public static bool TryWrite(string assetPath, byte[] data)
        {
            using (AcquireLock())
            {
                var store = Load();
                string full = MeshyPaths.ProjectPath(assetPath);
                bool exists = File.Exists(full);
                if (exists && !IsGenerated(assetPath, store)) return false;
                if (!exists && File.Exists(full + ".meta")) return false;
                // Avoid rewriting identical data and triggering needless asset refreshes.
                string hash = Hash(data);
                var previous = store.entries.Find(e => e.path == Normalize(assetPath));
                if (exists && previous != null && previous.sha256 == hash) return true;
                using (var stream = new FileStream(full, exists ? FileMode.Create : FileMode.CreateNew, FileAccess.Write))
                    stream.Write(data, 0, data.Length);
                store.entries.RemoveAll(e => e.path == Normalize(assetPath));
                store.entries.Add(new Entry { path = Normalize(assetPath), size = data.LongLength, sha256 = hash });
                Save(store);
                return true;
            }
        }

        /// <summary>Plain disk IO; never removes an untracked or user-edited GLB.</summary>
        public static bool DeleteIfGenerated(string assetPath)
        {
            try
            {
                using (AcquireLock())
                {
                    var store = Load();
                    if (!IsGenerated(assetPath, store)) return false;
                    string full = MeshyPaths.ProjectPath(assetPath);
                    File.Delete(full);
                    if (File.Exists(full + ".meta")) File.Delete(full + ".meta");
                    store.entries.RemoveAll(e => e.path == Normalize(assetPath));
                    Save(store);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Meshy Importer: could not remove generated GLB " + assetPath + ": " + ex.Message);
                return false;
            }
        }
    }
}
