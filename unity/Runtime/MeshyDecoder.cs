using System;

namespace FISHHWB.MeshyImporter
{
    internal static class MeshyDecoder
    {
        // Meshy .meshy currently stores an encrypted GLB prefix.
        //
        // Decoding is intentionally NOT implemented here. The AES-256-CTR
        // implementation lives only in the Editor converter
        // (unity/Editor/MeshyImporterMenu.cs) so a full crypto routine is
        // never compiled into player builds. Convert .meshy files to .glb
        // in the Editor (Tools > Meshy > Convert .meshy to GLB...) and ship
        // the resulting .glb, not the raw .meshy container.

        public static byte[] Decode(string path)
        {
            throw new NotSupportedException(
                "Runtime .meshy decoding is not supported. Use Tools > Meshy > " +
                "Convert .meshy to GLB... (or Convert All .meshy In Assets) in the " +
                "Unity Editor, then import the resulting .glb file instead.");
        }
    }
}
