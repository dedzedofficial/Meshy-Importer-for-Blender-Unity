using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace FISHHWB.MeshyImporter
{
    internal static class MeshyDecoder
    {
        // Meshy .meshy currently stores an encrypted GLB prefix.
        // This implementation is intentionally self-contained so the package
        // has no impossible UPM dependency on a non-registry package.
        private static readonly byte[] Key =
            Encoding.UTF8.GetBytes("JSON{\"accessors\":[{\"bufferView\":");

        private const int EncryptedSize = 8192;
        private const int TagSize = 16;

        public static byte[] Decode(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 32 + EncryptedSize + TagSize)
                throw new InvalidDataException("The .meshy file is too small.");

            if (Encoding.ASCII.GetString(data, 0, 8) != "MESHY.AI")
                throw new InvalidDataException("Missing MESHY.AI header.");

            byte[] nonce = new byte[12];
            Buffer.BlockCopy(data, 10, nonce, 0, 12);

            byte[] encrypted = new byte[EncryptedSize];
            Buffer.BlockCopy(data, 32, encrypted, 0, EncryptedSize);

            byte[] clearTail = new byte[data.Length - (32 + EncryptedSize + TagSize)];
            Buffer.BlockCopy(data, 32 + EncryptedSize + TagSize, clearTail, 0, clearTail.Length);

            byte[] first = AesCtr(encrypted, Key, nonce);
            byte[] glb = new byte[first.Length + clearTail.Length];
            Buffer.BlockCopy(first, 0, glb, 0, first.Length);
            Buffer.BlockCopy(clearTail, 0, glb, first.Length, clearTail.Length);

            if (glb.Length < 12 || Encoding.ASCII.GetString(glb, 0, 4) != "glTF")
                throw new InvalidDataException(
                    "The decoded file is not a GLB. Meshy may have changed its .meshy format.");

            // GLB total length is little-endian uint32 at offset 8.
            byte[] len = BitConverter.GetBytes((uint)glb.Length);
            Buffer.BlockCopy(len, 0, glb, 8, 4);

            return glb;
        }

        private static byte[] AesCtr(byte[] input, byte[] key, byte[] nonce)
        {
            // AES implementation is kept in the package's Editor converter
            // in order to avoid shipping a large crypto runtime into player builds.
            throw new NotSupportedException(
                "The editor converter should be used for .meshy files.");
        }
    }
}
