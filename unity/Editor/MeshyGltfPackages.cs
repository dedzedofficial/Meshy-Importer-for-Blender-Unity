using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    // No reference to either glTFast assembly: diagnostics must work with neither installed.
    internal sealed class MeshyGltfPackages
    {
        internal const string Legacy = "com.atteneder.gltfast";
        internal const string Unity = "com.unity.cloud.gltfast";
        internal const string Khronos = "org.khronos.unitygltf";
        private readonly HashSet<string> installed = new HashSet<string>(StringComparer.Ordinal);
        internal string Error { get; private set; }
        internal bool Conflict => Has(Legacy) && Has(Unity);
        internal bool HasFallback => Has(Legacy) || Has(Unity) || Has(Khronos);
        internal bool Has(string id) => installed.Contains(id);
        internal string Problem => Error != null ? "Could not inspect glTF packages: " + Error : Conflict
            ? "Both glTFast variants are installed (com.atteneder.gltfast and com.unity.cloud.gltfast). " +
              "They share asset GUIDs and cannot coexist. Keep the variant required by your other packages. " +
              "See the included UPGRADE_1.5.1.md and tools/repair_gltfast.py; do not edit package .meta GUIDs."
            : null;

        internal static MeshyGltfPackages Inspect()
        {
            var result = new MeshyGltfPackages();
            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                foreach (string file in new[] { "manifest.json", "packages-lock.json" })
                {
                    string path = Path.Combine(root, "Packages", file);
                    if (!File.Exists(path)) continue;
                    var json = MeshyMiniJson.AsObject(MeshyMiniJson.Parse(File.ReadAllText(path)));
                    var deps = json == null ? null : MeshyMiniJson.Get(json, "dependencies");
                    if (deps == null) throw new InvalidDataException(file + " has no dependencies object.");
                    foreach (var entry in deps) result.installed.Add(entry.Key);
                }
                // Embedded packages are not necessarily present in manifest.json.
                string packages = Path.Combine(root, "Packages");
                if (Directory.Exists(packages))
                    foreach (string directory in Directory.GetDirectories(packages))
                    {
                        string path = Path.Combine(directory, "package.json");
                        if (!File.Exists(path)) continue;
                        var json = MeshyMiniJson.AsObject(MeshyMiniJson.Parse(File.ReadAllText(path)));
                        string name = MeshyMiniJson.GetString(json, "name", "");
                        if (!string.IsNullOrEmpty(name)) result.installed.Add(name);
                    }
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }

        internal string Summary()
        {
            var sb = new StringBuilder();
            foreach (string id in new[] { Legacy, Unity, Khronos })
                sb.AppendLine(id + ": " + (Has(id) ? "present in project package configuration" : "not found"));
            if (Problem != null) sb.AppendLine(Problem);
            else if (Has(Khronos) && (Has(Legacy) || Has(Unity)))
                sb.AppendLine("Multiple GLB importers are present; check the GLB's importer override if import is ambiguous.");
            return sb.ToString().TrimEnd();
        }
    }
}
