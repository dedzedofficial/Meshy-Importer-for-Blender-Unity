using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    internal static class MeshyEditableCopy
    {
        internal static void Create(string sourcePath)
        {
            string folder = null;
            GameObject instance = null;
            try
            {
                var source = MeshySupport.LoadSource(sourcePath);
                var root = AssetDatabase.LoadMainAssetAtPath(sourcePath) as GameObject;
                if (source == null || !source.NativeSuccess || MeshySupport.IsFailure(source.Status) || root == null)
                    throw new InvalidOperationException("Create an editable copy after a successful native import. For a fallback model, use its GLB importer.");
                const string parent = "Assets/MeshyCustom";
                if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder("Assets", "MeshyCustom");
                folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + SafeName(root.name));
                string created = AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                if (string.IsNullOrEmpty(created)) throw new IOException("Could not create editable asset folder.");
                // Resolve the actual created folder in case Unity normalizes the name.
                folder = AssetDatabase.GUIDToAssetPath(created);
                var copies = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
                var originals = AssetDatabase.LoadAllAssetsAtPath(sourcePath);
                // Save textures/meshes first, then materials so every texture link is rewired.
                foreach (var original in originals)
                    if (original is Texture2D || original is Mesh)
                        SaveCopy(original, folder, copies);
                foreach (var original in originals)
                    if (original is Material material)
                    {
                        var copy = new Material(material) { name = material.name, hideFlags = HideFlags.None };
                        foreach (string property in copy.GetTexturePropertyNames())
                        {
                            var texture = copy.GetTexture(property);
                            if (texture != null && copies.TryGetValue(texture, out var replacement)) copy.SetTexture(property, (Texture)replacement);
                        }
                        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(copy.name) + ".mat");
                        AssetDatabase.CreateAsset(copy, path); copies[original] = copy;
                    }
                instance = UnityEngine.Object.Instantiate(root);
                instance.name = root.name;
                foreach (var transform in instance.GetComponentsInChildren<Transform>(true)) transform.gameObject.hideFlags = HideFlags.None;
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && copies.TryGetValue(filter.sharedMesh, out var filterMesh)) filter.sharedMesh = (Mesh)filterMesh;
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.sharedMesh != null && copies.TryGetValue(skin.sharedMesh, out var skinMesh)) skin.sharedMesh = (Mesh)skinMesh;
                foreach (var collider in instance.GetComponentsInChildren<MeshCollider>(true))
                    if (collider.sharedMesh != null && copies.TryGetValue(collider.sharedMesh, out var colliderMesh)) collider.sharedMesh = (Mesh)colliderMesh;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null && copies.TryGetValue(materials[i], out var replacement)) materials[i] = (Material)replacement;
                    renderer.sharedMaterials = materials;
                }
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(root.name) + ".prefab");
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool success);
                if (!success) throw new IOException("Unity could not save the editable prefab.");
                AssetDatabase.SaveAssets();
                foreach (string dependency in AssetDatabase.GetDependencies(prefabPath, true))
                    if (string.Equals(dependency, sourcePath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The copied prefab still has a dependency on the source model; inspect its references before treating it as independent.");
                Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(prefabPath);
                EditorUtility.DisplayDialog("Editable Copy Created", "Saved to " + folder +
                    ".\n\nThese copied meshes, textures, materials and prefab are not regenerated by Meshy. Existing external material mappings remain external references.\n\n" +
                    "To keep your imported model linked to a copied material, assign its .mat in the original model's Material Overrides and press Apply.", "OK");
            }
            catch (Exception ex)
            {
                MeshyImporterMenu.ShowError("Editable Copy", ex.Message + (folder == null ? "" : "\nAny files already created are in " + folder + ". Existing assets were not overwritten."));
            }
            finally { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static void SaveCopy(UnityEngine.Object original, string folder, Dictionary<UnityEngine.Object, UnityEngine.Object> copies)
        {
            var copy = UnityEngine.Object.Instantiate(original);
            copy.name = original.name; copy.hideFlags = HideFlags.None;
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(copy.name) + ".asset");
            AssetDatabase.CreateAsset(copy, path); copies[original] = copy;
        }
        private static string SafeName(string name)
        {
            foreach (char c in "<>:\"/\\|?*") name = name.Replace(c, '_');
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "Model" : name;
        }
    }
}
