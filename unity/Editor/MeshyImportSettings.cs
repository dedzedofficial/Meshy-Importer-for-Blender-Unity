using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    [Serializable]
    public sealed class MeshyMaterialRemap
    {
        public string key;
        public Material material;
        public string materialGuid;
        public long materialFileId;
    }

    [Serializable]
    internal sealed class MeshySettingsSnapshot
    {
        public int schema = 1;
        public float scale = 1f;
        public bool colliders, optimize = true, repairUvs = true;
        public List<MaterialReference> materials = new List<MaterialReference>();
        [Serializable] internal sealed class MaterialReference { public string key, guid; public long fileId; }
    }

    internal static class MeshySettingsMemory
    {
        private static string SnapshotPath(string assetPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return null;
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "ProjectSettings", "MeshyImporter", "LastSuccessful", guid + ".json");
        }

        // Called on the main editor after import. Failed/fallback imports don't erase success.
        internal static void Remember(string assetPath, FISHHWB.MeshyImporter.MeshySourceAsset source)
        {
            if (source == null || !source.NativeSuccess || MeshySupport.IsFailure(source.Status) || string.IsNullOrEmpty(source.SuccessfulSettingsJson)) return;
            try
            {
                string path = SnapshotPath(assetPath);
                if (path == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path) && File.ReadAllText(path) == source.SuccessfulSettingsJson) return;
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, source.SuccessfulSettingsJson);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch (Exception ex) { Debug.LogWarning("Meshy Importer: could not remember settings: " + ex.Message); }
        }

        internal static MeshySettingsSnapshot Load(string assetPath)
        {
            string path = SnapshotPath(assetPath);
            if (path == null || !File.Exists(path)) throw new InvalidOperationException("No successful native import settings have been saved for this model yet.");
            var data = JsonUtility.FromJson<MeshySettingsSnapshot>(File.ReadAllText(path));
            if (data == null || data.schema != 1 || float.IsNaN(data.scale) || float.IsInfinity(data.scale) || data.scale <= 0f)
                throw new InvalidDataException("Saved import settings are invalid or from an unsupported schema.");
            return data;
        }

        internal static Material Resolve(MeshySettingsSnapshot.MaterialReference reference)
        {
            string path = AssetDatabase.GUIDToAssetPath(reference.guid);
            if (!string.IsNullOrEmpty(path))
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (obj is Material material && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long id) && id == reference.fileId)
                        return material;
            throw new InvalidOperationException("A saved material is missing for '" + reference.key + "'. Restore that material first; current settings have been left intact.");
        }
    }
}
