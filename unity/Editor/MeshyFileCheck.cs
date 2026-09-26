using System;
using System.Text;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>Plain-language "this is the wrong file" diagnosis shared by every Unity entry point.
    /// No Unity dependencies, so tests/csharp/wrong-file can run it outside the Editor.</summary>
    public static class MeshyFileCheck
    {
        internal const string TroubleshootingUrl = "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/blob/main/TROUBLESHOOTING.md";
        internal const string HelpWrongFile = TroubleshootingUrl + "#wrong-file-errors";
        internal const string HelpFormatChanged = TroubleshootingUrl + "#meshy-changed-its-web-format";

        /// <summary>
        /// One plain sentence explaining why <paramref name="data"/> is not a usable .meshy
        /// file, or null when it looks like a complete container. Port of
        /// core/python/meshy_core/decode.py describe_wrong_file; keep the wording in sync.
        /// </summary>
        public static string DescribeWrongFile(byte[] data)
        {
            const int minSize = 32 + 8192 + 16;
            if (data == null || data.Length == 0)
                return "The file is empty. The download probably failed; save the model response again.";
            if (StartsWith(data, 0, "MESHY.AI"))
                return data.Length < minSize
                    ? "The .meshy file is cut off (only " + data.Length + " bytes). Save the complete model response again."
                    : null;

            int start = 0;
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) start = 3;
            while (start < data.Length && start < 512 && (data[start] == ' ' || data[start] == '\t' || data[start] == '\r' || data[start] == '\n')) start++;
            string head = Encoding.ASCII.GetString(data, start, Math.Min(64, data.Length - start)).ToLowerInvariant();

            if (StartsWith(data, 0, "glTF"))
                return "This is a plain GLB, not a .meshy payload. Import it as a .glb instead; it does not need the Meshy Importer.";
            if (head.StartsWith("<!doctype") || head.StartsWith("<html") || head.StartsWith("<?xml") || head.StartsWith("<head") || head.StartsWith("<body"))
                return "This is a web page (HTML), not the model. In the Network tab, save the model request's response instead of the page.";
            if (head.StartsWith("{") || head.StartsWith("["))
                return "This is a JSON API response, not the model. Pick the request whose response starts with MESHY.AI.";
            if (StartsWith(data, 0, "Kaydara FBX Binary") || head.StartsWith("; fbx"))
                return "This is an FBX file (Meshy's normal Download). Import it directly as .fbx; it does not need the Meshy Importer.";
            if (data.Length >= 4 && data[0] == 'P' && data[1] == 'K' && data[2] == 3 && data[3] == 4)
                return "This is a ZIP archive. Unzip it; if it holds .glb/.fbx/.obj files, import those directly.";
            if ((data.Length >= 8 && data[0] == 0x89 && StartsWith(data, 1, "PNG")) ||
                (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) ||
                (StartsWith(data, 0, "RIFF") && StartsWith(data, 8, "WEBP")))
                return "This is an image, not a model. Save the model request's response instead.";
            if (head.StartsWith("# ") || head.StartsWith("mtllib") || head.StartsWith("o ") || head.StartsWith("v ") || head.StartsWith("g "))
                return "This is an OBJ file (Meshy's normal Download). Import it directly as .obj; it does not need the Meshy Importer.";
            return "This is not a .meshy file: it does not start with MESHY.AI. Make sure you saved the model response, not another request.";
        }

        private static bool StartsWith(byte[] data, int offset, string ascii)
        {
            if (data.Length < offset + ascii.Length) return false;
            for (int i = 0; i < ascii.Length; i++)
                if (data[offset + i] != (byte)ascii[i]) return false;
            return true;
        }
    }
}
