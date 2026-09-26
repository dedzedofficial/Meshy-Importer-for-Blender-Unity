using System;
using System.IO;
using System.Text;
using FISHHWB.MeshyImporter.Editor;
class Program
{
    private static int passed;
    private static byte[] Glb(string json, byte[] binary = null)
    {
        while (Encoding.UTF8.GetByteCount(json) % 4 != 0) json += " ";
        byte[] js = Encoding.UTF8.GetBytes(json);
        using (var memory = new MemoryStream())
        using (var writer = new BinaryWriter(memory))
        {
            writer.Write(Encoding.ASCII.GetBytes("glTF")); writer.Write(2);
            writer.Write(12 + 8 + js.Length + (binary == null ? 0 : 8 + binary.Length));
            writer.Write(js.Length); writer.Write(0x4E4F534A); writer.Write(js);
            if (binary != null) { writer.Write(binary.Length); writer.Write(0x004E4942); writer.Write(binary); }
            return memory.ToArray();
        }
    }
    private static MeshyPreflight.Report Scan(string fields) => MeshyPreflight.Scan(Glb("{\"asset\":{\"version\":\"2.0\"}" + fields + "}"), new[] { "EXT_meshopt_compression" });
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
    static void Main(string[] args)
    {
        Check(!Scan("").NeedsFallback, "minimal glTF");
        Check(Scan(",\"animations\":[{}]").NeedsFallback, "animation preserved via fallback");
        Check(Scan(",\"extensionsRequired\":[\"KHR_draco_mesh_compression\"]").NeedsFallback, "required extension routes to fallback");
        Check(!Scan(",\"extensionsRequired\":[\"EXT_meshopt_compression\"]").NeedsFallback, "supported extension accepted");
        Check(Scan(",\"accessors\":[{\"componentType\":5126,\"count\":1,\"type\":\"VEC3\",\"sparse\":{}}]").NeedsFallback, "sparse accessors route to fallback");
        Check(Scan(",\"images\":[{\"uri\":\"texture.png\"}]").NeedsFallback, "external texture is explicit");
        Reject(() => MeshyPreflight.Scan(new byte[5], new string[0]), "truncated header");
        byte[] wrongLength = Glb("{\"asset\":{\"version\":\"2.0\"}}"); wrongLength[8] = 0;
        Reject(() => MeshyPreflight.Scan(wrongLength, new string[0]), "wrong declared length");
        Reject(() => Scan(",\"nodes\":[{\"children\":[0]}]"), "cyclic hierarchy");
        Reject(() => Scan(",\"nodes\":[{\"children\":[2]},{\"children\":[2]},{}]"), "multiple parents");
        Reject(() => Scan(",\"nodes\":[{\"mesh\":8}]"), "bad mesh reference");
        Reject(() => Scan(",\"buffers\":[{\"byteLength\":16}]"), "missing BIN data");
        Reject(() => Scan(",\"accessors\":[{\"componentType\":5126,\"count\":-1,\"type\":\"VEC3\"}]"), "negative accessor count");
        var first = MeshyMiniJson.GetArray(Scan(",\"materials\":[{\"name\":\"Body\"},{\"name\":\"Eyes\"}]").Root, "materials");
        var reordered = MeshyMiniJson.GetArray(Scan(",\"materials\":[{\"name\":\"Eyes\"},{\"name\":\"Body\"}]").Root, "materials");
        Check(MeshyPreflight.MaterialKey(first, 0) == MeshyPreflight.MaterialKey(reordered, 1), "unique-name remap survives reordering");
        var duplicates = MeshyMiniJson.GetArray(Scan(",\"materials\":[{\"name\":\"Same\"},{\"name\":\"Same\"}]").Root, "materials");
        Check(MeshyPreflight.MaterialKey(duplicates, 0) != MeshyPreflight.MaterialKey(duplicates, 1), "duplicate names remain distinct");
        Check(MeshyPreflight.MaterialKey(first, -1) == "default", "default material slot");
        string accessor = ",\"accessors\":[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}]";
        Check(Scan(accessor + ",\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"targets\":[{}]}]}]").NeedsFallback, "morph targets preserved via fallback");
        Check(Scan(accessor + ",\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"mode\":1}]}]").NeedsFallback, "line topology preserved via fallback");
        string ranged = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":16}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":16}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}]}";
        Reject(() => MeshyPreflight.Scan(Glb(ranged, new byte[16]), new string[0]), "accessor exceeds view");
        string oversizedView = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":16}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":20}]}";
        Reject(() => MeshyPreflight.Scan(Glb(oversizedView, new byte[16]), new string[0]), "view exceeds buffer");
        string matrix = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":12}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":12}],\"accessors\":[{\"bufferView\":0,\"componentType\":5121,\"count\":1,\"type\":\"MAT3\"}]}";
        Check(!MeshyPreflight.Scan(Glb(matrix, new byte[12]), new string[0]).NeedsFallback, "aligned byte matrix accessor accepted");
        if (args.Length > 0)
        {
            var fixture = MeshyPreflight.Scan(File.ReadAllBytes(args[0]), new[] { "EXT_meshopt_compression", "KHR_mesh_quantization", "EXT_texture_webp" });
            Check(!fixture.NeedsFallback && fixture.DeclaredVertices == 81 && fixture.DeclaredTriangles == 128, "realistic meshopt fixture range checks");
        }
        Console.WriteLine(passed + " preflight checks passed.");
    }
}
