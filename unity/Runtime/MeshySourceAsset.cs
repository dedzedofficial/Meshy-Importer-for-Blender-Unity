using UnityEngine;
namespace FISHHWB.MeshyImporter
{
    public sealed class MeshySourceAsset : ScriptableObject
    {
        [SerializeField] private string sourcePath;
        [SerializeField] private string generatedGlbPath;
        [SerializeField] private long sourceSize;
        [SerializeField] private string status;
        [SerializeField] private string assetType;
        [SerializeField] private int meshCount;
        [SerializeField] private int materialCount;
        [SerializeField] private int textureCount;
        [SerializeField] private int vertexCount;
        [SerializeField] private int triangleCount;
        [SerializeField] private int missingUvCount;
        [SerializeField] private bool skinned;
        [SerializeField] private int uvBadVertices;
        [SerializeField] private int uvRepairedVertices;
        [SerializeField] private int uvRegeneratedMeshes;
        [SerializeField] private string renderPipeline;

        [SerializeField] private string preflightReport;
        [SerializeField] private bool nativeSuccess;
        [SerializeField] private string successfulSettingsJson;
        [SerializeField] private string[] materialKeys = new string[0];
        [SerializeField] private string[] materialLabels = new string[0];
        [SerializeField] private Material[] generatedMaterials = new Material[0];
        [SerializeField] private Vector3 dimensions;
        [SerializeField] private int boneCount;
        [SerializeField] private long estimatedTextureBytes;
        [SerializeField] private int largestTextureWidth, largestTextureHeight;
        public string PreflightReport => preflightReport;
        public bool NativeSuccess => nativeSuccess;
        public string SuccessfulSettingsJson => successfulSettingsJson;
        public string[] MaterialKeys => materialKeys;
        public string[] MaterialLabels => materialLabels;
        public Material[] GeneratedMaterials => generatedMaterials;
        public Vector3 Dimensions => dimensions;
        public int BoneCount => boneCount;
        public long EstimatedTextureBytes => estimatedTextureBytes;
        public int LargestTextureWidth => largestTextureWidth;
        public int LargestTextureHeight => largestTextureHeight;
        public void SetPreflight(string report) { preflightReport = report; }
        public void SetMaterialCatalog(string[] keys, string[] labels) { materialKeys = keys; materialLabels = labels; }
        public void SetWorkflow(string[] keys, string[] labels, Material[] originals, string settings,
            Vector3 size, int bones, long textureBytes, int maxWidth, int maxHeight)
        {
            materialKeys = keys; materialLabels = labels; generatedMaterials = originals;
            successfulSettingsJson = settings; dimensions = size; boneCount = bones;
            estimatedTextureBytes = textureBytes; largestTextureWidth = maxWidth; largestTextureHeight = maxHeight;
            nativeSuccess = true;
        }

        public string SourcePath => sourcePath;
        /// <summary>The .glb written on the fallback path, or null/empty for a native import.</summary>
        public string GeneratedGlbPath => generatedGlbPath;
        public long SourceSize => sourceSize;
        public string Status => status;
        public string AssetType => assetType;
        public int MeshCount => meshCount;
        public int MaterialCount => materialCount;
        public int TextureCount => textureCount;
        public int VertexCount => vertexCount;
        public int TriangleCount => triangleCount;
        public int MissingUvCount => missingUvCount;
        public bool Skinned => skinned;
        public int UvBadVertices => uvBadVertices;
        public int UvRepairedVertices => uvRepairedVertices;
        public int UvRegeneratedMeshes => uvRegeneratedMeshes;
        public string RenderPipeline => renderPipeline;

        public void SetMetadata(string source, string glb, long size, string importStatus)
        { sourcePath = source; generatedGlbPath = glb; sourceSize = size; status = importStatus; }

        public void SetAnalysis(string type, int meshes, int materials, int textures, int vertices, int triangles, int missingUvs, bool isSkinned)
        {
            assetType = type; meshCount = meshes; materialCount = materials; textureCount = textures;
            vertexCount = vertices; triangleCount = triangles; missingUvCount = missingUvs; skinned = isSkinned;
        }

        public void SetUvRepair(int badVertices, int repairedVertices, int regeneratedMeshes)
        { uvBadVertices = badVertices; uvRepairedVertices = repairedVertices; uvRegeneratedMeshes = regeneratedMeshes; }

        public void SetRenderPipeline(string pipeline) { renderPipeline = pipeline; }
    }
}
