using UnityEngine;
namespace FISHHWB.MeshyImporter
{
    public sealed class MeshySourceAsset : ScriptableObject
    {
        [SerializeField] private string sourcePath;
        [SerializeField] private string generatedGlbPath;
        [SerializeField] private long sourceSize;
        [SerializeField] private string status;
        public string SourcePath => sourcePath;
        public string GeneratedGlbPath => generatedGlbPath;
        public long SourceSize => sourceSize;
        public string Status => status;
        public void SetMetadata(string source, string glb, long size, string importStatus)
        { sourcePath = source; generatedGlbPath = glb; sourceSize = size; status = importStatus; }
    }
}
