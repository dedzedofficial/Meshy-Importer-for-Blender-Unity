using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>CPU-only structural scan; does not allocate Unity model/texture objects.</summary>
    internal static class MeshyPreflight
    {
        internal sealed class Report
        {
            internal Dictionary<string, object> Root;
            internal readonly List<string> Warnings = new List<string>();
            internal readonly List<string> FallbackReasons = new List<string>();
            internal long DeclaredVertices, DeclaredTriangles;
            internal int Nodes, Materials, Images, Animations;
            internal readonly SortedSet<int> MaterialIndices = new SortedSet<int>();
            internal bool NeedsFallback => FallbackReasons.Count != 0;
            internal string Text => "Preflight: " + Nodes + " nodes, " + Materials + " materials, " + Images + " images; " +
                DeclaredVertices.ToString("N0") + " declared vertices, " + DeclaredTriangles.ToString("N0") + " triangle estimate." +
                (Warnings.Count == 0 ? "\nNo structural warnings found." : "\n" + string.Join("\n", Warnings)) +
                (NeedsFallback ? "\nExternal importer required: " + string.Join("; ", FallbackReasons) + "." : "");
        }

        internal static Report Scan(byte[] glb, IEnumerable<string> supportedExtensions)
        {
            if (glb == null || glb.Length < 20 || Encoding.ASCII.GetString(glb, 0, 4) != "glTF")
                throw new InvalidDataException("Preflight: missing or truncated GLB header.");
            if (BitConverter.ToUInt32(glb, 4) != 2 || BitConverter.ToUInt32(glb, 8) != glb.Length)
                throw new InvalidDataException("Preflight: invalid GLB version or total length.");
            string json = null;
            long binLength = 0;
            bool hasBin = false;
            int offset = 12;
            while (offset < glb.Length)
            {
                if (glb.Length - offset < 8) throw new InvalidDataException("Preflight: truncated chunk header.");
                uint length = BitConverter.ToUInt32(glb, offset);
                uint type = BitConverter.ToUInt32(glb, offset + 4);
                if (length % 4 != 0 || length > glb.Length - offset - 8)
                    throw new InvalidDataException("Preflight: invalid chunk length or alignment.");
                if (offset == 12 && type != 0x4E4F534A) throw new InvalidDataException("Preflight: first chunk must be JSON.");
                if (type == 0x4E4F534A)
                {
                    if (json != null) throw new InvalidDataException("Preflight: duplicate JSON chunk.");
                    json = Encoding.UTF8.GetString(glb, offset + 8, (int)length);
                }
                if (type == 0x004E4942)
                {
                    if (hasBin) throw new InvalidDataException("Preflight: duplicate BIN chunk.");
                    hasBin = true; binLength = length;
                }
                offset += 8 + (int)length;
            }
            var root = json == null ? null : MeshyMiniJson.AsObject(MeshyMiniJson.Parse(json));
            if (root == null || MeshyMiniJson.GetString(MeshyMiniJson.Get(root, "asset"), "version") != "2.0")
                throw new InvalidDataException("Preflight: expected a glTF 2.0 JSON object.");
            var r = new Report { Root = root };
            var buffers = Array(root, "buffers");
            var views = Array(root, "bufferViews");
            var accessors = Array(root, "accessors");
            var materials = Array(root, "materials");
            var meshes = Array(root, "meshes");
            var nodes = Array(root, "nodes");
            r.Nodes = nodes.Count; r.Materials = materials.Count; r.Images = Array(root, "images").Count;
            r.Animations = Array(root, "animations").Count;
            var supported = new HashSet<string>(supportedExtensions ?? new string[0], StringComparer.Ordinal);
            foreach (object item in Array(root, "extensionsRequired"))
            {
                string extension = item as string;
                if (extension == null) throw new InvalidDataException("Preflight: invalid required extension name.");
                if (!supported.Contains(extension)) Add(r.FallbackReasons, extension);
            }
            if (r.Animations > 0) Add(r.FallbackReasons, "animation clips");
            for (int i = 0; i < buffers.Count; i++)
            {
                var b = Object(buffers[i], "buffer");
                long size = NonNegative(b, "byteLength");
                string uri = MeshyMiniJson.GetString(b, "uri");
                bool virtualFallback = MeshyMiniJson.GetBool(MeshyMiniJson.Get(MeshyMiniJson.Get(b, "extensions"), "EXT_meshopt_compression"), "fallback");
                if (uri != null && !uri.StartsWith("data:", StringComparison.Ordinal))
                    Add(r.FallbackReasons, "external buffer files (must be available beside the generated GLB)");
                else if (uri == null && !virtualFallback && (i != 0 || !hasBin || size > binLength))
                    throw new InvalidDataException("Preflight: buffer " + i + " exceeds the embedded BIN data.");
            }
            foreach (object item in views)
            {
                var v = Object(item, "bufferView");
                int b = Index(v, "buffer", buffers.Count);
                long start = NonNegative(v, "byteOffset", 0), length = NonNegative(v, "byteLength");
                if (start + length > NonNegative(Object(buffers[b], "buffer"), "byteLength"))
                    throw new InvalidDataException("Preflight: bufferView exceeds its buffer.");
                var compression = MeshyMiniJson.Get(MeshyMiniJson.Get(v, "extensions"), "EXT_meshopt_compression");
                if (compression != null)
                {
                    int source = Index(compression, "buffer", buffers.Count);
                    if (NonNegative(compression, "byteOffset", 0) + NonNegative(compression, "byteLength") >
                        NonNegative(Object(buffers[source], "buffer"), "byteLength"))
                        throw new InvalidDataException("Preflight: meshopt source exceeds its buffer.");
                }
            }
            foreach (object item in accessors)
            {
                var a = Object(item, "accessor");
                long count = NonNegative(a, "count");
                int component = MeshyMiniJson.GetInt(a, "componentType");
                int bytes = component == 5120 || component == 5121 ? 1 : component == 5122 || component == 5123 ? 2 : component == 5125 || component == 5126 ? 4 : 0;
                string type = MeshyMiniJson.GetString(a, "type");
                int size = type == "SCALAR" ? 1 : type == "VEC2" ? 2 : type == "VEC3" ? 3 : type == "VEC4" || type == "MAT2" ? 4 : type == "MAT3" ? 9 : type == "MAT4" ? 16 : 0;
                if (bytes == 0 || size == 0) throw new InvalidDataException("Preflight: invalid accessor type.");
                if (MeshyMiniJson.Has(a, "sparse")) Add(r.FallbackReasons, "sparse accessors");
                if (MeshyMiniJson.Has(a, "bufferView"))
                {
                    var v = Object(views[Index(a, "bufferView", views.Count)], "bufferView");
                    // Matrix columns have 4-byte alignment, including byte/short matrices.
                    int columns = type == "MAT2" ? 2 : type == "MAT3" ? 3 : type == "MAT4" ? 4 : 0;
                    long element = columns == 0 ? bytes * size : ((columns * bytes + 3) / 4 * 4) * columns;
                    long stride = NonNegative(v, "byteStride", element);
                    if (stride < element) throw new InvalidDataException("Preflight: accessor stride is smaller than its element.");
                    long needed = checked(NonNegative(a, "byteOffset", 0) + (count == 0 ? 0 : checked((count - 1) * stride + element)));
                    if (needed > NonNegative(v, "byteLength")) throw new InvalidDataException("Preflight: accessor exceeds its bufferView.");
                }
            }
            bool defaultMaterial = false;
            foreach (object item in meshes)
                foreach (object p in Array(Object(item, "mesh"), "primitives"))
                {
                    var primitive = Object(p, "primitive");
                    var attributes = MeshyMiniJson.Get(primitive, "attributes");
                    if (attributes == null || !attributes.ContainsKey("POSITION")) throw new InvalidDataException("Preflight: a primitive has no POSITION accessor.");
                    foreach (var attribute in attributes) ValidateIndex(attribute.Value, accessors.Count, attribute.Key);
                    int position = Index(attributes, "POSITION", accessors.Count);
                    long vertices = NonNegative(Object(accessors[position], "accessor"), "count");
                    r.DeclaredVertices = checked(r.DeclaredVertices + vertices);
                    long indices = MeshyMiniJson.Has(primitive, "indices")
                        ? NonNegative(Object(accessors[Index(primitive, "indices", accessors.Count)], "accessor"), "count") : vertices;
                    int mode = MeshyMiniJson.GetInt(primitive, "mode", 4);
                    if (mode != 4) Add(r.FallbackReasons, "non-triangle primitive topology");
                    else r.DeclaredTriangles += indices / 3;
                    if (Array(primitive, "targets").Count > 0) Add(r.FallbackReasons, "blendshapes/morph targets");
                    if (MeshyMiniJson.Has(primitive, "material")) r.MaterialIndices.Add(Index(primitive, "material", materials.Count));
                    else { defaultMaterial = true; r.MaterialIndices.Add(-1); }
                }
            ValidateNodes(nodes, meshes.Count);
            foreach (object image in Array(root, "images"))
            {
                var img = Object(image, "image");
                if (MeshyMiniJson.Has(img, "bufferView")) Index(img, "bufferView", views.Count);
                string uri = MeshyMiniJson.GetString(img, "uri");
                if (uri != null && !uri.StartsWith("data:", StringComparison.Ordinal))
                    Add(r.FallbackReasons, "external images (must be available beside the generated GLB)");
            }
            if (r.DeclaredVertices > 1000000) r.Warnings.Add("Large model: more than one million declared vertices; importing may take substantial memory.");
            if (glb.Length > 100 * 1024 * 1024) r.Warnings.Add("Large payload: over 100 MiB before decoded textures and meshes.");
            if (meshes.Count == 0) r.Warnings.Add("This file contains no mesh definitions.");
            if (defaultMaterial) r.Warnings.Add("Some primitives use the default material.");
            return r;
        }

        internal static string MaterialKey(List<object> materials, int index)
        {
            if (index < 0) return "default";
            string name = MeshyMiniJson.GetString(Object(materials[index], "material"), "name", "");
            int matches = 0;
            foreach (object material in materials)
                if (MeshyMiniJson.GetString(Object(material, "material"), "name", "") == name) matches++;
            return name.Length > 0 && matches == 1 ? "name:" + name : "slot:" + index + ":" + name;
        }
        private static void ValidateNodes(List<object> nodes, int meshCount)
        {
            var parents = new int[nodes.Count]; var edges = new List<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = Object(nodes[i], "node"); edges[i] = new List<int>();
                if (MeshyMiniJson.Has(node, "mesh")) Index(node, "mesh", meshCount);
                foreach (object c in Array(node, "children"))
                {
                    int child = ValidateIndex(c, nodes.Count, "child"); edges[i].Add(child);
                    if (++parents[child] > 1) throw new InvalidDataException("Preflight: node has multiple parents.");
                }
            }
            var queue = new Queue<int>();
            for (int i = 0; i < nodes.Count; i++) if (parents[i] == 0) queue.Enqueue(i);
            int visited = 0;
            while (queue.Count > 0) { int n = queue.Dequeue(); visited++; foreach (int child in edges[n]) if (--parents[child] == 0) queue.Enqueue(child); }
            if (visited != nodes.Count) throw new InvalidDataException("Preflight: cyclic node hierarchy.");
        }
        private static List<object> Array(Dictionary<string, object> obj, string key) => MeshyMiniJson.GetArray(obj, key) ?? new List<object>();
        private static Dictionary<string, object> Object(object value, string label) => MeshyMiniJson.AsObject(value) ?? throw new InvalidDataException("Preflight: invalid " + label + " object.");
        private static long NonNegative(Dictionary<string, object> obj, string key, long fallback = -1)
        {
            double n = MeshyMiniJson.GetNumber(obj, key, fallback);
            if (double.IsNaN(n) || double.IsInfinity(n) || n < 0 || n > int.MaxValue || n != Math.Floor(n))
                throw new InvalidDataException("Preflight: invalid " + key + ".");
            return (long)n;
        }
        private static int Index(Dictionary<string, object> obj, string key, int count)
        { if (!obj.TryGetValue(key, out var value)) throw new InvalidDataException("Preflight: missing " + key + "."); return ValidateIndex(value, count, key); }
        private static int ValidateIndex(object value, int count, string key)
        { double n = MeshyMiniJson.AsNumber(value, -1); if (double.IsNaN(n) || n < 0 || n >= count || n != Math.Floor(n)) throw new InvalidDataException("Preflight: invalid " + key + " reference."); return (int)n; }
        private static void Add(List<string> items, string item) { if (!items.Contains(item)) items.Add(item); }
    }
}
