using System;
using System.IO;
using FISHHWB.MeshyImporter.Editor;
namespace UnityEngine { public static class Application { public static string dataPath; } }
class Program
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "meshy-packages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "Packages"));
        UnityEngine.Application.dataPath = Path.Combine(root, "Assets");
        string manifest = Path.Combine(root, "Packages/manifest.json");
        string locked = Path.Combine(root, "Packages/packages-lock.json");
        try
        {
            File.WriteAllText(manifest, "{\"dependencies\":{}}");
            Check(!MeshyGltfPackages.Inspect().HasFallback, "native-only install");
            File.WriteAllText(manifest, "{\"dependencies\":{},\"description\":\"org.khronos.unitygltf\"}");
            Check(!MeshyGltfPackages.Inspect().HasFallback, "ignore package name in unrelated metadata");
            File.WriteAllText(manifest, "{\"dependencies\":{\"com.unity.cloud.gltfast\":\"6.0.0\"}}");
            Check(MeshyGltfPackages.Inspect().HasFallback && !MeshyGltfPackages.Inspect().Conflict, "single variant");
            File.WriteAllText(locked, "{\"dependencies\":{\"com.atteneder.gltfast\":{\"version\":\"5.0.0\"}}}");
            Check(MeshyGltfPackages.Inspect().Conflict, "transitive collision");
            File.Delete(locked);
            string embedded = Path.Combine(root, "Packages/custom-folder");
            Directory.CreateDirectory(embedded);
            File.WriteAllText(Path.Combine(embedded, "package.json"), "{\"name\":\"com.atteneder.gltfast\"}");
            Check(MeshyGltfPackages.Inspect().Conflict, "embedded collision with arbitrary folder name");
            Directory.Delete(embedded, true);
            File.WriteAllText(locked, "{\"dependencies\":{\"org.khronos.unitygltf\":{}}}");
            Check(MeshyGltfPackages.Inspect().Has(MeshyGltfPackages.Khronos), "indirect UnityGLTF detection");
            File.WriteAllText(manifest, "{broken");
            Check(MeshyGltfPackages.Inspect().Problem != null, "malformed manifest produces diagnostic");
        }
        finally { Directory.Delete(root, true); }
    }
}
