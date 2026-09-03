using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// Builds Unity Mesh/Material/Texture2D/GameObject objects directly from a
    /// decrypted .meshy payload's GLB bytes, without going through UnityGLTF or
    /// glTFast. Covers the common core of glTF 2.0 that Meshy exports use:
    /// triangle meshes, pbrMetallicRoughness materials with embedded textures,
    /// and skinning. Extensions this builder does not implement (chiefly
    /// EXT_meshopt_compression) are detected up front so the caller can fall
    /// back to the old .glb + external-importer path for just that file
    /// instead of failing silently.
    /// </summary>
    internal static class MeshyGltfBuilder
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>
        {
            "KHR_materials_emissive_strength",
            "KHR_texture_transform", // detected but not applied; harmless to ignore for a first pass
            // Meshy's exports commonly quantize POSITION/NORMAL/TANGENT/TEXCOORD_n to smaller
            // integer component types to shrink file size. The extension itself only relaxes
            // the spec's "must be FLOAT" restriction -- it adds no new data or transform of its
            // own. ReadAccessorFloats already reads every componentType (BYTE/UBYTE/SHORT/USHORT/
            // FLOAT) with the accessor's own `normalized` flag, and node TRS/matrix handling is
            // already generic, which is exactly what's needed to reconstruct real-world
            // coordinates from quantized data (the exporter bakes any compensating scale into
            // the node transform, per the standard glTF quantization pattern). So no extra
            // decode logic is required here -- just allow the file through.
            "KHR_mesh_quantization",
            // Geometry compression: decoded by MeshyMeshopt, ported from and verified
            // byte-exact against the reference meshoptimizer decoder (see that file).
            "EXT_meshopt_compression",
            // Texture compression: decoded by MeshyWebpVp8, a from-scratch VP8 lossy
            // (RIFF/WEBP "VP8 ") decoder ported from and verified byte-exact against
            // the reference libwebp decoder (see that file). No alpha channel support
            // (VP8L lossless / ALPH chunk), matching Meshy's real-world exports.
            "EXT_texture_webp",
        };

        public sealed class BuildResult
        {
            public GameObject Root;
            public readonly List<UnityEngine.Object> SubAssets = new List<UnityEngine.Object>();
            public readonly Dictionary<int, GameObject> Nodes = new Dictionary<int, GameObject>();
            public int MeshCount;
            public int MaterialCount;
            public int TextureCount;
            public bool Skinned;
        }

        private sealed class Ctx
        {
            public Dictionary<string, object> Root;
            public byte[] Bin;
            public List<object> Buffers, BufferViews, Accessors, Meshes, Materials, Textures, Images, Samplers, Skins, Nodes;
            public byte[][] BufferBytes;
            public readonly Dictionary<long, Texture2D> TextureCache = new Dictionary<long, Texture2D>();
            public readonly Dictionary<int, Material> MaterialCache = new Dictionary<int, Material>();
            public readonly Dictionary<int, GameObject> NodeObjects = new Dictionary<int, GameObject>();
            public readonly Dictionary<int, byte[]> BufferViewCache = new Dictionary<int, byte[]>();
            public Shader LitShader;
            public bool UsingUrp;
        }

        /// <summary>
        /// Returns the logical bytes a bufferView holds, decoding EXT_meshopt_compression
        /// transparently when present. Callers (ReadAccessorFloats/ReadAccessorInts) can
        /// then treat every bufferView the same way regardless of whether it was
        /// compressed -- the returned array always starts at that bufferView's own byte 0.
        /// </summary>
        private static byte[] GetBufferViewBytes(Ctx ctx, int bvIndex)
        {
            if (ctx.BufferViewCache.TryGetValue(bvIndex, out var cached)) return cached;

            var bv = MeshyMiniJson.AsObject(ctx.BufferViews[bvIndex]);
            var extensions = MeshyMiniJson.Get(bv, "extensions");
            var meshopt = extensions != null ? MeshyMiniJson.Get(extensions, "EXT_meshopt_compression") : null;

            byte[] result;
            if (meshopt != null)
            {
                int srcBuffer = MeshyMiniJson.GetInt(meshopt, "buffer", 0);
                int srcOffset = MeshyMiniJson.GetInt(meshopt, "byteOffset", 0);
                int srcLength = MeshyMiniJson.GetInt(meshopt, "byteLength", 0);
                int stride = MeshyMiniJson.GetInt(meshopt, "byteStride", 0);
                int count = MeshyMiniJson.GetInt(meshopt, "count", 0);
                string mode = MeshyMiniJson.GetString(meshopt, "mode", "ATTRIBUTES");
                string filter = MeshyMiniJson.GetString(meshopt, "filter", "NONE");

                byte[] compressed = new byte[srcLength];
                Buffer.BlockCopy(ctx.BufferBytes[srcBuffer], srcOffset, compressed, 0, srcLength);

                if (mode == "ATTRIBUTES")
                {
                    result = MeshyMeshopt.DecodeVertexBuffer(compressed, count, stride);
                    switch (filter)
                    {
                        case "OCTAHEDRAL": MeshyMeshopt.DecodeFilterOct(result, count, stride); break;
                        case "QUATERNION": MeshyMeshopt.DecodeFilterQuat(result, count, stride); break;
                        case "EXPONENTIAL": MeshyMeshopt.DecodeFilterExp(result, count, stride); break;
                    }
                }
                else
                {
                    uint[] indices = mode == "TRIANGLES"
                        ? MeshyMeshopt.DecodeIndexBuffer(compressed, count)
                        : MeshyMeshopt.DecodeIndexSequence(compressed, count);

                    result = new byte[count * stride];
                    for (int i = 0; i < count; i++)
                    {
                        if (stride == 2)
                        {
                            ushort v = (ushort)indices[i];
                            result[i * 2] = (byte)(v & 0xff);
                            result[i * 2 + 1] = (byte)((v >> 8) & 0xff);
                        }
                        else // stride == 4
                        {
                            uint v = indices[i];
                            result[i * 4] = (byte)(v & 0xff);
                            result[i * 4 + 1] = (byte)((v >> 8) & 0xff);
                            result[i * 4 + 2] = (byte)((v >> 16) & 0xff);
                            result[i * 4 + 3] = (byte)((v >> 24) & 0xff);
                        }
                    }
                }
            }
            else
            {
                int bufferIndex = MeshyMiniJson.GetInt(bv, "buffer", 0);
                int byteOffset = MeshyMiniJson.GetInt(bv, "byteOffset", 0);
                int byteLength = MeshyMiniJson.GetInt(bv, "byteLength", 0);
                result = new byte[byteLength];
                Buffer.BlockCopy(ctx.BufferBytes[bufferIndex], byteOffset, result, 0, byteLength);
            }

            ctx.BufferViewCache[bvIndex] = result;
            return result;
        }

        public static BuildResult Build(byte[] glb, string assetName, out string unsupportedReason)
        {
            unsupportedReason = null;
            var ctx = new Ctx();

            string json = ReadGlbChunks(glb, out ctx.Bin);
            ctx.Root = MeshyMiniJson.AsObject(MeshyMiniJson.Parse(json));
            if (ctx.Root == null) { unsupportedReason = "The GLB JSON chunk did not parse to an object."; return null; }

            var required = MeshyMiniJson.GetArray(ctx.Root, "extensionsRequired");
            if (required != null)
            {
                foreach (var e in required)
                {
                    string ext = e as string;
                    if (ext != null && !SupportedExtensions.Contains(ext))
                    {
                        unsupportedReason = $"requires unsupported glTF extension '{ext}'";
                        return null;
                    }
                }
            }

            ctx.Buffers = MeshyMiniJson.GetArray(ctx.Root, "buffers") ?? new List<object>();
            ctx.BufferViews = MeshyMiniJson.GetArray(ctx.Root, "bufferViews") ?? new List<object>();
            ctx.Accessors = MeshyMiniJson.GetArray(ctx.Root, "accessors") ?? new List<object>();
            ctx.Meshes = MeshyMiniJson.GetArray(ctx.Root, "meshes") ?? new List<object>();
            ctx.Materials = MeshyMiniJson.GetArray(ctx.Root, "materials") ?? new List<object>();
            ctx.Textures = MeshyMiniJson.GetArray(ctx.Root, "textures") ?? new List<object>();
            ctx.Images = MeshyMiniJson.GetArray(ctx.Root, "images") ?? new List<object>();
            ctx.Samplers = MeshyMiniJson.GetArray(ctx.Root, "samplers") ?? new List<object>();
            ctx.Skins = MeshyMiniJson.GetArray(ctx.Root, "skins") ?? new List<object>();
            ctx.Nodes = MeshyMiniJson.GetArray(ctx.Root, "nodes") ?? new List<object>();

            if (!ResolveBuffers(ctx, out unsupportedReason)) return null;

            ctx.UsingUrp = GraphicsSettings.currentRenderPipeline != null;
            ctx.LitShader = ctx.UsingUrp
                ? Shader.Find("Universal Render Pipeline/Lit")
                : Shader.Find("Standard");
            if (ctx.LitShader == null) ctx.LitShader = Shader.Find("Standard") ?? Shader.Find("Diffuse");

            var result = new BuildResult();

            // Pass A: create a bare GameObject per node with local transform, and parent them.
            for (int i = 0; i < ctx.Nodes.Count; i++)
            {
                var node = MeshyMiniJson.AsObject(ctx.Nodes[i]);
                string name = MeshyMiniJson.GetString(node, "name", $"node{i}");
                var go = new GameObject(name);
                ApplyNodeTransform(node, go.transform);
                ctx.NodeObjects[i] = go;
            }
            for (int i = 0; i < ctx.Nodes.Count; i++)
            {
                var node = MeshyMiniJson.AsObject(ctx.Nodes[i]);
                var children = MeshyMiniJson.GetArray(node, "children");
                if (children == null) continue;
                foreach (var c in children)
                {
                    int ci = MeshyMiniJson.AsInt(c, -1);
                    if (ci >= 0 && ctx.NodeObjects.TryGetValue(ci, out var childGo))
                        childGo.transform.SetParent(ctx.NodeObjects[i].transform, false);
                }
            }

            // Determine roots (nodes referenced by the default scene; fall back to nodes with no parent).
            var rootNodeIndices = new List<int>();
            var scenes = MeshyMiniJson.GetArray(ctx.Root, "scenes");
            int sceneIndex = MeshyMiniJson.GetInt(ctx.Root, "scene", 0);
            if (scenes != null && sceneIndex >= 0 && sceneIndex < scenes.Count)
            {
                var scene = MeshyMiniJson.AsObject(scenes[sceneIndex]);
                var sceneNodes = MeshyMiniJson.GetArray(scene, "nodes");
                if (sceneNodes != null)
                    rootNodeIndices.AddRange(sceneNodes.Select(n => MeshyMiniJson.AsInt(n, -1)).Where(n => n >= 0));
            }
            if (rootNodeIndices.Count == 0)
            {
                for (int i = 0; i < ctx.Nodes.Count; i++)
                    if (ctx.NodeObjects[i].transform.parent == null) rootNodeIndices.Add(i);
            }

            var sceneRoot = new GameObject(string.IsNullOrEmpty(assetName) ? "Meshy Model" : assetName);
            foreach (int ri in rootNodeIndices)
                if (ctx.NodeObjects.TryGetValue(ri, out var rgo)) rgo.transform.SetParent(sceneRoot.transform, false);
            result.Root = sceneRoot;

            // Pass B: attach mesh (and skin) renderers now that every node's Transform exists.
            for (int i = 0; i < ctx.Nodes.Count; i++)
            {
                var node = MeshyMiniJson.AsObject(ctx.Nodes[i]);
                if (!MeshyMiniJson.Has(node, "mesh")) continue;
                int meshIndex = MeshyMiniJson.GetInt(node, "mesh", -1);
                if (meshIndex < 0 || meshIndex >= ctx.Meshes.Count) continue;
                int skinIndex = MeshyMiniJson.Has(node, "skin") ? MeshyMiniJson.GetInt(node, "skin", -1) : -1;
                BuildMeshOnNode(ctx, meshIndex, skinIndex, ctx.NodeObjects[i], result);
            }

            foreach (var t in ctx.TextureCache.Values) result.SubAssets.Add(t);
            foreach (var m in ctx.MaterialCache.Values) result.SubAssets.Add(m);
            result.TextureCount = ctx.TextureCache.Values.Select(t => t).Distinct().Count();
            result.MaterialCount = ctx.MaterialCache.Count;
            foreach (var kv in ctx.NodeObjects) result.Nodes[kv.Key] = kv.Value;

            return result;
        }

        // ---- GLB container -----------------------------------------------

        private static string ReadGlbChunks(byte[] glb, out byte[] bin)
        {
            bin = null;
            if (glb.Length < 12 || glb[0] != 'g' || glb[1] != 'l' || glb[2] != 'T' || glb[3] != 'F')
                throw new InvalidOperationException("Not a GLB (missing glTF magic).");
            int offset = 12;
            string json = null;
            while (offset + 8 <= glb.Length)
            {
                uint chunkLen = BitConverter.ToUInt32(glb, offset);
                string chunkType = System.Text.Encoding.ASCII.GetString(glb, offset + 4, 4);
                int dataStart = offset + 8;
                if (dataStart + chunkLen > glb.Length) break;
                if (chunkType == "JSON")
                    json = System.Text.Encoding.UTF8.GetString(glb, dataStart, (int)chunkLen);
                else if (chunkType == "BIN\0")
                {
                    bin = new byte[chunkLen];
                    Buffer.BlockCopy(glb, dataStart, bin, 0, (int)chunkLen);
                }
                offset = dataStart + (int)chunkLen;
            }
            if (json == null) throw new InvalidOperationException("GLB has no JSON chunk.");
            return json;
        }

        private static bool ResolveBuffers(Ctx ctx, out string unsupportedReason)
        {
            unsupportedReason = null;
            ctx.BufferBytes = new byte[ctx.Buffers.Count][];
            for (int i = 0; i < ctx.Buffers.Count; i++)
            {
                var buf = MeshyMiniJson.AsObject(ctx.Buffers[i]);
                string uri = MeshyMiniJson.GetString(buf, "uri");
                if (uri == null)
                {
                    if (i == 0 && ctx.Bin != null)
                    {
                        ctx.BufferBytes[i] = ctx.Bin;
                        continue;
                    }

                    // EXT_meshopt_compression allows a URI-less "fallback" buffer (index >= 1)
                    // that a non-supporting loader would read directly; a loader that supports
                    // the extension (we do) sources compressed bufferViews from their own
                    // extension.buffer instead and never touches this placeholder's bytes.
                    var bufExt = MeshyMiniJson.Get(buf, "extensions");
                    var meshoptFallback = bufExt != null ? MeshyMiniJson.Get(bufExt, "EXT_meshopt_compression") : null;
                    if (meshoptFallback != null && MeshyMiniJson.GetBool(meshoptFallback, "fallback", false))
                    {
                        ctx.BufferBytes[i] = Array.Empty<byte>();
                        continue;
                    }

                    unsupportedReason = "a buffer has no URI and no GLB binary chunk is present";
                    return false;
                }
                else if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = uri.IndexOf(',');
                    ctx.BufferBytes[i] = Convert.FromBase64String(uri.Substring(comma + 1));
                }
                else
                {
                    unsupportedReason = "references an external buffer file, which a single .meshy payload should not do";
                    return false;
                }
            }
            return true;
        }

        // ---- Accessors / bufferViews --------------------------------------

        private static readonly Dictionary<string, int> TypeComponentCount = new Dictionary<string, int>
        {
            { "SCALAR", 1 }, { "VEC2", 2 }, { "VEC3", 3 }, { "VEC4", 4 }, { "MAT4", 16 },
        };

        private static float[][] ReadAccessorFloats(Ctx ctx, int accessorIndex)
        {
            var acc = MeshyMiniJson.AsObject(ctx.Accessors[accessorIndex]);
            int count = MeshyMiniJson.GetInt(acc, "count", 0);
            int componentType = MeshyMiniJson.GetInt(acc, "componentType", 5126);
            string type = MeshyMiniJson.GetString(acc, "type", "SCALAR");
            bool normalized = MeshyMiniJson.GetBool(acc, "normalized", false);
            int numComp = TypeComponentCount.TryGetValue(type, out var nc) ? nc : 1;
            int accByteOffset = MeshyMiniJson.GetInt(acc, "byteOffset", 0);

            var result = new float[count][];
            if (!MeshyMiniJson.Has(acc, "bufferView"))
            {
                // No bufferView: accessor is implicitly zero-filled (rare; used with sparse accessors).
                for (int i = 0; i < count; i++) result[i] = new float[numComp];
                return result;
            }

            int bvIndex = MeshyMiniJson.GetInt(acc, "bufferView", -1);
            var bv = MeshyMiniJson.AsObject(ctx.BufferViews[bvIndex]);
            int compSize = ComponentByteSize(componentType);
            int tightStride = compSize * numComp;
            int stride = MeshyMiniJson.Has(bv, "byteStride") ? MeshyMiniJson.GetInt(bv, "byteStride", tightStride) : tightStride;
            byte[] data = GetBufferViewBytes(ctx, bvIndex);
            int baseOffset = accByteOffset;

            for (int i = 0; i < count; i++)
            {
                int elemOffset = baseOffset + i * stride;
                var comps = new float[numComp];
                for (int c = 0; c < numComp; c++)
                {
                    comps[c] = ReadComponentNormalized(data, elemOffset + c * compSize, componentType, normalized);
                }
                result[i] = comps;
            }
            return result;
        }

        private static int[] ReadAccessorInts(Ctx ctx, int accessorIndex)
        {
            var acc = MeshyMiniJson.AsObject(ctx.Accessors[accessorIndex]);
            int count = MeshyMiniJson.GetInt(acc, "count", 0);
            int componentType = MeshyMiniJson.GetInt(acc, "componentType", 5121);
            string type = MeshyMiniJson.GetString(acc, "type", "SCALAR");
            int numComp = TypeComponentCount.TryGetValue(type, out var nc) ? nc : 1;
            int accByteOffset = MeshyMiniJson.GetInt(acc, "byteOffset", 0);

            var result = new int[count * numComp];
            if (!MeshyMiniJson.Has(acc, "bufferView")) return result;

            int bvIndex = MeshyMiniJson.GetInt(acc, "bufferView", -1);
            var bv = MeshyMiniJson.AsObject(ctx.BufferViews[bvIndex]);
            int compSize = ComponentByteSize(componentType);
            int tightStride = compSize * numComp;
            int stride = MeshyMiniJson.Has(bv, "byteStride") ? MeshyMiniJson.GetInt(bv, "byteStride", tightStride) : tightStride;
            byte[] data = GetBufferViewBytes(ctx, bvIndex);
            int baseOffset = accByteOffset;

            int k = 0;
            for (int i = 0; i < count; i++)
            {
                int elemOffset = baseOffset + i * stride;
                for (int c = 0; c < numComp; c++)
                    result[k++] = ReadComponentInt(data, elemOffset + c * compSize, componentType);
            }
            return result;
        }

        private static int ComponentByteSize(int componentType)
        {
            switch (componentType)
            {
                case 5120: return 1; // BYTE
                case 5121: return 1; // UNSIGNED_BYTE
                case 5122: return 2; // SHORT
                case 5123: return 2; // UNSIGNED_SHORT
                case 5125: return 4; // UNSIGNED_INT
                case 5126: return 4; // FLOAT
                default: throw new InvalidOperationException("Unknown glTF componentType " + componentType);
            }
        }

        private static float ReadComponentNormalized(byte[] data, int offset, int componentType, bool normalized)
        {
            switch (componentType)
            {
                case 5120: { sbyte v = (sbyte)data[offset]; return normalized ? Mathf.Max(v / 127f, -1f) : v; }
                case 5121: { byte v = data[offset]; return normalized ? v / 255f : v; }
                case 5122: { short v = BitConverter.ToInt16(data, offset); return normalized ? Mathf.Max(v / 32767f, -1f) : v; }
                case 5123: { ushort v = BitConverter.ToUInt16(data, offset); return normalized ? v / 65535f : v; }
                case 5125: { uint v = BitConverter.ToUInt32(data, offset); return v; }
                case 5126: return BitConverter.ToSingle(data, offset);
                default: throw new InvalidOperationException("Unknown glTF componentType " + componentType);
            }
        }

        private static int ReadComponentInt(byte[] data, int offset, int componentType)
        {
            switch (componentType)
            {
                case 5120: return (sbyte)data[offset];
                case 5121: return data[offset];
                case 5122: return BitConverter.ToInt16(data, offset);
                case 5123: return BitConverter.ToUInt16(data, offset);
                case 5125: return unchecked((int)BitConverter.ToUInt32(data, offset));
                default: throw new InvalidOperationException("Unexpected componentType for index/joint data: " + componentType);
            }
        }

        // ---- Node transforms (glTF right-handed -> Unity left-handed, Z negated) ----

        private static void ApplyNodeTransform(Dictionary<string, object> node, Transform t)
        {
            if (MeshyMiniJson.Has(node, "matrix"))
            {
                var m = MeshyMiniJson.GetArray(node, "matrix");
                var col = new Vector4[4];
                for (int c = 0; c < 4; c++)
                    col[c] = new Vector4(
                        (float)MeshyMiniJson.AsNumber(m[c * 4 + 0]),
                        (float)MeshyMiniJson.AsNumber(m[c * 4 + 1]),
                        (float)MeshyMiniJson.AsNumber(m[c * 4 + 2]),
                        (float)MeshyMiniJson.AsNumber(m[c * 4 + 3]));
                Vector3 translation = new Vector3(col[3].x, col[3].y, col[3].z);
                Vector3 basisX = new Vector3(col[0].x, col[0].y, col[0].z);
                Vector3 basisY = new Vector3(col[1].x, col[1].y, col[1].z);
                Vector3 basisZ = new Vector3(col[2].x, col[2].y, col[2].z);
                Vector3 scale = new Vector3(basisX.magnitude, basisY.magnitude, basisZ.magnitude);
                Vector3 fwd = scale.z > 1e-8f ? basisZ / scale.z : Vector3.forward;
                Vector3 up = scale.y > 1e-8f ? basisY / scale.y : Vector3.up;
                Quaternion rot = Quaternion.LookRotation(fwd, up);
                SetLocalGltf(t, translation, rot, scale);
                return;
            }

            var tr = MeshyMiniJson.GetArray(node, "translation");
            Vector3 pos = tr != null
                ? new Vector3((float)MeshyMiniJson.AsNumber(tr[0]), (float)MeshyMiniJson.AsNumber(tr[1]), (float)MeshyMiniJson.AsNumber(tr[2]))
                : Vector3.zero;

            var rt = MeshyMiniJson.GetArray(node, "rotation");
            Quaternion rotation = rt != null
                ? new Quaternion((float)MeshyMiniJson.AsNumber(rt[0]), (float)MeshyMiniJson.AsNumber(rt[1]), (float)MeshyMiniJson.AsNumber(rt[2]), (float)MeshyMiniJson.AsNumber(rt[3]))
                : Quaternion.identity;

            var sc = MeshyMiniJson.GetArray(node, "scale");
            Vector3 scaleVec = sc != null
                ? new Vector3((float)MeshyMiniJson.AsNumber(sc[0]), (float)MeshyMiniJson.AsNumber(sc[1]), (float)MeshyMiniJson.AsNumber(sc[2]))
                : Vector3.one;

            SetLocalGltf(t, pos, rotation, scaleVec);
        }

        private static void SetLocalGltf(Transform t, Vector3 gltfPos, Quaternion gltfRot, Vector3 gltfScale)
        {
            t.localPosition = new Vector3(gltfPos.x, gltfPos.y, -gltfPos.z);
            t.localRotation = new Quaternion(-gltfRot.x, -gltfRot.y, gltfRot.z, gltfRot.w);
            t.localScale = gltfScale;
        }

        public static Vector3 ConvertPoint(Vector3 p) => new Vector3(p.x, p.y, -p.z);
        public static Vector3 ConvertVector(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        // ---- Meshes ---------------------------------------------------------

        private static void BuildMeshOnNode(Ctx ctx, int meshIndex, int skinIndex, GameObject node, BuildResult result)
        {
            var meshDef = MeshyMiniJson.AsObject(ctx.Meshes[meshIndex]);
            var primitives = MeshyMiniJson.GetArray(meshDef, "primitives");
            if (primitives == null) return;

            Transform[] boneTransforms = null;
            Matrix4x4[] bindPoses = null;
            if (skinIndex >= 0 && skinIndex < ctx.Skins.Count)
            {
                var skin = MeshyMiniJson.AsObject(ctx.Skins[skinIndex]);
                var joints = MeshyMiniJson.GetArray(skin, "joints");
                boneTransforms = new Transform[joints.Count];
                for (int j = 0; j < joints.Count; j++)
                {
                    int jn = MeshyMiniJson.AsInt(joints[j], -1);
                    boneTransforms[j] = ctx.NodeObjects.TryGetValue(jn, out var jgo) ? jgo.transform : node.transform;
                }
                // inverseBindMatrices is optional per spec (defaults to identity) -- always
                // populate bindPoses so a skin without it still imports as skinned, not static.
                bindPoses = new Matrix4x4[joints.Count];
                for (int j = 0; j < bindPoses.Length; j++) bindPoses[j] = Matrix4x4.identity;
                if (MeshyMiniJson.Has(skin, "inverseBindMatrices"))
                {
                    int ibmAcc = MeshyMiniJson.GetInt(skin, "inverseBindMatrices", -1);
                    var raw = ReadAccessorFloats(ctx, ibmAcc);
                    for (int j = 0; j < raw.Length && j < bindPoses.Length; j++)
                        bindPoses[j] = ConvertMatrix(raw[j]);
                }
            }

            for (int p = 0; p < primitives.Count; p++)
            {
                var prim = MeshyMiniJson.AsObject(primitives[p]);
                int mode = MeshyMiniJson.GetInt(prim, "mode", 4);
                if (mode != 4) continue; // only TRIANGLES supported

                var attrs = MeshyMiniJson.Get(prim, "attributes");
                if (attrs == null || !MeshyMiniJson.Has(attrs, "POSITION")) continue;

                var mesh = new Mesh { name = $"{MeshyMiniJson.GetString(meshDef, "name", "mesh" + meshIndex)}_{p}" };

                var posRaw = ReadAccessorFloats(ctx, MeshyMiniJson.GetInt(attrs, "POSITION"));
                var vertices = new Vector3[posRaw.Length];
                for (int i = 0; i < posRaw.Length; i++)
                    vertices[i] = ConvertPoint(new Vector3(posRaw[i][0], posRaw[i][1], posRaw[i][2]));
                mesh.vertices = vertices;

                if (MeshyMiniJson.Has(attrs, "NORMAL"))
                {
                    var nRaw = ReadAccessorFloats(ctx, MeshyMiniJson.GetInt(attrs, "NORMAL"));
                    var normals = new Vector3[nRaw.Length];
                    for (int i = 0; i < nRaw.Length; i++)
                        normals[i] = ConvertVector(new Vector3(nRaw[i][0], nRaw[i][1], nRaw[i][2]));
                    mesh.normals = normals;
                }

                if (MeshyMiniJson.Has(attrs, "TANGENT"))
                {
                    var tRaw = ReadAccessorFloats(ctx, MeshyMiniJson.GetInt(attrs, "TANGENT"));
                    var tangents = new Vector4[tRaw.Length];
                    for (int i = 0; i < tRaw.Length; i++)
                        tangents[i] = new Vector4(tRaw[i][0], tRaw[i][1], -tRaw[i][2], -tRaw[i][3]);
                    mesh.tangents = tangents;
                }

                if (MeshyMiniJson.Has(attrs, "TEXCOORD_0"))
                    mesh.uv = ToUv(ReadAccessorFloats(ctx, MeshyMiniJson.GetInt(attrs, "TEXCOORD_0")));
                if (MeshyMiniJson.Has(attrs, "TEXCOORD_1"))
                    mesh.uv2 = ToUv(ReadAccessorFloats(ctx, MeshyMiniJson.GetInt(attrs, "TEXCOORD_1")));

                if (MeshyMiniJson.Has(attrs, "COLOR_0"))
                {
                    var cRaw = ReadAccessorFloats(ctx, MeshyMiniJson.GetInt(attrs, "COLOR_0"));
                    var colors = new Color[cRaw.Length];
                    for (int i = 0; i < cRaw.Length; i++)
                        colors[i] = cRaw[i].Length >= 4
                            ? new Color(cRaw[i][0], cRaw[i][1], cRaw[i][2], cRaw[i][3])
                            : new Color(cRaw[i][0], cRaw[i][1], cRaw[i][2], 1f);
                    mesh.colors = colors;
                }

                BoneWeight[] boneWeights = null;
                if (boneTransforms != null && MeshyMiniJson.Has(attrs, "JOINTS_0") && MeshyMiniJson.Has(attrs, "WEIGHTS_0"))
                {
                    var joints0 = ReadAccessorInts(ctx, MeshyMiniJson.GetInt(attrs, "JOINTS_0"));
                    var weights0 = ReadAccessorFloats(ctx, MeshyMiniJson.GetInt(attrs, "WEIGHTS_0"));
                    boneWeights = new BoneWeight[weights0.Length];
                    for (int i = 0; i < weights0.Length; i++)
                    {
                        var bw = new BoneWeight
                        {
                            boneIndex0 = joints0[i * 4 + 0], weight0 = weights0[i][0],
                            boneIndex1 = joints0[i * 4 + 1], weight1 = weights0[i].Length > 1 ? weights0[i][1] : 0,
                            boneIndex2 = joints0[i * 4 + 2], weight2 = weights0[i].Length > 2 ? weights0[i][2] : 0,
                            boneIndex3 = joints0[i * 4 + 3], weight3 = weights0[i].Length > 3 ? weights0[i][3] : 0,
                        };
                        boneWeights[i] = bw;
                    }
                }

                if (MeshyMiniJson.Has(prim, "indices"))
                {
                    var idx = ReadAccessorInts(ctx, MeshyMiniJson.GetInt(prim, "indices"));
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        int tmp = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = tmp; // reverse winding
                    }
                    mesh.triangles = idx;
                }
                else
                {
                    var idx = new int[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++) idx[i] = i;
                    for (int i = 0; i + 2 < idx.Length; i += 3) { int tmp = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = tmp; }
                    mesh.triangles = idx;
                }

                mesh.RecalculateBounds();
                if (!MeshyMiniJson.Has(attrs, "NORMAL")) mesh.RecalculateNormals();
                if (!MeshyMiniJson.Has(attrs, "TANGENT") && MeshyMiniJson.Has(attrs, "TEXCOORD_0")) mesh.RecalculateTangents();

                Material mat = MeshyMiniJson.Has(prim, "material")
                    ? GetOrBuildMaterial(ctx, MeshyMiniJson.GetInt(prim, "material", -1))
                    : GetOrBuildMaterial(ctx, -1);

                GameObject target = primitives.Count == 1 ? node : new GameObject(mesh.name);
                if (target != node) target.transform.SetParent(node.transform, false);

                if (boneWeights != null && bindPoses != null)
                {
                    mesh.boneWeights = boneWeights;
                    mesh.bindposes = bindPoses;
                    var smr = target.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = mesh;
                    smr.bones = boneTransforms;
                    smr.sharedMaterial = mat;
                    smr.rootBone = boneTransforms.Length > 0 ? boneTransforms[0] : null;
                    result.Skinned = true;
                }
                else
                {
                    var mf = target.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = target.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                }

                mesh.name = target.name;
                result.SubAssets.Add(mesh);
                result.MeshCount++;
            }
        }

        private static Matrix4x4 ConvertMatrix(float[] m16)
        {
            // glTF matrices are column-major float[16]. Convert basis to Unity's
            // left-handed space (negate Z row/column) to match ConvertPoint/ConvertVector.
            var flip = Matrix4x4.identity;
            flip.m22 = -1f;
            var src = new Matrix4x4(
                new Vector4(m16[0], m16[1], m16[2], m16[3]),
                new Vector4(m16[4], m16[5], m16[6], m16[7]),
                new Vector4(m16[8], m16[9], m16[10], m16[11]),
                new Vector4(m16[12], m16[13], m16[14], m16[15]));
            return flip * src * flip;
        }

        private static Vector2[] ToUv(float[][] raw)
        {
            var uv = new Vector2[raw.Length];
            for (int i = 0; i < raw.Length; i++) uv[i] = new Vector2(raw[i][0], 1f - raw[i][1]); // glTF UV origin is top-left
            return uv;
        }

        // ---- Materials & textures --------------------------------------------

        private static Material GetOrBuildMaterial(Ctx ctx, int materialIndex)
        {
            if (ctx.MaterialCache.TryGetValue(materialIndex, out var cached)) return cached;

            var mat = new Material(ctx.LitShader) { name = materialIndex < 0 ? "default" : "material" + materialIndex };
            if (materialIndex < 0)
            {
                ctx.MaterialCache[materialIndex] = mat;
                return mat;
            }

            var m = MeshyMiniJson.AsObject(ctx.Materials[materialIndex]);
            mat.name = MeshyMiniJson.GetString(m, "name", mat.name);

            var pbr = MeshyMiniJson.Get(m, "pbrMetallicRoughness");
            Color baseColor = Color.white;
            var bcf = pbr != null ? MeshyMiniJson.GetArray(pbr, "baseColorFactor") : null;
            if (bcf != null)
                baseColor = new Color((float)MeshyMiniJson.AsNumber(bcf[0]), (float)MeshyMiniJson.AsNumber(bcf[1]), (float)MeshyMiniJson.AsNumber(bcf[2]), bcf.Count > 3 ? (float)MeshyMiniJson.AsNumber(bcf[3]) : 1f);
            SetColor(mat, ctx, "_BaseColor", "_Color", baseColor);

            float metallic = pbr != null ? (float)MeshyMiniJson.GetNumber(pbr, "metallicFactor", 1) : 1f;
            float roughness = pbr != null ? (float)MeshyMiniJson.GetNumber(pbr, "roughnessFactor", 1) : 1f;
            SetFloat(mat, ctx, "_Metallic", "_Metallic", metallic);
            SetFloat(mat, ctx, "_Smoothness", "_Glossiness", 1f - roughness);

            var baseColorTex = pbr != null ? MeshyMiniJson.Get(pbr, "baseColorTexture") : null;
            Texture2D baseColorTexture = null;
            if (baseColorTex != null)
            {
                baseColorTexture = GetTexture(ctx, MeshyMiniJson.GetInt(baseColorTex, "index", -1), linear: false);
                if (baseColorTexture != null) SetTexture(mat, ctx, "_BaseMap", "_MainTex", baseColorTexture);
            }

            var mrTex = pbr != null ? MeshyMiniJson.Get(pbr, "metallicRoughnessTexture") : null;
            if (mrTex != null)
            {
                var packed = GetMetallicSmoothnessTexture(ctx, MeshyMiniJson.GetInt(mrTex, "index", -1));
                if (packed != null) SetTexture(mat, ctx, "_MetallicGlossMap", "_MetallicGlossMap", packed);
                if (ctx.UsingUrp) mat.EnableKeyword("_METALLICSPECGLOSSMAP"); else mat.EnableKeyword("_METALLICGLOSSMAP");
            }

            var normalTex = MeshyMiniJson.Get(m, "normalTexture");
            if (normalTex != null)
            {
                var tex = GetTexture(ctx, MeshyMiniJson.GetInt(normalTex, "index", -1), linear: true);
                if (tex != null)
                {
                    SetTexture(mat, ctx, "_BumpMap", "_BumpMap", tex);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            var occTex = MeshyMiniJson.Get(m, "occlusionTexture");
            if (occTex != null)
            {
                var tex = GetTexture(ctx, MeshyMiniJson.GetInt(occTex, "index", -1), linear: true);
                if (tex != null) SetTexture(mat, ctx, "_OcclusionMap", "_OcclusionMap", tex);
            }

            var emissiveFactorArr = MeshyMiniJson.GetArray(m, "emissiveFactor");
            Color emissive = emissiveFactorArr != null
                ? new Color((float)MeshyMiniJson.AsNumber(emissiveFactorArr[0]), (float)MeshyMiniJson.AsNumber(emissiveFactorArr[1]), (float)MeshyMiniJson.AsNumber(emissiveFactorArr[2]))
                : Color.black;
            var emissiveStrengthExt = MeshyMiniJson.Get(MeshyMiniJson.Get(m, "extensions"), "KHR_materials_emissive_strength");
            if (emissiveStrengthExt != null)
            {
                float strength = (float)MeshyMiniJson.GetNumber(emissiveStrengthExt, "emissiveStrength", 1);
                emissive *= strength;
            }
            if (emissive.maxColorComponent > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                SetColor(mat, ctx, "_EmissionColor", "_EmissionColor", emissive);
                var emTex = MeshyMiniJson.Get(m, "emissiveTexture");
                if (emTex != null)
                {
                    var tex = GetTexture(ctx, MeshyMiniJson.GetInt(emTex, "index", -1), linear: false);
                    if (tex != null) SetTexture(mat, ctx, "_EmissionMap", "_EmissionMap", tex);
                }
            }
            else if (baseColorTexture != null)
            {
                // Meshy's exports rarely define a real glTF emissive channel, and the
                // destination scene's lighting is completely out of this importer's
                // control -- a physically-correct metallic/roughness material can render
                // solid black under a scene with little ambient light and no reflection
                // probes, even though the imported texture data itself is fine. To keep
                // "drop the file in, see the model" working regardless of the receiving
                // scene's lighting rig, feed the base color texture back in as emission
                // too, at unit strength. This puts a floor under the material's
                // brightness at exactly the base color texture -- direct/specular
                // lighting from a properly lit scene still adds highlights on top, it's
                // just no longer possible for the model to disappear into a dark scene.
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                SetColor(mat, ctx, "_EmissionColor", "_EmissionColor", new Color(baseColor.r, baseColor.g, baseColor.b, 1f));
                SetTexture(mat, ctx, "_EmissionMap", "_EmissionMap", baseColorTexture);
            }

            string alphaMode = MeshyMiniJson.GetString(m, "alphaMode", "OPAQUE");
            bool doubleSided = MeshyMiniJson.GetBool(m, "doubleSided", false);
            ApplySurfaceMode(mat, ctx, alphaMode, (float)MeshyMiniJson.GetNumber(m, "alphaCutoff", 0.5), doubleSided);

            ctx.MaterialCache[materialIndex] = mat;
            return mat;
        }

        private static void ApplySurfaceMode(Material mat, Ctx ctx, string alphaMode, float cutoff, bool doubleSided)
        {
            if (doubleSided) mat.SetInt("_Cull", (int)CullMode.Off);

            if (alphaMode == "MASK")
            {
                mat.SetFloat("_Cutoff", cutoff);
                if (ctx.UsingUrp)
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }
                else
                {
                    mat.SetFloat("_Mode", 1);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.renderQueue = (int)RenderQueue.AlphaTest;
                }
            }
            else if (alphaMode == "BLEND")
            {
                if (ctx.UsingUrp)
                {
                    mat.SetFloat("_Surface", 1f);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }
                else
                {
                    mat.SetFloat("_Mode", 3);
                    mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }
            }
        }

        private static void SetColor(Material mat, Ctx ctx, string urpProp, string legacyProp, Color c)
        {
            string prop = ctx.UsingUrp ? urpProp : legacyProp;
            if (mat.HasProperty(prop)) mat.SetColor(prop, c);
        }

        private static void SetFloat(Material mat, Ctx ctx, string urpProp, string legacyProp, float v)
        {
            string prop = ctx.UsingUrp ? urpProp : legacyProp;
            if (mat.HasProperty(prop)) mat.SetFloat(prop, v);
        }

        private static void SetTexture(Material mat, Ctx ctx, string urpProp, string legacyProp, Texture2D tex)
        {
            string prop = ctx.UsingUrp ? urpProp : legacyProp;
            if (mat.HasProperty(prop)) mat.SetTexture(prop, tex);
        }

        private static byte[] GetImageBytes(Ctx ctx, int imageIndex)
        {
            var image = MeshyMiniJson.AsObject(ctx.Images[imageIndex]);
            if (MeshyMiniJson.Has(image, "bufferView"))
            {
                int bvIndex = MeshyMiniJson.GetInt(image, "bufferView", -1);
                var bv = MeshyMiniJson.AsObject(ctx.BufferViews[bvIndex]);
                int bufferIndex = MeshyMiniJson.GetInt(bv, "buffer", 0);
                int byteOffset = MeshyMiniJson.GetInt(bv, "byteOffset", 0);
                int byteLength = MeshyMiniJson.GetInt(bv, "byteLength", 0);
                var bytes = new byte[byteLength];
                Buffer.BlockCopy(ctx.BufferBytes[bufferIndex], byteOffset, bytes, 0, byteLength);
                return bytes;
            }
            string uri = MeshyMiniJson.GetString(image, "uri");
            if (uri != null && uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = uri.IndexOf(',');
                return Convert.FromBase64String(uri.Substring(comma + 1));
            }
            return null; // external file reference: not supported, texture slot left empty
        }

        private static bool LooksLikeWebp(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 12 &&
                   bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
                   bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
        }

        private static Texture2D GetTexture(Ctx ctx, int textureIndex, bool linear)
        {
            if (textureIndex < 0 || textureIndex >= ctx.Textures.Count) return null;
            var texDef = MeshyMiniJson.AsObject(ctx.Textures[textureIndex]);

            // EXT_texture_webp overrides the base "source" with a webp-encoded image;
            // Meshy exports typically omit the PNG/JPEG fallback "source" entirely.
            int imageIndex = -1;
            var texExtensions = MeshyMiniJson.Get(texDef, "extensions");
            var webpExt = texExtensions != null ? MeshyMiniJson.Get(texExtensions, "EXT_texture_webp") : null;
            if (webpExt != null && MeshyMiniJson.Has(webpExt, "source"))
                imageIndex = MeshyMiniJson.GetInt(webpExt, "source", -1);
            else if (MeshyMiniJson.Has(texDef, "source"))
                imageIndex = MeshyMiniJson.GetInt(texDef, "source", -1);
            else
                return null;

            long cacheKey = (long)imageIndex * 2 + (linear ? 1 : 0);
            if (ctx.TextureCache.TryGetValue(cacheKey, out var cached)) return cached;

            var bytes = GetImageBytes(ctx, imageIndex);
            if (bytes == null) return null;

            Texture2D tex;
            if (LooksLikeWebp(bytes))
            {
                if (!MeshyWebpVp8.TryDecodeRgb(bytes, out int webpW, out int webpH, out byte[] rgb)) return null;
                tex = new Texture2D(webpW, webpH, TextureFormat.RGB24, true, linear);
                // LoadRawTextureData requires data for the whole mip chain once mipmaps are
                // enabled; SetPixelData(data, 0) sets just mip 0, and Apply(true) below
                // generates the rest from it.
                tex.SetPixelData(rgb, 0);
                tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            }
            else
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                if (!tex.LoadImage(bytes, markNonReadable: false)) return null;
            }

            if (MeshyMiniJson.Has(texDef, "sampler"))
            {
                int samplerIndex = MeshyMiniJson.GetInt(texDef, "sampler", -1);
                if (samplerIndex >= 0 && samplerIndex < ctx.Samplers.Count)
                    ApplySampler(tex, MeshyMiniJson.AsObject(ctx.Samplers[samplerIndex]));
            }

            tex.name = "image" + imageIndex + (linear ? "_linear" : "_srgb");
            ctx.TextureCache[cacheKey] = tex;
            return tex;
        }

        // glTF packs metallic/roughness as B=metallic, G=roughness; Unity's
        // _MetallicGlossMap expects R=metallic, A=smoothness. Repack on the CPU.
        private static Texture2D GetMetallicSmoothnessTexture(Ctx ctx, int textureIndex)
        {
            var src = GetTexture(ctx, textureIndex, linear: true);
            if (src == null) return null;
            long cacheKey = (long)textureIndex * 2 + 100000; // distinct namespace from GetTexture's cache
            if (ctx.TextureCache.TryGetValue(cacheKey, out var cached)) return cached;

            Color[] pixels;
            try { pixels = src.GetPixels(); }
            catch (UnityException) { return src; } // non-readable for some reason; fall back to raw texture

            var outPixels = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                float metallic = pixels[i].b;
                float smoothness = 1f - pixels[i].g;
                outPixels[i] = new Color(metallic, metallic, metallic, smoothness);
            }
            var packed = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true, true) { name = src.name + "_metallicSmoothness" };
            packed.SetPixels(outPixels);
            packed.Apply();
            ctx.TextureCache[cacheKey] = packed;
            return packed;
        }

        private static void ApplySampler(Texture2D tex, Dictionary<string, object> sampler)
        {
            int wrapS = MeshyMiniJson.GetInt(sampler, "wrapS", 10497);
            int wrapT = MeshyMiniJson.GetInt(sampler, "wrapT", 10497);
            tex.wrapModeU = ToWrapMode(wrapS);
            tex.wrapModeV = ToWrapMode(wrapT);
            int magFilter = MeshyMiniJson.GetInt(sampler, "magFilter", 9729);
            tex.filterMode = magFilter == 9728 ? FilterMode.Point : FilterMode.Bilinear;
        }

        private static TextureWrapMode ToWrapMode(int glWrap)
        {
            switch (glWrap)
            {
                case 33071: return TextureWrapMode.Clamp;      // CLAMP_TO_EDGE
                case 33648: return TextureWrapMode.Mirror;     // MIRRORED_REPEAT
                default: return TextureWrapMode.Repeat;         // REPEAT (10497)
            }
        }
    }
}
