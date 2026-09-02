using System;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// Decoder for the meshoptimizer compressed vertex/index bitstreams used by
    /// EXT_meshopt_compression (https://github.com/zeux/meshoptimizer, MIT licensed).
    /// Ported by hand from the ratified Khronos EXT_meshopt_compression spec
    /// (Appendix A: Bitstream, Appendix B: Filters), cross-checked line-by-line
    /// against the reference implementation (src/vertexcodec.cpp, src/indexcodec.cpp,
    /// src/vertexfilter.cpp at github.com/zeux/meshoptimizer) to resolve every place
    /// the prose spec was ambiguous. Only format version 0 (vertex) / version 1
    /// (index) are implemented -- those are the only versions the ratified extension
    /// ever emits (headers 0xa0 and 0xe1/0xd1 respectively).
    /// Verified byte-for-byte against the reference decoder's output on real Meshy
    /// export data before being wired into the importer.
    /// </summary>
    internal static class MeshyMeshopt
    {
        private const int kByteGroupSize = 16;
        private const int kByteGroupDecodeLimit = 24;
        private const int kTailMinSizeV0 = 32;
        private static readonly int[] kBitsV0 = { 0, 2, 4, 8 };

        // ------------------------------------------------------------------
        // Mode 0: ATTRIBUTES (vertex buffer)
        // ------------------------------------------------------------------

        public static byte[] DecodeVertexBuffer(byte[] buffer, int vertexCount, int vertexSize)
        {
            if (vertexSize <= 0 || vertexSize > 256 || (vertexSize % 4) != 0)
                throw new InvalidOperationException("meshopt: invalid vertex size " + vertexSize);

            int pos = 0;
            int end = buffer.Length;
            if (end - pos < 1) throw new InvalidOperationException("meshopt: empty vertex buffer");

            byte header = buffer[pos++];
            if ((header & 0xf0) != 0xa0) throw new InvalidOperationException("meshopt: bad vertex header 0x" + header.ToString("x2"));
            int version = header & 0x0f;
            if (version != 0) throw new InvalidOperationException("meshopt: unsupported vertex codec version " + version);

            int tailSize = vertexSize; // version 0: no per-channel control bytes
            int tailSizePad = Math.Max(tailSize, kTailMinSizeV0);
            if (end - pos < tailSizePad) throw new InvalidOperationException("meshopt: vertex buffer truncated (tail)");

            byte[] lastVertex = new byte[vertexSize];
            Array.Copy(buffer, end - tailSize, lastVertex, 0, vertexSize);

            byte[] result = new byte[vertexCount * vertexSize];

            int vertexBlockSize = GetVertexBlockSize(vertexSize);
            int vertexOffset = 0;

            while (vertexOffset < vertexCount)
            {
                int blockSize = (vertexOffset + vertexBlockSize < vertexCount) ? vertexBlockSize : vertexCount - vertexOffset;
                pos = DecodeVertexBlock(buffer, pos, end, result, vertexOffset * vertexSize, blockSize, vertexSize, lastVertex);
                vertexOffset += blockSize;
            }

            if (end - pos != tailSizePad)
                throw new InvalidOperationException("meshopt: vertex stream did not end exactly at the tail boundary");

            return result;
        }

        private static int GetVertexBlockSize(int vertexSize)
        {
            int result = (8192 / vertexSize) & ~(kByteGroupSize - 1);
            return Math.Min(result, 256);
        }

        private static int DecodeVertexBlock(byte[] data, int pos, int end, byte[] output, int outOffset, int vertexCount, int vertexSize, byte[] lastVertex)
        {
            int vertexCountAligned = (vertexCount + kByteGroupSize - 1) & ~(kByteGroupSize - 1);

            // buffer[byteIndex][vertexIndex] planar storage, one plane per byte position in the vertex.
            byte[][] planes = new byte[vertexSize][];
            for (int b = 0; b < vertexSize; b++)
            {
                planes[b] = new byte[vertexCountAligned];
                pos = DecodeBytes(data, pos, end, planes[b], vertexCountAligned);
            }

            // De-zigzag + accumulate each byte plane against lastVertex, write back interleaved.
            for (int b = 0; b < vertexSize; b++)
            {
                byte p = lastVertex[b];
                byte[] plane = planes[b];
                for (int i = 0; i < vertexCount; i++)
                {
                    byte v = (byte)(Unzigzag8(plane[i]) + p);
                    output[outOffset + i * vertexSize + b] = v;
                    p = v;
                }
            }

            for (int b = 0; b < vertexSize; b++)
                lastVertex[b] = output[outOffset + (vertexCount - 1) * vertexSize + b];

            return pos;
        }

        private static byte Unzigzag8(byte v)
        {
            // decode(v) = ((v&1)!=0) ? ~(v>>1) : (v>>1), all in 8-bit space
            int uv = v;
            int dec = ((uv & 1) != 0) ? ~(uv >> 1) : (uv >> 1);
            return unchecked((byte)dec);
        }

        private static int DecodeBytes(byte[] data, int pos, int end, byte[] buffer, int bufferSize)
        {
            if (bufferSize % kByteGroupSize != 0) throw new InvalidOperationException("meshopt: internal buffer size error");

            int headerSize = (bufferSize / kByteGroupSize + 3) / 4;
            if (end - pos < headerSize) throw new InvalidOperationException("meshopt: truncated group header");

            int header = pos;
            pos += headerSize;

            for (int i = 0; i < bufferSize; i += kByteGroupSize)
            {
                if (end - pos < kByteGroupDecodeLimit) throw new InvalidOperationException("meshopt: truncated byte group");

                int headerOffset = i / kByteGroupSize;
                int bitsK = (data[header + headerOffset / 4] >> ((headerOffset % 4) * 2)) & 3;

                pos = DecodeBytesGroup(data, pos, buffer, i, kBitsV0[bitsK]);
            }

            return pos;
        }

        private static int DecodeBytesGroup(byte[] data, int pos, byte[] buffer, int bufferOffset, int bits)
        {
            switch (bits)
            {
                case 0:
                    for (int i = 0; i < kByteGroupSize; i++) buffer[bufferOffset + i] = 0;
                    return pos;

                case 2:
                {
                    int dataVar = pos + 4;
                    int outI = bufferOffset;
                    for (int g = 0; g < 4; g++)
                    {
                        byte b = data[pos + g];
                        for (int s = 0; s < 4; s++)
                        {
                            int enc = (b >> 6) & 3;
                            b <<= 2;
                            byte val;
                            if (enc == 3) { val = data[dataVar]; dataVar++; }
                            else val = (byte)enc;
                            buffer[outI++] = val;
                        }
                    }
                    return dataVar;
                }

                case 4:
                {
                    int dataVar = pos + 8;
                    int outI = bufferOffset;
                    for (int g = 0; g < 8; g++)
                    {
                        byte b = data[pos + g];
                        for (int s = 0; s < 2; s++)
                        {
                            int enc = (b >> 4) & 15;
                            b <<= 4;
                            byte val;
                            if (enc == 15) { val = data[dataVar]; dataVar++; }
                            else val = (byte)enc;
                            buffer[outI++] = val;
                        }
                    }
                    return dataVar;
                }

                case 8:
                    Array.Copy(data, pos, buffer, bufferOffset, kByteGroupSize);
                    return pos + kByteGroupSize;

                default:
                    throw new InvalidOperationException("meshopt: unexpected bit width " + bits);
            }
        }

        // ------------------------------------------------------------------
        // Mode 1: TRIANGLES (index buffer, triangle-list topology)
        // ------------------------------------------------------------------

        public static uint[] DecodeIndexBuffer(byte[] buffer, int indexCount)
        {
            if (indexCount % 3 != 0) throw new InvalidOperationException("meshopt: triangle index count must be a multiple of 3");
            if (buffer.Length < 1 + indexCount / 3 + 16) throw new InvalidOperationException("meshopt: index buffer too small");
            if ((buffer[0] & 0xf0) != 0xe0) throw new InvalidOperationException("meshopt: bad triangle header 0x" + buffer[0].ToString("x2"));
            int version = buffer[0] & 0x0f;
            if (version > 1) throw new InvalidOperationException("meshopt: unsupported triangle codec version " + version);

            uint[] edgeFifoA = new uint[16];
            uint[] edgeFifoB = new uint[16];
            uint[] vertexFifo = new uint[16];
            int edgeOffset = 0, vertexOffset = 0;

            uint next = 0, last = 0;
            int fecmax = version >= 1 ? 13 : 15;

            int code = 1;
            int codeEnd = code + indexCount / 3;
            int data = codeEnd;
            int dataSafeEnd = buffer.Length - 16;
            int codeauxTable = dataSafeEnd;

            uint[] result = new uint[indexCount];
            int outIdx = 0;

            while (code < codeEnd)
            {
                byte codetri = buffer[code++];

                if (codetri < 0xf0)
                {
                    int fe = codetri >> 4;
                    uint a = edgeFifoA[(edgeOffset - 1 - fe) & 15];
                    uint b = edgeFifoB[(edgeOffset - 1 - fe) & 15];
                    uint c;

                    int fec = codetri & 15;

                    if (fec < fecmax)
                    {
                        uint cf = vertexFifo[(vertexOffset - 1 - fec) & 15];
                        c = (fec == 0) ? next : cf;
                        int fec0 = fec == 0 ? 1 : 0;
                        next += (uint)fec0;
                        PushVertexFifo(vertexFifo, c, ref vertexOffset, fec0);
                    }
                    else
                    {
                        if (data > dataSafeEnd) throw new InvalidOperationException("meshopt: triangle stream truncated");
                        if (fec != 15) { last = (uint)(last + (fec * 2 - 27)); c = last; }
                        else { c = DecodeIndex(buffer, ref data, last); last = c; }
                        PushVertexFifo(vertexFifo, c, ref vertexOffset, 1);
                    }

                    PushEdgeFifo(edgeFifoA, edgeFifoB, c, b, ref edgeOffset);
                    PushEdgeFifo(edgeFifoA, edgeFifoB, a, c, ref edgeOffset);

                    result[outIdx++] = a; result[outIdx++] = b; result[outIdx++] = c;
                }
                else
                {
                    if (codetri < 0xfe)
                    {
                        byte codeaux = buffer[codeauxTable + (codetri & 15)];
                        int feb = codeaux >> 4;
                        int fec = codeaux & 15;

                        uint a = next++;

                        uint bf = vertexFifo[(vertexOffset - feb) & 15];
                        uint b = (feb == 0) ? next : bf;
                        int feb0 = feb == 0 ? 1 : 0;
                        next += (uint)feb0;

                        uint cf = vertexFifo[(vertexOffset - fec) & 15];
                        uint c = (fec == 0) ? next : cf;
                        int fec0 = fec == 0 ? 1 : 0;
                        next += (uint)fec0;

                        result[outIdx++] = a; result[outIdx++] = b; result[outIdx++] = c;

                        PushVertexFifo(vertexFifo, a, ref vertexOffset, 1);
                        PushVertexFifo(vertexFifo, b, ref vertexOffset, feb0);
                        PushVertexFifo(vertexFifo, c, ref vertexOffset, fec0);

                        PushEdgeFifo(edgeFifoA, edgeFifoB, b, a, ref edgeOffset);
                        PushEdgeFifo(edgeFifoA, edgeFifoB, c, b, ref edgeOffset);
                        PushEdgeFifo(edgeFifoA, edgeFifoB, a, c, ref edgeOffset);
                    }
                    else
                    {
                        if (data > dataSafeEnd) throw new InvalidOperationException("meshopt: triangle stream truncated");
                        byte codeaux = buffer[data++];

                        int fea = codetri == 0xfe ? 0 : 15;
                        int feb = codeaux >> 4;
                        int fec = codeaux & 15;

                        if (codeaux == 0) next = 0;

                        uint a = (fea == 0) ? next++ : 0;
                        uint b = (feb == 0) ? next++ : vertexFifo[(vertexOffset - feb) & 15];
                        uint c = (fec == 0) ? next++ : vertexFifo[(vertexOffset - fec) & 15];

                        if (fea == 15) { last = a = DecodeIndex(buffer, ref data, last); }
                        if (feb == 15) { last = b = DecodeIndex(buffer, ref data, last); }
                        if (fec == 15) { last = c = DecodeIndex(buffer, ref data, last); }

                        result[outIdx++] = a; result[outIdx++] = b; result[outIdx++] = c;

                        PushVertexFifo(vertexFifo, a, ref vertexOffset, 1);
                        PushVertexFifo(vertexFifo, b, ref vertexOffset, (feb == 0 || feb == 15) ? 1 : 0);
                        PushVertexFifo(vertexFifo, c, ref vertexOffset, (fec == 0 || fec == 15) ? 1 : 0);

                        PushEdgeFifo(edgeFifoA, edgeFifoB, b, a, ref edgeOffset);
                        PushEdgeFifo(edgeFifoA, edgeFifoB, c, b, ref edgeOffset);
                        PushEdgeFifo(edgeFifoA, edgeFifoB, a, c, ref edgeOffset);
                    }
                }
            }

            if (data != dataSafeEnd) throw new InvalidOperationException("meshopt: triangle stream left unread data");

            return result;
        }

        private static void PushEdgeFifo(uint[] a, uint[] b, uint va, uint vb, ref int offset)
        {
            a[offset] = va; b[offset] = vb;
            offset = (offset + 1) & 15;
        }

        private static void PushVertexFifo(uint[] fifo, uint v, ref int offset, int cond)
        {
            fifo[offset] = v;
            offset = (offset + cond) & 15;
        }

        private static uint DecodeIndex(byte[] data, ref int pos, uint last)
        {
            uint v = DecodeVByte(data, ref pos);
            int d = unchecked((int)(v >> 1) ^ -(int)(v & 1));
            return unchecked((uint)((int)last + d));
        }

        private static uint DecodeVByte(byte[] data, ref int pos)
        {
            byte lead = data[pos++];
            if (lead < 128) return lead;

            uint result = (uint)(lead & 127);
            int shift = 7;
            for (int i = 0; i < 4; i++)
            {
                byte group = data[pos++];
                result |= (uint)(group & 127) << shift;
                shift += 7;
                if (group < 128) break;
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Mode 2: INDICES (generic index sequence)
        // ------------------------------------------------------------------

        public static uint[] DecodeIndexSequence(byte[] buffer, int indexCount)
        {
            if (buffer.Length < 1 + indexCount + 4) throw new InvalidOperationException("meshopt: index sequence buffer too small");
            if ((buffer[0] & 0xf0) != 0xd0) throw new InvalidOperationException("meshopt: bad index-sequence header 0x" + buffer[0].ToString("x2"));
            int version = buffer[0] & 0x0f;
            if (version > 1) throw new InvalidOperationException("meshopt: unsupported index-sequence codec version " + version);

            int pos = 1;
            int dataSafeEnd = buffer.Length - 4;
            uint[] last = new uint[2];
            uint[] result = new uint[indexCount];

            for (int i = 0; i < indexCount; i++)
            {
                if (pos >= dataSafeEnd) throw new InvalidOperationException("meshopt: index sequence truncated");

                uint v = DecodeVByte(buffer, ref pos);
                int current = (int)(v & 1);
                v >>= 1;
                int d = unchecked((int)(v >> 1) ^ -(int)(v & 1));
                uint index = unchecked((uint)((int)last[current] + d));
                last[current] = index;
                result[i] = index;
            }

            if (pos != dataSafeEnd) throw new InvalidOperationException("meshopt: index sequence left unread data");

            return result;
        }

        // ------------------------------------------------------------------
        // Appendix B: post-decode filters (applied in place, byte buffer holds
        // `count` elements of `stride` bytes each, already delta-decoded).
        // ------------------------------------------------------------------

        public static void DecodeFilterOct(byte[] buffer, int count, int stride)
        {
            // Spec pseudocode is specified in float32_t throughout; matching that precision
            // (rather than using double) matters -- it changes rounding at the ~1-ULP boundary.
            if (stride != 4 && stride != 8) throw new InvalidOperationException("meshopt: octahedral filter needs stride 4 or 8");
            bool wide = stride == 8; // 4x16-bit components instead of 4x8-bit

            for (int i = 0; i < count; i++)
            {
                int off = i * stride;
                float x, y, one;
                if (!wide)
                {
                    sbyte ix = unchecked((sbyte)buffer[off + 0]);
                    sbyte iy = unchecked((sbyte)buffer[off + 1]);
                    sbyte iz = unchecked((sbyte)buffer[off + 2]);
                    one = iz;
                    x = ix / one;
                    y = iy / one;
                }
                else
                {
                    short ix = (short)(buffer[off + 0] | (buffer[off + 1] << 8));
                    short iy = (short)(buffer[off + 2] | (buffer[off + 3] << 8));
                    short iz = (short)(buffer[off + 4] | (buffer[off + 5] << 8));
                    one = iz;
                    x = ix / one;
                    y = iy / one;
                }

                float z = 1.0f - Math.Abs(x) - Math.Abs(y);
                float t = Math.Min(z, 0.0f);
                x -= CopySignF(t, x);
                y -= CopySignF(t, y);

                float len = (float)Math.Sqrt(x * x + y * y + z * z);
                if (len > 0) { x /= len; y /= len; z /= len; }

                int max = wide ? 32767 : 127;
                int ox = RoundF(x * max);
                int oy = RoundF(y * max);
                int oz = RoundF(z * max);

                if (!wide)
                {
                    buffer[off + 0] = unchecked((byte)(sbyte)ox);
                    buffer[off + 1] = unchecked((byte)(sbyte)oy);
                    buffer[off + 2] = unchecked((byte)(sbyte)oz);
                    // buffer[off+3] passed through verbatim (w component untouched)
                }
                else
                {
                    WriteInt16(buffer, off + 0, (short)ox);
                    WriteInt16(buffer, off + 2, (short)oy);
                    WriteInt16(buffer, off + 4, (short)oz);
                    // bytes off+6..off+7 (w) passed through verbatim
                }
            }
        }

        public static void DecodeFilterQuat(byte[] buffer, int count, int stride)
        {
            // Spec pseudocode is specified in float32_t throughout; match that precision.
            if (stride != 8) throw new InvalidOperationException("meshopt: quaternion filter needs stride 8");
            const float range = 0.70710678118654752440084436210485f; // 1/sqrt(2)

            for (int i = 0; i < count; i++)
            {
                int off = i * stride;
                short i0 = ReadInt16(buffer, off + 0);
                short i1 = ReadInt16(buffer, off + 2);
                short i2 = ReadInt16(buffer, off + 4);
                short i3 = ReadInt16(buffer, off + 6);

                float one = (i3 | 3);
                float x = i0 / one * range;
                float y = i1 / one * range;
                float z = i2 / one * range;
                float w = (float)Math.Sqrt(Math.Max(0.0f, 1.0f - x * x - y * y - z * z));

                int maxcomp = i3 & 3;

                short ox = (short)RoundF(x * 32767.0f);
                short oy = (short)RoundF(y * 32767.0f);
                short oz = (short)RoundF(z * 32767.0f);
                short ow = (short)RoundF(w * 32767.0f);

                short[] outc = new short[4];
                outc[(maxcomp + 1) % 4] = ox;
                outc[(maxcomp + 2) % 4] = oy;
                outc[(maxcomp + 3) % 4] = oz;
                outc[(maxcomp + 0) % 4] = ow;

                WriteInt16(buffer, off + 0, outc[0]);
                WriteInt16(buffer, off + 2, outc[1]);
                WriteInt16(buffer, off + 4, outc[2]);
                WriteInt16(buffer, off + 6, outc[3]);
            }
        }

        public static void DecodeFilterExp(byte[] buffer, int count, int stride)
        {
            if (stride % 4 != 0) throw new InvalidOperationException("meshopt: exponential filter needs a stride divisible by 4");
            int comps = stride / 4;

            for (int i = 0; i < count; i++)
            {
                for (int c = 0; c < comps; c++)
                {
                    int off = i * stride + c * 4;
                    int raw = buffer[off] | (buffer[off + 1] << 8) | (buffer[off + 2] << 16) | (buffer[off + 3] << 24);
                    int e = raw >> 24; // arithmetic shift: sign-extends the exponent
                    int m = (raw << 8) >> 8; // sign-extends the 24-bit mantissa
                    // Reference computes this as ldexp(float(m), e) via a direct IEEE-754 bit
                    // construction of 2^e (see meshoptimizer's decodeFilterExp), not pow() in
                    // double precision -- replicate that exactly rather than approximate it.
                    float pow2e = BitConverter.ToSingle(BitConverter.GetBytes(unchecked((e + 127) << 23)), 0);
                    float value = pow2e * (float)m;
                    byte[] fb = BitConverter.GetBytes(value);
                    buffer[off] = fb[0]; buffer[off + 1] = fb[1]; buffer[off + 2] = fb[2]; buffer[off + 3] = fb[3];
                }
            }
        }

        private static float CopySignF(float magnitude, float sign)
        {
            float m = Math.Abs(magnitude);
            return sign < 0 ? -m : m;
        }

        // C's round(): half away from zero, evaluated at the given (float) precision --
        // matters here since it runs on values already computed in float32.
        private static int RoundF(float v)
        {
            return v >= 0 ? (int)Math.Floor(v + 0.5f) : (int)Math.Ceiling(v - 0.5f);
        }

        private static short ReadInt16(byte[] b, int off) => (short)(b[off] | (b[off + 1] << 8));
        private static void WriteInt16(byte[] b, int off, short v) { b[off] = (byte)(v & 0xff); b[off + 1] = (byte)((v >> 8) & 0xff); }
    }
}
