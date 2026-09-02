using System;
using T = FISHHWB.MeshyImporter.Editor.MeshyWebpVp8Tables;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// From-scratch lossy WebP (VP8, intra/keyframe only) decoder, ported faithfully
    /// from libwebp (BSD licensed, https://github.com/webmproject/libwebp):
    ///   src/dec/vp8_dec.c, tree_dec.c, quant_dec.c, frame_dec.c, io_dec.c,
    ///   src/dsp/dec.c, dec_clip_tables.c, upsampling.c, yuv.h
    /// This targets exactly what Meshy's exported textures use: single-keyframe
    /// lossy VP8 (RIFF "WEBP"/"VP8 "), no alpha, no scaling/cropping. It decodes
    /// the whole image in one pass into full-frame planes (rather than libwebp's
    /// small rotating row cache), which is mathematically equivalent but far
    /// simpler to implement correctly - see design notes in the accompanying
    /// summary. Output matches libwebp's default WebPDecodeRGB (fancy upsampling,
    /// no dithering, no cropping) bit-for-bit.
    /// </summary>
    internal static class MeshyWebpVp8
    {
        // ---------------------------------------------------------------
        // Public entry point
        // ---------------------------------------------------------------

        /// <summary>Decode a complete .webp file's bytes (RIFF/WEBP/"VP8 ") to RGB24.</summary>
        public static bool TryDecodeRgb(byte[] fileBytes, out int width, out int height, out byte[] rgb)
        {
            width = 0; height = 0; rgb = null;
            if (!TryFindVp8Chunk(fileBytes, out int vp8Offset, out int vp8Size)) return false;

            var dec = new Decoder();
            if (!dec.ParseAndDecode(fileBytes, vp8Offset, vp8Size)) return false;

            width = dec.PicWidth;
            height = dec.PicHeight;
            rgb = dec.ToRgb();
            return true;
        }

        private static bool TryFindVp8Chunk(byte[] data, out int offset, out int size)
        {
            offset = 0; size = 0;
            if (data == null || data.Length < 20) return false;
            if (data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F') return false;
            if (data[8] != (byte)'W' || data[9] != (byte)'E' || data[10] != (byte)'B' || data[11] != (byte)'P') return false;

            int pos = 12;
            while (pos + 8 <= data.Length)
            {
                char c0 = (char)data[pos], c1 = (char)data[pos + 1], c2 = (char)data[pos + 2], c3 = (char)data[pos + 3];
                uint chunkSize = (uint)(data[pos + 4] | (data[pos + 5] << 8) | (data[pos + 6] << 16) | (data[pos + 7] << 24));
                int payloadStart = pos + 8;
                if (c0 == 'V' && c1 == 'P' && c2 == '8' && c3 == ' ')
                {
                    offset = payloadStart;
                    size = (int)Math.Min(chunkSize, (uint)(data.Length - payloadStart));
                    return true;
                }
                if (c0 == 'V' && c1 == 'P' && c2 == '8' && c3 == 'L') return false; // lossless: not supported here
                long disk = 8L + chunkSize + (chunkSize & 1);
                pos += (int)disk;
            }
            return false;
        }

        // ---------------------------------------------------------------
        // Small clip helpers (replace libwebp's static lookup tables -
        // see src/dsp/dec_clip_tables.c; these are exactly equivalent).
        // ---------------------------------------------------------------
        private static int Clip8b(int v) => (v & ~0xff) == 0 ? v : (v < 0 ? 0 : 255);
        private static int KAbs0(int v) => v < 0 ? -v : v;
        private static int SClip1(int v) => v < -128 ? -128 : (v > 127 ? 127 : v);
        private static int SClip2(int v) => v < -16 ? -16 : (v > 15 ? 15 : v);
        private static int KClip1(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);

        // ---------------------------------------------------------------
        // Header structs
        // ---------------------------------------------------------------
        private sealed class SegmentHeader
        {
            public bool UseSegment;
            public bool UpdateMap;
            public bool AbsoluteDelta = true;
            public readonly int[] Quantizer = new int[T.NUM_MB_SEGMENTS];
            public readonly int[] FilterStrength = new int[T.NUM_MB_SEGMENTS];
        }

        private sealed class FilterHeader
        {
            public bool Simple;
            public int Level;
            public int Sharpness;
            public bool UseLfDelta;
            public readonly int[] RefLfDelta = new int[T.NUM_REF_LF_DELTAS];
            public readonly int[] ModeLfDelta = new int[T.NUM_MODE_LF_DELTAS];
        }

        private struct QuantMatrix
        {
            public int Y1Dc, Y1Ac, Y2Dc, Y2Ac, UvDc, UvAc;
        }

        private struct FilterInfo
        {
            public int FLimit;
            public int FIlevel;
            public int HevThresh;
            public bool FInner;
        }

        private sealed class MbData
        {
            public int Segment;
            public bool IsI4x4;
            public readonly int[] IModes = new int[16];
            public int UvMode;
            public readonly short[] Coeffs = new short[384];
            public readonly int[] BlockCodeY = new int[16]; // 0..3 per 4x4 luma block
            public readonly int[] BlockCodeU = new int[4];
            public readonly int[] BlockCodeV = new int[4];
            public bool Skip; // true => residuals fully zero (no coding needed)
        }

        private struct MbCtx
        {
            public int Nz; // bit0..3 luma cols nz, bit4-5 U cols nz, bit6-7 V cols nz
            public int NzDc; // whether Y2/WHT DC block was non-zero (0/1)
        }

        // ---------------------------------------------------------------
        // Decoder
        // ---------------------------------------------------------------
        private sealed class Decoder
        {
            public int PicWidth, PicHeight;
            private int _mbW, _mbH;
            private int _paddedW, _paddedH;

            // header state
            private readonly SegmentHeader _segHdr = new SegmentHeader();
            private readonly FilterHeader _filterHdr = new FilterHeader();
            private int _filterType; // 0 none, 1 simple, 2 complex
            private readonly QuantMatrix[] _dqm = new QuantMatrix[T.NUM_MB_SEGMENTS];
            private readonly FilterInfo[,] _fstrengths = new FilterInfo[T.NUM_MB_SEGMENTS, 2];

            // proba state
            private readonly byte[] _segmentProbas = new byte[T.MB_FEATURE_TREE_PROBS];
            private readonly byte[,,,] _coeffProbas = new byte[T.NUM_TYPES, T.NUM_BANDS, T.NUM_CTX, T.NUM_PROBAS];
            private bool _useSkipProba;
            private int _skipP;

            private Vp8BitReader _br; // partition 0 (headers, modes)
            private Vp8BitReader[] _parts; // token partitions
            private int _numPartsMinusOne;

            // planes: full padded canvas with 1-pixel border (top row / left col)
            // index (r,c) for r in [-1,H-1], c in [-1,W-1] -> data[(r+1)*stride + (c+1)]
            private byte[] _y, _u, _v;
            private int _yStride, _uvStride;

            // per-MB persistent context (top row) + transient (left, reset per row)
            private MbCtx[] _topCtx;
            private MbCtx _leftCtx;
            private int[] _intraT; // 4 per mb column
            private readonly int[] _intraL = new int[4];

            private FilterInfo[,] _fInfo; // [mbY, mbX]
            private bool[,] _fInner;

            public bool ParseAndDecode(byte[] data, int offset, int size)
            {
                if (size < 10) return false;

                int p = offset;
                uint bits = (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16));
                bool keyFrame = (bits & 1) == 0;
                int profile = (int)((bits >> 1) & 7);
                bool show = ((bits >> 4) & 1) != 0;
                int partitionLength = (int)(bits >> 5);
                p += 3;
                if (!keyFrame || profile > 3 || !show) return false;

                if (p + 7 > offset + size) return false;
                if (!(data[p] == 0x9d && data[p + 1] == 0x01 && data[p + 2] == 0x2a)) return false;
                PicWidth = ((data[p + 4] << 8) | data[p + 3]) & 0x3fff;
                PicHeight = ((data[p + 6] << 8) | data[p + 5]) & 0x3fff;
                p += 7;

                if (PicWidth == 0 || PicHeight == 0) return false;
                _mbW = (PicWidth + 15) >> 4;
                _mbH = (PicHeight + 15) >> 4;
                _paddedW = _mbW * 16;
                _paddedH = _mbH * 16;

                int end = offset + size;
                if (p + partitionLength > end) return false;

                _br = new Vp8BitReader();
                _br.Init(data, p, partitionLength);
                int afterP0 = p + partitionLength;
                int remaining = end - afterP0;

                // colorspace + clamp type (ignored - no effect on decode logic here)
                _br.GetBit(0x80);
                _br.GetBit(0x80);

                if (!ParseSegmentHeader()) return false;
                if (!ParseFilterHeader()) return false;
                if (!ParsePartitions(data, afterP0, remaining)) return false;

                ParseQuant();

                _br.GetBit(0x80); // ignore update_proba
                ParseProba();

                AllocatePlanes();
                PrecomputeFilterStrengths();

                DecodeAllMacroblocks();

                if (_filterType > 0)
                {
                    ApplyLoopFilter();
                }

                return true;
            }

            // -----------------------------------------------------------
            // Header parsing (Paragraph 9.x)
            // -----------------------------------------------------------
            private bool ParseSegmentHeader()
            {
                var hdr = _segHdr;
                hdr.UseSegment = _br.GetBit(0x80) != 0;
                if (hdr.UseSegment)
                {
                    hdr.UpdateMap = _br.GetBit(0x80) != 0;
                    if (_br.GetBit(0x80) != 0) // update data
                    {
                        hdr.AbsoluteDelta = _br.GetBit(0x80) != 0;
                        for (int s = 0; s < T.NUM_MB_SEGMENTS; ++s)
                            hdr.Quantizer[s] = _br.GetBit(0x80) != 0 ? _br.GetSignedValue(7) : 0;
                        for (int s = 0; s < T.NUM_MB_SEGMENTS; ++s)
                            hdr.FilterStrength[s] = _br.GetBit(0x80) != 0 ? _br.GetSignedValue(6) : 0;
                    }
                    if (hdr.UpdateMap)
                    {
                        for (int s = 0; s < T.MB_FEATURE_TREE_PROBS; ++s)
                            _segmentProbas[s] = _br.GetBit(0x80) != 0 ? (byte)_br.GetValue(8) : (byte)255;
                    }
                }
                else
                {
                    hdr.UpdateMap = false;
                }
                return true;
            }

            private bool ParseFilterHeader()
            {
                var hdr = _filterHdr;
                hdr.Simple = _br.GetBit(0x80) != 0;
                hdr.Level = (int)_br.GetValue(6);
                hdr.Sharpness = (int)_br.GetValue(3);
                hdr.UseLfDelta = _br.GetBit(0x80) != 0;
                if (hdr.UseLfDelta)
                {
                    if (_br.GetBit(0x80) != 0)
                    {
                        for (int i = 0; i < T.NUM_REF_LF_DELTAS; ++i)
                            if (_br.GetBit(0x80) != 0) hdr.RefLfDelta[i] = _br.GetSignedValue(6);
                        for (int i = 0; i < T.NUM_MODE_LF_DELTAS; ++i)
                            if (_br.GetBit(0x80) != 0) hdr.ModeLfDelta[i] = _br.GetSignedValue(6);
                    }
                }
                _filterType = (hdr.Level == 0) ? 0 : (hdr.Simple ? 1 : 2);
                return true;
            }

            private bool ParsePartitions(byte[] data, int bufStart, int size)
            {
                _numPartsMinusOne = (1 << (int)_br.GetValue(2)) - 1;
                int lastPart = _numPartsMinusOne;
                if (size < 3 * lastPart) return false;

                _parts = new Vp8BitReader[lastPart + 1];
                int szPtr = bufStart;
                int partStart = bufStart + lastPart * 3;
                int sizeLeft = size - lastPart * 3;
                for (int pIdx = 0; pIdx < lastPart; ++pIdx)
                {
                    int psize = data[szPtr] | (data[szPtr + 1] << 8) | (data[szPtr + 2] << 16);
                    if (psize > sizeLeft) psize = sizeLeft;
                    var r = new Vp8BitReader();
                    r.Init(data, partStart, psize);
                    _parts[pIdx] = r;
                    partStart += psize;
                    sizeLeft -= psize;
                    szPtr += 3;
                }
                var last = new Vp8BitReader();
                last.Init(data, partStart, sizeLeft);
                _parts[lastPart] = last;
                return true;
            }

            private void ParseQuant()
            {
                int baseQ0 = (int)_br.GetValue(7);
                int dqy1Dc = _br.GetBit(0x80) != 0 ? _br.GetSignedValue(4) : 0;
                int dqy2Dc = _br.GetBit(0x80) != 0 ? _br.GetSignedValue(4) : 0;
                int dqy2Ac = _br.GetBit(0x80) != 0 ? _br.GetSignedValue(4) : 0;
                int dquvDc = _br.GetBit(0x80) != 0 ? _br.GetSignedValue(4) : 0;
                int dquvAc = _br.GetBit(0x80) != 0 ? _br.GetSignedValue(4) : 0;

                for (int i = 0; i < T.NUM_MB_SEGMENTS; ++i)
                {
                    int q;
                    if (_segHdr.UseSegment)
                    {
                        q = _segHdr.Quantizer[i];
                        if (!_segHdr.AbsoluteDelta) q += baseQ0;
                    }
                    else
                    {
                        if (i > 0) { _dqm[i] = _dqm[0]; continue; }
                        q = baseQ0;
                    }

                    int Clip(int v, int m) => v < 0 ? 0 : (v > m ? m : v);
                    var m2 = new QuantMatrix();
                    m2.Y1Dc = T.KDcTable[Clip(q + dqy1Dc, 127)];
                    m2.Y1Ac = T.KAcTable[Clip(q + 0, 127)];
                    m2.Y2Dc = T.KDcTable[Clip(q + dqy2Dc, 127)] * 2;
                    int y2ac = (T.KAcTable[Clip(q + dqy2Ac, 127)] * 101581) >> 16;
                    if (y2ac < 8) y2ac = 8;
                    m2.Y2Ac = y2ac;
                    m2.UvDc = T.KDcTable[Clip(q + dquvDc, 117)];
                    m2.UvAc = T.KAcTable[Clip(q + dquvAc, 127)];
                    _dqm[i] = m2;
                }
            }

            private void ParseProba()
            {
                for (int t = 0; t < T.NUM_TYPES; ++t)
                    for (int b = 0; b < T.NUM_BANDS; ++b)
                        for (int c = 0; c < T.NUM_CTX; ++c)
                            for (int pp = 0; pp < T.NUM_PROBAS; ++pp)
                            {
                                int v = _br.GetBit(T.CoeffsUpdateProba[t, b, c, pp]) != 0
                                    ? (int)_br.GetValue(8)
                                    : T.CoeffsProba0[t, b, c, pp];
                                _coeffProbas[t, b, c, pp] = (byte)v;
                            }
                _useSkipProba = _br.GetBit(0x80) != 0;
                _skipP = _useSkipProba ? (int)_br.GetValue(8) : 0;
            }

            private void PrecomputeFilterStrengths()
            {
                if (_filterType <= 0) return;
                var hdr = _filterHdr;
                for (int s = 0; s < T.NUM_MB_SEGMENTS; ++s)
                {
                    int baseLevel;
                    if (_segHdr.UseSegment)
                    {
                        baseLevel = _segHdr.FilterStrength[s];
                        if (!_segHdr.AbsoluteDelta) baseLevel += hdr.Level;
                    }
                    else
                    {
                        baseLevel = hdr.Level;
                    }
                    for (int i4x4 = 0; i4x4 <= 1; ++i4x4)
                    {
                        int level = baseLevel;
                        if (hdr.UseLfDelta)
                        {
                            level += hdr.RefLfDelta[0];
                            if (i4x4 != 0) level += hdr.ModeLfDelta[0];
                        }
                        level = level < 0 ? 0 : (level > 63 ? 63 : level);
                        var info = new FilterInfo();
                        if (level > 0)
                        {
                            int ilevel = level;
                            if (hdr.Sharpness > 0)
                            {
                                ilevel = (hdr.Sharpness > 4) ? (ilevel >> 2) : (ilevel >> 1);
                                if (ilevel > 9 - hdr.Sharpness) ilevel = 9 - hdr.Sharpness;
                            }
                            if (ilevel < 1) ilevel = 1;
                            info.FIlevel = ilevel;
                            info.FLimit = 2 * level + ilevel;
                            info.HevThresh = (level >= 40) ? 2 : (level >= 15) ? 1 : 0;
                        }
                        else
                        {
                            info.FLimit = 0;
                        }
                        info.FInner = i4x4 != 0;
                        _fstrengths[s, i4x4] = info;
                    }
                }
            }

            private void AllocatePlanes()
            {
                _yStride = _paddedW + 1;
                _y = new byte[_yStride * (_paddedH + 1)];
                int uvW = _paddedW / 2, uvH = _paddedH / 2;
                _uvStride = uvW + 1;
                _u = new byte[_uvStride * (uvH + 1)];
                _v = new byte[_uvStride * (uvH + 1)];

                // border init: row -1 = 127 everywhere, col -1 = 129 everywhere.
                FillBorder(_y, _yStride, _paddedW, _paddedH);
                FillBorder(_u, _uvStride, uvW, uvH);
                FillBorder(_v, _uvStride, uvW, uvH);

                _topCtx = new MbCtx[_mbW];
                _intraT = new int[4 * _mbW]; // default 0 = B_DC_PRED

                _fInfo = new FilterInfo[_mbH, _mbW];
                _fInner = new bool[_mbH, _mbW];
            }

            private static void FillBorder(byte[] plane, int stride, int w, int h)
            {
                // row -1 (index row 0 in storage) fully 127, including col -1.
                for (int c = -1; c < w; ++c) plane[Idx(-1, c, stride)] = 127;
                // col -1 for every real row = 129.
                for (int r = 0; r < h; ++r) plane[Idx(r, -1, stride)] = 129;
            }

            private static int Idx(int r, int c, int stride) => (r + 1) * stride + (c + 1);

            // -----------------------------------------------------------
            // Main per-MB loop
            // -----------------------------------------------------------
            private void DecodeAllMacroblocks()
            {
                var rowData = new MbData[_mbW];
                for (int i = 0; i < _mbW; ++i) rowData[i] = new MbData();

                for (int mbY = 0; mbY < _mbH; ++mbY)
                {
                    _leftCtx = default;
                    Array.Clear(_intraL, 0, 4);

                    ParseIntraModeRow(mbY, rowData);

                    var tokenBr = _parts[mbY & _numPartsMinusOne];
                    for (int mbX = 0; mbX < _mbW; ++mbX)
                    {
                        DecodeMbResiduals(tokenBr, mbX, mbY, rowData[mbX]);
                    }

                    ReconstructRow(mbY, rowData);
                }
            }

            // -----------------------------------------------------------
            // Intra mode parsing (tree_dec.c: ParseIntraMode / VP8ParseIntraModeRow)
            // -----------------------------------------------------------
            private void ParseIntraModeRow(int mbY, MbData[] rowData)
            {
                for (int mbX = 0; mbX < _mbW; ++mbX)
                {
                    var block = rowData[mbX];
                    int topBase = 4 * mbX;

                    if (_segHdr.UpdateMap)
                    {
                        block.Segment = _br.GetBit(_segmentProbas[0]) == 0
                            ? _br.GetBit(_segmentProbas[1])
                            : _br.GetBit(_segmentProbas[2]) + 2;
                    }
                    else
                    {
                        block.Segment = 0;
                    }
                    block.Skip = _useSkipProba && _br.GetBit(_skipP) != 0;

                    block.IsI4x4 = _br.GetBit(145) == 0;
                    if (!block.IsI4x4)
                    {
                        int ymode = _br.GetBit(156) != 0
                            ? (_br.GetBit(128) != 0 ? T.TM_PRED : T.H_PRED)
                            : (_br.GetBit(163) != 0 ? T.V_PRED : T.DC_PRED);
                        block.IModes[0] = ymode;
                        for (int k = 0; k < 4; ++k) { _intraT[topBase + k] = ymode; _intraL[k] = ymode; }
                    }
                    else
                    {
                        for (int y = 0; y < 4; ++y)
                        {
                            int ymode = _intraL[y];
                            for (int x = 0; x < 4; ++x)
                            {
                                int topMode = _intraT[topBase + x];
                                // kBModesProba[top][ymode][.]
                                ymode = ReadBMode(topMode, ymode);
                                _intraT[topBase + x] = ymode;
                                block.IModes[y * 4 + x] = ymode;
                            }
                            _intraL[y] = ymode;
                        }
                    }

                    block.UvMode = _br.GetBit(142) == 0 ? T.DC_PRED
                                  : _br.GetBit(114) == 0 ? T.V_PRED
                                  : _br.GetBit(183) != 0 ? T.TM_PRED : T.H_PRED;
                }
            }

            private int ReadBMode(int top, int left)
            {
                // kBModesProba[top][left][0..8], hardcoded tree matching tree_dec.c.
                int p0 = T.KBModesProba[top, left, 0];
                if (_br.GetBit(p0) == 0) return T.B_DC_PRED;
                int p1 = T.KBModesProba[top, left, 1];
                if (_br.GetBit(p1) == 0) return T.B_TM_PRED;
                int p2 = T.KBModesProba[top, left, 2];
                if (_br.GetBit(p2) == 0) return T.B_VE_PRED;
                int p3 = T.KBModesProba[top, left, 3];
                if (_br.GetBit(p3) == 0)
                {
                    int p4 = T.KBModesProba[top, left, 4];
                    if (_br.GetBit(p4) == 0) return T.B_HE_PRED;
                    int p5 = T.KBModesProba[top, left, 5];
                    return _br.GetBit(p5) == 0 ? T.B_RD_PRED : T.B_VR_PRED;
                }
                else
                {
                    int p6 = T.KBModesProba[top, left, 6];
                    if (_br.GetBit(p6) == 0) return T.B_LD_PRED;
                    int p7 = T.KBModesProba[top, left, 7];
                    if (_br.GetBit(p7) == 0) return T.B_VL_PRED;
                    int p8 = T.KBModesProba[top, left, 8];
                    return _br.GetBit(p8) == 0 ? T.B_HD_PRED : T.B_HU_PRED;
                }
            }

            // -----------------------------------------------------------
            // Residual (token) decoding (vp8_dec.c: GetLargeValue / GetCoeffsFast /
            // ParseResiduals)
            // -----------------------------------------------------------
            private static int GetLargeValue(Vp8BitReader br, int p3, int p4, int p5, int p6, int p7, int p8, int p9, int p10)
            {
                int v;
                if (br.GetBit(p3) == 0)
                {
                    if (br.GetBit(p4) == 0) v = 2;
                    else v = 3 + br.GetBit(p5);
                }
                else
                {
                    if (br.GetBit(p6) == 0)
                    {
                        if (br.GetBit(p7) == 0) v = 5 + br.GetBit(159);
                        else { v = 7 + 2 * br.GetBit(165); v += br.GetBit(145); }
                    }
                    else
                    {
                        int bit1 = br.GetBit(p8);
                        int bit0 = br.GetBit(bit1 != 0 ? p10 : p9);
                        int cat = 2 * bit1 + bit0;
                        v = 0;
                        byte[] tab = cat == 0 ? T.KCat3 : cat == 1 ? T.KCat4 : cat == 2 ? T.KCat5 : T.KCat6;
                        for (int i = 0; i < tab.Length; ++i) v += v + br.GetBit(tab[i]);
                        v += 3 + (8 << cat);
                    }
                }
                return v;
            }

            // Reads coefficients for one 4x4 block starting at zig-zag position n.
            // probaType/bandCtxStart follow libwebp's band-probability-array chaining.
            // Returns "nz" (position of last non-zero + 1).
            private int GetCoeffs(Vp8BitReader br, int type, int ctx, int firstBandIdx, int dqDc, int dqAc, int n, short[] outArr, int outOffset)
            {
                int band = T.KBands[n];
                for (; n < 16; ++n)
                {
                    int p0 = _coeffProbas[type, band, ctx, 0];
                    if (br.GetBit(p0) == 0) return n;
                    for (;;)
                    {
                        int p1 = _coeffProbas[type, band, ctx, 1];
                        if (br.GetBit(p1) != 0) break;
                        ++n;
                        if (n == 16) return 16;
                        band = T.KBands[n];
                        ctx = 0;
                        p0 = _coeffProbas[type, band, ctx, 0];
                        // loop continues reading p1 with ctx=0 at new band
                    }
                    {
                        int p2 = _coeffProbas[type, band, ctx, 2];
                        int v;
                        int nextCtx;
                        if (br.GetBit(p2) == 0)
                        {
                            v = 1;
                            nextCtx = 1;
                        }
                        else
                        {
                            int p3 = _coeffProbas[type, band, ctx, 3];
                            int p4 = _coeffProbas[type, band, ctx, 4];
                            int p5 = _coeffProbas[type, band, ctx, 5];
                            int p6 = _coeffProbas[type, band, ctx, 6];
                            int p7 = _coeffProbas[type, band, ctx, 7];
                            int p8 = _coeffProbas[type, band, ctx, 8];
                            int p9 = _coeffProbas[type, band, ctx, 9];
                            int p10 = _coeffProbas[type, band, ctx, 10];
                            v = GetLargeValue(br, p3, p4, p5, p6, p7, p8, p9, p10);
                            nextCtx = 2;
                        }
                        int dq = n > 0 ? dqAc : dqDc;
                        int signedV = br.GetSigned(v);
                        outArr[outOffset + T.KZigzag[n]] = (short)(signedV * dq);
                        ctx = nextCtx;
                        if (n + 1 < 16) band = T.KBands[n + 1];
                    }
                }
                return 16;
            }

            private void DecodeMbResiduals(Vp8BitReader tokenBr, int mbX, int mbY, MbData block)
            {
                bool skip = block.Skip;
                if (!skip)
                {
                    skip = ParseResiduals(tokenBr, mbX, block);
                }
                else
                {
                    _leftCtx.Nz = 0; _topCtx[mbX].Nz = 0;
                    if (!block.IsI4x4) { _leftCtx.NzDc = 0; _topCtx[mbX].NzDc = 0; }
                    Array.Clear(block.Coeffs, 0, 384);
                    for (int i = 0; i < 16; ++i) block.BlockCodeY[i] = 0;
                    for (int i = 0; i < 4; ++i) { block.BlockCodeU[i] = 0; block.BlockCodeV[i] = 0; }
                }

                if (_filterType > 0)
                {
                    var info = _fstrengths[block.Segment, block.IsI4x4 ? 1 : 0];
                    _fInfo[mbY, mbX] = info;
                    _fInner[mbY, mbX] = info.FInner || !skip;
                }
            }

            private static int NzCode(int nz, int dcNz)
            {
                return (nz > 3) ? 3 : (nz > 1) ? 2 : dcNz;
            }

            private bool ParseResiduals(Vp8BitReader br, int mbX, MbData block)
            {
                var q = _dqm[block.Segment];
                Array.Clear(block.Coeffs, 0, 384);

                int first;
                int lumaType;
                bool anyNz = false;

                if (!block.IsI4x4)
                {
                    short[] dc = new short[16];
                    int ctxDc = _topCtx[mbX].NzDc + _leftCtx.NzDc;
                    int nzDcCount = GetCoeffs(br, 1, ctxDc, 0, q.Y2Dc, q.Y2Ac, 0, dc, 0);
                    int nzDcFlag = nzDcCount > 0 ? 1 : 0;
                    _topCtx[mbX].NzDc = nzDcFlag; _leftCtx.NzDc = nzDcFlag;
                    if (nzDcCount > 1)
                    {
                        TransformWht(dc, block.Coeffs);
                    }
                    else
                    {
                        int dc0 = (dc[0] + 3) >> 3;
                        for (int i = 0; i < 16 * 16; i += 16) block.Coeffs[i] = (short)dc0;
                    }
                    first = 1;
                    lumaType = 0;
                }
                else
                {
                    first = 0;
                    lumaType = 3;
                }

                int tnz = _topCtx[mbX].Nz & 0x0f;
                int lnz = _leftCtx.Nz & 0x0f;
                for (int y = 0; y < 4; ++y)
                {
                    int l = lnz & 1;
                    for (int x = 0; x < 4; ++x)
                    {
                        int ctx = l + (tnz & 1);
                        int off = (y * 4 + x) * 16;
                        int nz = GetCoeffs(br, lumaType, ctx, 0, q.Y1Dc, q.Y1Ac, first, block.Coeffs, off);
                        l = nz > first ? 1 : 0;
                        tnz = (tnz >> 1) | (l << 3);
                        int code = NzCode(nz, block.Coeffs[off] != 0 ? 1 : 0);
                        block.BlockCodeY[y * 4 + x] = code;
                        if (code != 0) anyNz = true;
                    }
                    // NOTE: no extra shift here - with the << 3 accumulation above
                    // (into a 4-wide field), tnz already lands in bits0-3 in the
                    // correct column order after each row's inner x-loop completes.
                    lnz = (lnz >> 1) | (l << 3);
                }
                int outTnz = tnz;
                int outLnz = lnz;

                for (int chIdx = 0; chIdx < 2; ++chIdx)
                {
                    int chShift = 4 + chIdx * 2;
                    int tnzc = (_topCtx[mbX].Nz >> chShift) & 0x3;
                    int lnzc = (_leftCtx.Nz >> chShift) & 0x3;
                    int[] codes = chIdx == 0 ? block.BlockCodeU : block.BlockCodeV;
                    int baseOff = 16 * 16 + chIdx * 4 * 16;
                    for (int y = 0; y < 2; ++y)
                    {
                        int l = lnzc & 1;
                        for (int x = 0; x < 2; ++x)
                        {
                            int ctx = l + (tnzc & 1);
                            int off = baseOff + (y * 2 + x) * 16;
                            int nz = GetCoeffs(br, 2, ctx, 0, q.UvDc, q.UvAc, 0, block.Coeffs, off);
                            l = nz > 0 ? 1 : 0;
                            tnzc = (tnzc >> 1) | (l << 1);
                            int code = NzCode(nz, block.Coeffs[off] != 0 ? 1 : 0);
                            codes[y * 2 + x] = code;
                            if (code != 0) anyNz = true;
                        }
                        // NOTE: no extra shift here - see luma comment above; the
                        // << 1 accumulation already lands tnzc in bits0-1 correctly
                        // after each row's inner x-loop (2-wide field).
                        lnzc = (lnzc >> 1) | (l << 1);
                    }
                    outTnz |= (tnzc << 4) << (chIdx * 2);
                    outLnz |= (lnzc << 4) << (chIdx * 2);
                }

                _topCtx[mbX].Nz = outTnz;
                _leftCtx.Nz = outLnz;

                return !anyNz;
            }

            // -----------------------------------------------------------
            // Transforms (dsp/dec.c Paragraph 14.3/14.4)
            // -----------------------------------------------------------
            private static int Mul1(int a) => ((a * 20091) >> 16) + a;
            private static int Mul2(int a) => (a * 35468) >> 16;

            private static void Store(byte[] plane, int stride, int r, int c, int v)
            {
                int idx = Idx(r, c, stride);
                plane[idx] = (byte)Clip8b(plane[idx] + (v >> 3));
            }

            private static void TransformWht(short[] inArr, short[] outArr)
            {
                int[] tmp = new int[16];
                for (int i = 0; i < 4; ++i)
                {
                    int a0 = inArr[0 + i] + inArr[12 + i];
                    int a1 = inArr[4 + i] + inArr[8 + i];
                    int a2 = inArr[4 + i] - inArr[8 + i];
                    int a3 = inArr[0 + i] - inArr[12 + i];
                    tmp[0 + i] = a0 + a1;
                    tmp[8 + i] = a0 - a1;
                    tmp[4 + i] = a3 + a2;
                    tmp[12 + i] = a3 - a2;
                }
                int outOff = 0;
                for (int i = 0; i < 4; ++i)
                {
                    int dc = tmp[0 + i * 4] + 3;
                    int a0 = dc + tmp[3 + i * 4];
                    int a1 = tmp[1 + i * 4] + tmp[2 + i * 4];
                    int a2 = tmp[1 + i * 4] - tmp[2 + i * 4];
                    int a3 = dc - tmp[3 + i * 4];
                    outArr[outOff + 0] = (short)((a0 + a1) >> 3);
                    outArr[outOff + 16] = (short)((a3 + a2) >> 3);
                    outArr[outOff + 32] = (short)((a0 - a1) >> 3);
                    outArr[outOff + 48] = (short)((a3 - a2) >> 3);
                    outOff += 64;
                }
            }

            private static void TransformOne(short[] coeffs, int offset, byte[] plane, int stride, int r0, int c0)
            {
                int[] tmp = new int[16];
                for (int i = 0; i < 4; ++i)
                {
                    int in0 = coeffs[offset + i];
                    int in4 = coeffs[offset + i + 4];
                    int in8 = coeffs[offset + i + 8];
                    int in12 = coeffs[offset + i + 12];
                    int a = in0 + in8;
                    int b = in0 - in8;
                    int c = Mul2(in4) - Mul1(in12);
                    int d = Mul1(in4) + Mul2(in12);
                    tmp[i * 4 + 0] = a + d;
                    tmp[i * 4 + 1] = b + c;
                    tmp[i * 4 + 2] = b - c;
                    tmp[i * 4 + 3] = a - d;
                }
                for (int i = 0; i < 4; ++i)
                {
                    int t0 = tmp[0 + i];
                    int t4 = tmp[4 + i];
                    int t8 = tmp[8 + i];
                    int t12 = tmp[12 + i];
                    int dc = t0 + 4;
                    int a = dc + t8;
                    int b = dc - t8;
                    int c = Mul2(t4) - Mul1(t12);
                    int d = Mul1(t4) + Mul2(t12);
                    Store(plane, stride, r0 + i, c0 + 0, a + d);
                    Store(plane, stride, r0 + i, c0 + 1, b + c);
                    Store(plane, stride, r0 + i, c0 + 2, b - c);
                    Store(plane, stride, r0 + i, c0 + 3, a - d);
                }
            }

            private static void Store2(byte[] plane, int stride, int r0, int c0, int y, int dc, int d, int c)
            {
                Store(plane, stride, r0 + y, c0 + 0, dc + d);
                Store(plane, stride, r0 + y, c0 + 1, dc + c);
                Store(plane, stride, r0 + y, c0 + 2, dc - c);
                Store(plane, stride, r0 + y, c0 + 3, dc - d);
            }

            private static void TransformAc3(short[] coeffs, int offset, byte[] plane, int stride, int r0, int c0)
            {
                int a = coeffs[offset + 0] + 4;
                int c4 = Mul2(coeffs[offset + 4]);
                int d4 = Mul1(coeffs[offset + 4]);
                int c1 = Mul2(coeffs[offset + 1]);
                int d1 = Mul1(coeffs[offset + 1]);
                Store2(plane, stride, r0, c0, 0, a + d4, d1, c1);
                Store2(plane, stride, r0, c0, 1, a + c4, d1, c1);
                Store2(plane, stride, r0, c0, 2, a - c4, d1, c1);
                Store2(plane, stride, r0, c0, 3, a - d4, d1, c1);
            }

            private static void TransformDc(short[] coeffs, int offset, byte[] plane, int stride, int r0, int c0)
            {
                int dc = coeffs[offset + 0] + 4;
                for (int j = 0; j < 4; ++j)
                    for (int i = 0; i < 4; ++i)
                        Store(plane, stride, r0 + j, c0 + i, dc);
            }

            private static void DoLumaTransform(int code, short[] coeffs, int offset, byte[] plane, int stride, int r0, int c0)
            {
                switch (code)
                {
                    case 3: TransformOne(coeffs, offset, plane, stride, r0, c0); break;
                    case 2: TransformAc3(coeffs, offset, plane, stride, r0, c0); break;
                    case 1: TransformDc(coeffs, offset, plane, stride, r0, c0); break;
                }
            }

            private static void DoUvTransform(int[] codes, short[] coeffs, int baseOff, byte[] plane, int stride, int r0, int c0)
            {
                bool anyNonZero = codes[0] != 0 || codes[1] != 0 || codes[2] != 0 || codes[3] != 0;
                if (!anyNonZero) return;
                bool anyAc = codes[0] >= 2 || codes[1] >= 2 || codes[2] >= 2 || codes[3] >= 2;
                if (anyAc)
                {
                    TransformOne(coeffs, baseOff + 0 * 16, plane, stride, r0, c0);
                    TransformOne(coeffs, baseOff + 1 * 16, plane, stride, r0, c0 + 4);
                    TransformOne(coeffs, baseOff + 2 * 16, plane, stride, r0 + 4, c0);
                    TransformOne(coeffs, baseOff + 3 * 16, plane, stride, r0 + 4, c0 + 4);
                }
                else
                {
                    if (coeffs[baseOff + 0 * 16] != 0) TransformDc(coeffs, baseOff + 0 * 16, plane, stride, r0, c0);
                    if (coeffs[baseOff + 1 * 16] != 0) TransformDc(coeffs, baseOff + 1 * 16, plane, stride, r0, c0 + 4);
                    if (coeffs[baseOff + 2 * 16] != 0) TransformDc(coeffs, baseOff + 2 * 16, plane, stride, r0 + 4, c0);
                    if (coeffs[baseOff + 3 * 16] != 0) TransformDc(coeffs, baseOff + 3 * 16, plane, stride, r0 + 4, c0 + 4);
                }
            }

            // -----------------------------------------------------------
            // Intra prediction (dsp/dec.c)
            // -----------------------------------------------------------
            private static int Avg3(int a, int b, int c) => (a + 2 * b + c + 2) >> 2;
            private static int Avg2(int a, int b) => (a + b + 1) >> 1;

            private static int CheckMode(int mbX, int mbY, int mode)
            {
                if (mode == T.DC_PRED)
                {
                    if (mbX == 0) return (mbY == 0) ? T.B_DC_PRED_NOTOPLEFT : T.B_DC_PRED_NOLEFT;
                    return (mbY == 0) ? T.B_DC_PRED_NOTOP : T.DC_PRED;
                }
                return mode;
            }

            private static void Put16(byte[] plane, int stride, int r0, int c0, int v)
            {
                byte bv = (byte)v;
                for (int j = 0; j < 16; ++j)
                    for (int i = 0; i < 16; ++i)
                        plane[Idx(r0 + j, c0 + i, stride)] = bv;
            }

            private static void Put8(byte[] plane, int stride, int r0, int c0, int v)
            {
                byte bv = (byte)v;
                for (int j = 0; j < 8; ++j)
                    for (int i = 0; i < 8; ++i)
                        plane[Idx(r0 + j, c0 + i, stride)] = bv;
            }

            private static void TrueMotion(byte[] plane, int stride, int r0, int c0, int size)
            {
                int corner = plane[Idx(r0 - 1, c0 - 1, stride)];
                for (int y = 0; y < size; ++y)
                {
                    int left = plane[Idx(r0 + y, c0 - 1, stride)];
                    for (int x = 0; x < size; ++x)
                    {
                        int top = plane[Idx(r0 - 1, c0 + x, stride)];
                        plane[Idx(r0 + y, c0 + x, stride)] = (byte)KClip1(top + left - corner);
                    }
                }
            }

            private static void PredictLuma16(byte[] plane, int stride, int r0, int c0, int mode)
            {
                switch (mode)
                {
                    case T.DC_PRED:
                    {
                        int dc = 16;
                        for (int j = 0; j < 16; ++j) dc += plane[Idx(r0 + j, c0 - 1, stride)] + plane[Idx(r0 - 1, c0 + j, stride)];
                        Put16(plane, stride, r0, c0, dc >> 5);
                        break;
                    }
                    case T.TM_PRED: TrueMotion(plane, stride, r0, c0, 16); break;
                    case T.V_PRED:
                        for (int j = 0; j < 16; ++j)
                            for (int i = 0; i < 16; ++i)
                                plane[Idx(r0 + j, c0 + i, stride)] = plane[Idx(r0 - 1, c0 + i, stride)];
                        break;
                    case T.H_PRED:
                        for (int j = 0; j < 16; ++j)
                        {
                            byte v = plane[Idx(r0 + j, c0 - 1, stride)];
                            for (int i = 0; i < 16; ++i) plane[Idx(r0 + j, c0 + i, stride)] = v;
                        }
                        break;
                    case T.B_DC_PRED_NOTOP:
                    {
                        int dc = 8;
                        for (int j = 0; j < 16; ++j) dc += plane[Idx(r0 + j, c0 - 1, stride)];
                        Put16(plane, stride, r0, c0, dc >> 4);
                        break;
                    }
                    case T.B_DC_PRED_NOLEFT:
                    {
                        int dc = 8;
                        for (int i = 0; i < 16; ++i) dc += plane[Idx(r0 - 1, c0 + i, stride)];
                        Put16(plane, stride, r0, c0, dc >> 4);
                        break;
                    }
                    case T.B_DC_PRED_NOTOPLEFT:
                        Put16(plane, stride, r0, c0, 0x80);
                        break;
                }
            }

            private static void PredictChroma8(byte[] plane, int stride, int r0, int c0, int mode)
            {
                switch (mode)
                {
                    case T.DC_PRED:
                    {
                        int dc = 8;
                        for (int i = 0; i < 8; ++i) dc += plane[Idx(r0 - 1, c0 + i, stride)] + plane[Idx(r0 + i, c0 - 1, stride)];
                        Put8(plane, stride, r0, c0, dc >> 4);
                        break;
                    }
                    case T.TM_PRED: TrueMotion(plane, stride, r0, c0, 8); break;
                    case T.V_PRED:
                        for (int j = 0; j < 8; ++j)
                            for (int i = 0; i < 8; ++i)
                                plane[Idx(r0 + j, c0 + i, stride)] = plane[Idx(r0 - 1, c0 + i, stride)];
                        break;
                    case T.H_PRED:
                        for (int j = 0; j < 8; ++j)
                        {
                            byte v = plane[Idx(r0 + j, c0 - 1, stride)];
                            for (int i = 0; i < 8; ++i) plane[Idx(r0 + j, c0 + i, stride)] = v;
                        }
                        break;
                    case T.B_DC_PRED_NOTOP:
                    {
                        int dc = 4;
                        for (int i = 0; i < 8; ++i) dc += plane[Idx(r0 + i, c0 - 1, stride)];
                        Put8(plane, stride, r0, c0, dc >> 3);
                        break;
                    }
                    case T.B_DC_PRED_NOLEFT:
                    {
                        int dc = 4;
                        for (int i = 0; i < 8; ++i) dc += plane[Idx(r0 - 1, c0 + i, stride)];
                        Put8(plane, stride, r0, c0, dc >> 3);
                        break;
                    }
                    case T.B_DC_PRED_NOTOPLEFT:
                        Put8(plane, stride, r0, c0, 0x80);
                        break;
                }
            }

            // 4x4 intra prediction. top8[0..7] = extended top row (cols 0..7 relative
            // to the block - cols 4..7 come from the MB's true top-right neighbour,
            // NOT the current row, per spec: see topRight handling in ReconstructRow).
            // left4[0..3] = left column, corner = top-left diagonal pixel.
            private static void PredictLuma4(byte[] plane, int stride, int r0, int c0, int mode, int[] top8, int[] left4, int corner)
            {
                switch (mode)
                {
                    case T.B_DC_PRED:
                    {
                        int dc = 4;
                        for (int i = 0; i < 4; ++i) dc += top8[i] + left4[i];
                        dc >>= 3;
                        for (int j = 0; j < 4; ++j) for (int i = 0; i < 4; ++i) plane[Idx(r0 + j, c0 + i, stride)] = (byte)dc;
                        break;
                    }
                    case T.B_TM_PRED:
                        for (int y = 0; y < 4; ++y)
                            for (int x = 0; x < 4; ++x)
                                plane[Idx(r0 + y, c0 + x, stride)] = (byte)KClip1(top8[x] + left4[y] - corner);
                        break;
                    case T.B_VE_PRED:
                    {
                        int v0 = Avg3(corner, top8[0], top8[1]);
                        int v1 = Avg3(top8[0], top8[1], top8[2]);
                        int v2 = Avg3(top8[1], top8[2], top8[3]);
                        int v3 = Avg3(top8[2], top8[3], top8[4]);
                        for (int j = 0; j < 4; ++j)
                        {
                            plane[Idx(r0 + j, c0 + 0, stride)] = (byte)v0;
                            plane[Idx(r0 + j, c0 + 1, stride)] = (byte)v1;
                            plane[Idx(r0 + j, c0 + 2, stride)] = (byte)v2;
                            plane[Idx(r0 + j, c0 + 3, stride)] = (byte)v3;
                        }
                        break;
                    }
                    case T.B_HE_PRED:
                    {
                        int a = corner, b = left4[0], c = left4[1], d = left4[2], e = left4[3];
                        int r0v = Avg3(a, b, c), r1 = Avg3(b, c, d), r2 = Avg3(c, d, e), r3 = Avg3(d, e, e);
                        for (int i = 0; i < 4; ++i) plane[Idx(r0 + 0, c0 + i, stride)] = (byte)r0v;
                        for (int i = 0; i < 4; ++i) plane[Idx(r0 + 1, c0 + i, stride)] = (byte)r1;
                        for (int i = 0; i < 4; ++i) plane[Idx(r0 + 2, c0 + i, stride)] = (byte)r2;
                        for (int i = 0; i < 4; ++i) plane[Idx(r0 + 3, c0 + i, stride)] = (byte)r3;
                        break;
                    }
                    case T.B_RD_PRED:
                    {
                        int i4 = left4[0], j4 = left4[1], k4 = left4[2], l4 = left4[3];
                        int x4 = corner;
                        int a4 = top8[0], b4 = top8[1], c4 = top8[2], d4 = top8[3];
                        void Set(int row, int col, int v) => plane[Idx(r0 + row, c0 + col, stride)] = (byte)v;
                        Set(3, 0, Avg3(j4, k4, l4));
                        int t1 = Avg3(i4, j4, k4); Set(3, 1, t1); Set(2, 0, t1);
                        int t2 = Avg3(x4, i4, j4); Set(3, 2, t2); Set(2, 1, t2); Set(1, 0, t2);
                        int t3 = Avg3(a4, x4, i4); Set(3, 3, t3); Set(2, 2, t3); Set(1, 1, t3); Set(0, 0, t3);
                        int t4 = Avg3(b4, a4, x4); Set(2, 3, t4); Set(1, 2, t4); Set(0, 1, t4);
                        int t5 = Avg3(c4, b4, a4); Set(1, 3, t5); Set(0, 2, t5);
                        Set(0, 3, Avg3(d4, c4, b4));
                        break;
                    }
                    case T.B_VR_PRED:
                    {
                        int i4 = left4[0], j4 = left4[1], k4 = left4[2];
                        int x4 = corner;
                        int a4 = top8[0], b4 = top8[1], c4 = top8[2], d4 = top8[3];
                        void Set(int row, int col, int v) => plane[Idx(r0 + row, c0 + col, stride)] = (byte)v;
                        int v1 = Avg2(x4, a4); Set(0, 0, v1); Set(2, 1, v1);
                        int v2 = Avg2(a4, b4); Set(0, 1, v2); Set(2, 2, v2);
                        int v3 = Avg2(b4, c4); Set(0, 2, v3); Set(2, 3, v3);
                        int v4 = Avg2(c4, d4); Set(0, 3, v4);
                        Set(3, 0, Avg3(k4, j4, i4));
                        Set(2, 0, Avg3(j4, i4, x4));
                        int v7 = Avg3(i4, x4, a4); Set(1, 0, v7); Set(3, 1, v7);
                        int v8 = Avg3(x4, a4, b4); Set(1, 1, v8); Set(3, 2, v8);
                        int v9 = Avg3(a4, b4, c4); Set(1, 2, v9); Set(3, 3, v9);
                        Set(1, 3, Avg3(b4, c4, d4));
                        break;
                    }
                    case T.B_LD_PRED:
                    {
                        int a4 = top8[0], b4 = top8[1], c4 = top8[2], d4 = top8[3];
                        int e4 = top8[4], f4 = top8[5], g4 = top8[6], h4 = top8[7];
                        void Set(int row, int col, int v) => plane[Idx(r0 + row, c0 + col, stride)] = (byte)v;
                        Set(0, 0, Avg3(a4, b4, c4));
                        int v2 = Avg3(b4, c4, d4); Set(0, 1, v2); Set(1, 0, v2);
                        int v3 = Avg3(c4, d4, e4); Set(0, 2, v3); Set(1, 1, v3); Set(2, 0, v3);
                        int v4 = Avg3(d4, e4, f4); Set(0, 3, v4); Set(1, 2, v4); Set(2, 1, v4); Set(3, 0, v4);
                        int v5 = Avg3(e4, f4, g4); Set(1, 3, v5); Set(2, 2, v5); Set(3, 1, v5);
                        int v6 = Avg3(f4, g4, h4); Set(2, 3, v6); Set(3, 2, v6);
                        Set(3, 3, Avg3(g4, h4, h4));
                        break;
                    }
                    case T.B_VL_PRED:
                    {
                        int a4 = top8[0], b4 = top8[1], c4 = top8[2], d4 = top8[3];
                        int e4 = top8[4], f4 = top8[5], g4 = top8[6], h4 = top8[7];
                        void Set(int row, int col, int v) => plane[Idx(r0 + row, c0 + col, stride)] = (byte)v;
                        Set(0, 0, Avg2(a4, b4));
                        int v2 = Avg2(b4, c4); Set(0, 1, v2); Set(2, 0, v2);
                        int v3 = Avg2(c4, d4); Set(0, 2, v3); Set(2, 1, v3);
                        int v4 = Avg2(d4, e4); Set(0, 3, v4); Set(2, 2, v4);
                        Set(1, 0, Avg3(a4, b4, c4));
                        int v6 = Avg3(b4, c4, d4); Set(1, 1, v6); Set(3, 0, v6);
                        int v7 = Avg3(c4, d4, e4); Set(1, 2, v7); Set(3, 1, v7);
                        int v8 = Avg3(d4, e4, f4); Set(1, 3, v8); Set(3, 2, v8);
                        Set(2, 3, Avg3(e4, f4, g4));
                        Set(3, 3, Avg3(f4, g4, h4));
                        break;
                    }
                    case T.B_HD_PRED:
                    {
                        int i4 = left4[0], j4 = left4[1], k4 = left4[2], l4 = left4[3];
                        int x4 = corner;
                        int a4 = top8[0], b4 = top8[1], c4 = top8[2];
                        void Set(int row, int col, int v) => plane[Idx(r0 + row, c0 + col, stride)] = (byte)v;
                        int v1 = Avg2(i4, x4); Set(0, 0, v1); Set(1, 2, v1);
                        int v2 = Avg2(j4, i4); Set(1, 0, v2); Set(2, 2, v2);
                        int v3 = Avg2(k4, j4); Set(2, 0, v3); Set(3, 2, v3);
                        Set(3, 0, Avg2(l4, k4));
                        Set(0, 3, Avg3(a4, b4, c4));
                        Set(0, 2, Avg3(x4, a4, b4));
                        int v7 = Avg3(i4, x4, a4); Set(0, 1, v7); Set(1, 3, v7);
                        int v8 = Avg3(j4, i4, x4); Set(1, 1, v8); Set(2, 3, v8);
                        int v9 = Avg3(k4, j4, i4); Set(2, 1, v9); Set(3, 3, v9);
                        Set(3, 1, Avg3(l4, k4, j4));
                        break;
                    }
                    case T.B_HU_PRED:
                    {
                        int i4 = left4[0], j4 = left4[1], k4 = left4[2], l4 = left4[3];
                        void Set(int row, int col, int v) => plane[Idx(r0 + row, c0 + col, stride)] = (byte)v;
                        Set(0, 0, Avg2(i4, j4));
                        int v2 = Avg2(j4, k4); Set(0, 2, v2); Set(1, 0, v2);
                        int v3 = Avg2(k4, l4); Set(1, 2, v3); Set(2, 0, v3);
                        Set(0, 1, Avg3(i4, j4, k4));
                        int v5 = Avg3(j4, k4, l4); Set(0, 3, v5); Set(1, 1, v5);
                        int v6 = Avg3(k4, l4, l4); Set(1, 3, v6); Set(2, 1, v6);
                        Set(2, 3, l4); Set(2, 2, l4); Set(3, 0, l4); Set(3, 1, l4); Set(3, 2, l4); Set(3, 3, l4);
                        break;
                    }
                }
            }

            // -----------------------------------------------------------
            // Reconstruction (frame_dec.c ReconstructRow, adapted to full-frame arrays)
            // -----------------------------------------------------------
            private void ReconstructRow(int mbY, MbData[] rowData)
            {
                for (int mbX = 0; mbX < _mbW; ++mbX)
                {
                    var block = rowData[mbX];
                    int r0 = mbY * 16, c0 = mbX * 16;

                    if (block.IsI4x4)
                    {
                        int[] topRight = new int[4];
                        if (mbX >= _mbW - 1)
                        {
                            byte v = _y[Idx(r0 - 1, c0 + 15, _yStride)];
                            topRight[0] = topRight[1] = topRight[2] = topRight[3] = v;
                        }
                        else
                        {
                            for (int k = 0; k < 4; ++k) topRight[k] = _y[Idx(r0 - 1, c0 + 16 + k, _yStride)];
                        }

                        int[] top8 = new int[8];
                        int[] left4 = new int[4];
                        for (int n = 0; n < 16; ++n)
                        {
                            int dy = (n / 4) * 4, dx = (n % 4) * 4;
                            int rr = r0 + dy, cc = c0 + dx;
                            for (int k = 0; k < 4; ++k) top8[k] = _y[Idx(rr - 1, cc + k, _yStride)];
                            if (dx == 12)
                            {
                                top8[4] = topRight[0]; top8[5] = topRight[1]; top8[6] = topRight[2]; top8[7] = topRight[3];
                            }
                            else
                            {
                                for (int k = 0; k < 4; ++k) top8[4 + k] = _y[Idx(rr - 1, cc + 4 + k, _yStride)];
                            }
                            for (int k = 0; k < 4; ++k) left4[k] = _y[Idx(rr + k, cc - 1, _yStride)];
                            int corner = _y[Idx(rr - 1, cc - 1, _yStride)];

                            PredictLuma4(_y, _yStride, rr, cc, block.IModes[n], top8, left4, corner);
                            DoLumaTransform(block.BlockCodeY[n], block.Coeffs, n * 16, _y, _yStride, rr, cc);
                        }
                    }
                    else
                    {
                        int predMode = CheckMode(mbX, mbY, block.IModes[0]);
                        PredictLuma16(_y, _yStride, r0, c0, predMode);
                        for (int n = 0; n < 16; ++n)
                        {
                            int dy = (n / 4) * 4, dx = (n % 4) * 4;
                            DoLumaTransform(block.BlockCodeY[n], block.Coeffs, n * 16, _y, _yStride, r0 + dy, c0 + dx);
                        }
                    }

                    int cr0 = mbY * 8, cc0 = mbX * 8;
                    int uvPredMode = CheckMode(mbX, mbY, block.UvMode);
                    PredictChroma8(_u, _uvStride, cr0, cc0, uvPredMode);
                    PredictChroma8(_v, _uvStride, cr0, cc0, uvPredMode);
                    DoUvTransform(block.BlockCodeU, block.Coeffs, 16 * 16, _u, _uvStride, cr0, cc0);
                    DoUvTransform(block.BlockCodeV, block.Coeffs, 16 * 16 + 4 * 16, _v, _uvStride, cr0, cc0);
                }
            }

            // -----------------------------------------------------------
            // Loop filter (frame_dec.c DoFilter / dsp/dec.c filter primitives)
            // -----------------------------------------------------------
            private static void DoFilter2(byte[] plane, int idx, int step)
            {
                int p1 = plane[idx - 2 * step], p0 = plane[idx - step], q0 = plane[idx], q1 = plane[idx + step];
                int a = 3 * (q0 - p0) + SClip1(p1 - q1);
                int a1 = SClip2((a + 4) >> 3);
                int a2 = SClip2((a + 3) >> 3);
                plane[idx - step] = (byte)KClip1(p0 + a2);
                plane[idx] = (byte)KClip1(q0 - a1);
            }

            private static void DoFilter4(byte[] plane, int idx, int step)
            {
                int p1 = plane[idx - 2 * step], p0 = plane[idx - step], q0 = plane[idx], q1 = plane[idx + step];
                int a = 3 * (q0 - p0);
                int a1 = SClip2((a + 4) >> 3);
                int a2 = SClip2((a + 3) >> 3);
                int a3 = (a1 + 1) >> 1;
                plane[idx - 2 * step] = (byte)KClip1(p1 + a3);
                plane[idx - step] = (byte)KClip1(p0 + a2);
                plane[idx] = (byte)KClip1(q0 - a1);
                plane[idx + step] = (byte)KClip1(q1 - a3);
            }

            private static void DoFilter6(byte[] plane, int idx, int step)
            {
                int p2 = plane[idx - 3 * step], p1 = plane[idx - 2 * step], p0 = plane[idx - step];
                int q0 = plane[idx], q1 = plane[idx + step], q2 = plane[idx + 2 * step];
                int a = SClip1(3 * (q0 - p0) + SClip1(p1 - q1));
                int a1 = (27 * a + 63) >> 7;
                int a2 = (18 * a + 63) >> 7;
                int a3 = (9 * a + 63) >> 7;
                plane[idx - 3 * step] = (byte)KClip1(p2 + a3);
                plane[idx - 2 * step] = (byte)KClip1(p1 + a2);
                plane[idx - step] = (byte)KClip1(p0 + a1);
                plane[idx] = (byte)KClip1(q0 - a1);
                plane[idx + step] = (byte)KClip1(q1 - a2);
                plane[idx + 2 * step] = (byte)KClip1(q2 - a3);
            }

            private static bool Hev(byte[] plane, int idx, int step, int thresh)
            {
                int p1 = plane[idx - 2 * step], p0 = plane[idx - step], q0 = plane[idx], q1 = plane[idx + step];
                return KAbs0(p1 - p0) > thresh || KAbs0(q1 - q0) > thresh;
            }

            private static bool NeedsFilter(byte[] plane, int idx, int step, int t)
            {
                int p1 = plane[idx - 2 * step], p0 = plane[idx - step], q0 = plane[idx], q1 = plane[idx + step];
                return (4 * KAbs0(p0 - q0) + KAbs0(p1 - q1)) <= t;
            }

            private static bool NeedsFilter2(byte[] plane, int idx, int step, int t, int it)
            {
                int p3 = plane[idx - 4 * step], p2 = plane[idx - 3 * step], p1 = plane[idx - 2 * step], p0 = plane[idx - step], q0 = plane[idx];
                int q1 = plane[idx + step], q2 = plane[idx + 2 * step], q3 = plane[idx + 3 * step];
                if ((4 * KAbs0(p0 - q0) + KAbs0(p1 - q1)) > t) return false;
                return KAbs0(p3 - p2) <= it && KAbs0(p2 - p1) <= it && KAbs0(p1 - p0) <= it &&
                       KAbs0(q3 - q2) <= it && KAbs0(q2 - q1) <= it && KAbs0(q1 - q0) <= it;
            }

            private static void FilterLoop26(byte[] plane, int idx, int hstride, int vstride, int size, int thresh, int ithresh, int hevThresh)
            {
                int thresh2 = 2 * thresh + 1;
                for (int i = 0; i < size; ++i)
                {
                    if (NeedsFilter2(plane, idx, hstride, thresh2, ithresh))
                    {
                        if (Hev(plane, idx, hstride, hevThresh)) DoFilter2(plane, idx, hstride);
                        else DoFilter6(plane, idx, hstride);
                    }
                    idx += vstride;
                }
            }

            private static void FilterLoop24(byte[] plane, int idx, int hstride, int vstride, int size, int thresh, int ithresh, int hevThresh)
            {
                int thresh2 = 2 * thresh + 1;
                for (int i = 0; i < size; ++i)
                {
                    if (NeedsFilter2(plane, idx, hstride, thresh2, ithresh))
                    {
                        if (Hev(plane, idx, hstride, hevThresh)) DoFilter2(plane, idx, hstride);
                        else DoFilter4(plane, idx, hstride);
                    }
                    idx += vstride;
                }
            }

            private static void SimpleFilterEdge(byte[] plane, int idx, int hstride, int vstride, int size, int thresh)
            {
                int thresh2 = 2 * thresh + 1;
                for (int i = 0; i < size; ++i)
                {
                    if (NeedsFilter(plane, idx, hstride, thresh2)) DoFilter2(plane, idx, hstride);
                    idx += vstride;
                }
            }

            private void FilterMb(int mbX, int mbY)
            {
                var info = _fInfo[mbY, mbX];
                if (info.FLimit == 0) return;
                int limit = info.FLimit;
                bool inner = _fInner[mbY, mbX];
                int r0 = mbY * 16, c0 = mbX * 16;
                int yIdx = Idx(r0, c0, _yStride);

                if (_filterType == 1)
                {
                    if (mbX > 0) SimpleFilterEdge(_y, yIdx, 1, _yStride, 16, limit + 4);
                    if (inner)
                    {
                        for (int k = 1; k <= 3; ++k) SimpleFilterEdge(_y, yIdx + k * 4, 1, _yStride, 16, limit);
                    }
                    if (mbY > 0) SimpleFilterEdge(_y, yIdx, _yStride, 1, 16, limit + 4);
                    if (inner)
                    {
                        for (int k = 1; k <= 3; ++k) SimpleFilterEdge(_y, yIdx + k * 4 * _yStride, _yStride, 1, 16, limit);
                    }
                    return;
                }

                int ilevel = info.FIlevel;
                int hevThresh = info.HevThresh;
                int cr0 = mbY * 8, cc0 = mbX * 8;
                int uIdx = Idx(cr0, cc0, _uvStride);
                int vIdx = Idx(cr0, cc0, _uvStride);

                if (mbX > 0)
                {
                    FilterLoop26(_y, yIdx, 1, _yStride, 16, limit + 4, ilevel, hevThresh);
                    FilterLoop26(_u, uIdx, 1, _uvStride, 8, limit + 4, ilevel, hevThresh);
                    FilterLoop26(_v, vIdx, 1, _uvStride, 8, limit + 4, ilevel, hevThresh);
                }
                if (inner)
                {
                    for (int k = 1; k <= 3; ++k)
                        FilterLoop24(_y, yIdx + k * 4, 1, _yStride, 16, limit, ilevel, hevThresh);
                    FilterLoop24(_u, uIdx + 4, 1, _uvStride, 8, limit, ilevel, hevThresh);
                    FilterLoop24(_v, vIdx + 4, 1, _uvStride, 8, limit, ilevel, hevThresh);
                }
                if (mbY > 0)
                {
                    FilterLoop26(_y, yIdx, _yStride, 1, 16, limit + 4, ilevel, hevThresh);
                    FilterLoop26(_u, uIdx, _uvStride, 1, 8, limit + 4, ilevel, hevThresh);
                    FilterLoop26(_v, vIdx, _uvStride, 1, 8, limit + 4, ilevel, hevThresh);
                }
                if (inner)
                {
                    for (int k = 1; k <= 3; ++k)
                        FilterLoop24(_y, yIdx + k * 4 * _yStride, _yStride, 1, 16, limit, ilevel, hevThresh);
                    FilterLoop24(_u, uIdx + 4 * _uvStride, _uvStride, 1, 8, limit, ilevel, hevThresh);
                    FilterLoop24(_v, vIdx + 4 * _uvStride, _uvStride, 1, 8, limit, ilevel, hevThresh);
                }
            }

            private void ApplyLoopFilter()
            {
                for (int mbY = 0; mbY < _mbH; ++mbY)
                    for (int mbX = 0; mbX < _mbW; ++mbX)
                        FilterMb(mbX, mbY);
            }

            // -----------------------------------------------------------
            // YUV -> RGB with fancy chroma upsampling (dsp/upsampling.c UPSAMPLE_FUNC,
            // dsp/yuv.h VP8YuvToRgb / io_dec.c EmitFancyRGB - collapsed into a single
            // whole-image pass since we don't stream row caches like libwebp does).
            // -----------------------------------------------------------
            private static int MultHi(int v, int coeff) => (v * coeff) >> 8;
            private static int YuvClip8(int v) => ((v & ~16383) == 0) ? (v >> 6) : (v < 0 ? 0 : 255);
            private static int YuvToR(int y, int v) => YuvClip8(MultHi(y, 19077) + MultHi(v, 26149) - 14234);
            private static int YuvToG(int y, int u, int v) => YuvClip8(MultHi(y, 19077) - MultHi(u, 6419) - MultHi(v, 13320) + 8708);
            private static int YuvToB(int y, int u) => YuvClip8(MultHi(y, 19077) + MultHi(u, 33050) - 17685);

            private static void InterpChannelRow(int[] topRow, int[] curRow, int len, bool isTopOutput, int[] outVals)
            {
                int lastPixelPair = (len - 1) >> 1;
                int tl = topRow[0];
                int l = curRow[0];
                outVals[0] = isTopOutput ? ((3 * tl + l + 2) >> 2) : ((3 * l + tl + 2) >> 2);
                for (int x = 1; x <= lastPixelPair; ++x)
                {
                    int t = topRow[x];
                    int cur = curRow[x];
                    int avg = tl + t + l + cur + 8;
                    int diag12 = (avg + 2 * (t + l)) >> 3;
                    int diag03 = (avg + 2 * (tl + cur)) >> 3;
                    int v0, v1;
                    if (isTopOutput) { v0 = (diag12 + tl) >> 1; v1 = (diag03 + t) >> 1; }
                    else { v0 = (diag03 + l) >> 1; v1 = (diag12 + cur) >> 1; }
                    outVals[2 * x - 1] = v0;
                    outVals[2 * x] = v1;
                    tl = t; l = cur;
                }
                if ((len & 1) == 0)
                {
                    outVals[len - 1] = isTopOutput ? ((3 * tl + l + 2) >> 2) : ((3 * l + tl + 2) >> 2);
                }
            }

            public byte[] ToRgb()
            {
                int w = PicWidth, h = PicHeight;
                int uvW = (w + 1) / 2;
                byte[] rgb = new byte[w * h * 3];

                int[] topU = new int[uvW], topV = new int[uvW];
                int[] curU = new int[uvW], curV = new int[uvW];
                int[] uOutT = new int[w], vOutT = new int[w];
                int[] uOutB = new int[w], vOutB = new int[w];

                void ReadUvRow(int uvRow, int[] uArr, int[] vArr)
                {
                    for (int i = 0; i < uvW; ++i)
                    {
                        uArr[i] = _u[Idx(uvRow, i, _uvStride)];
                        vArr[i] = _v[Idx(uvRow, i, _uvStride)];
                    }
                }

                void EmitRgbRow(int yRow, int[] uOut, int[] vOut, int dstRow)
                {
                    for (int x = 0; x < w; ++x)
                    {
                        int yVal = _y[Idx(yRow, x, _yStride)];
                        int uVal = uOut[x], vVal = vOut[x];
                        int off = (dstRow * w + x) * 3;
                        rgb[off + 0] = (byte)YuvToR(yVal, vVal);
                        rgb[off + 1] = (byte)YuvToG(yVal, uVal, vVal);
                        rgb[off + 2] = (byte)YuvToB(yVal, uVal);
                    }
                }

                ReadUvRow(0, curU, curV);
                InterpChannelRow(curU, curU, w, true, uOutT);
                InterpChannelRow(curV, curV, w, true, vOutT);
                EmitRgbRow(0, uOutT, vOutT, 0);

                int y = 0;
                int uvRowCur = 0;
                for (; y + 2 < h; y += 2)
                {
                    Array.Copy(curU, topU, uvW);
                    Array.Copy(curV, topV, uvW);
                    uvRowCur += 1;
                    ReadUvRow(uvRowCur, curU, curV);

                    InterpChannelRow(topU, curU, w, true, uOutT);
                    InterpChannelRow(topV, curV, w, true, vOutT);
                    InterpChannelRow(topU, curU, w, false, uOutB);
                    InterpChannelRow(topV, curV, w, false, vOutB);

                    EmitRgbRow(y + 1, uOutT, vOutT, y + 1);
                    EmitRgbRow(y + 2, uOutB, vOutB, y + 2);
                }

                if ((h & 1) == 0)
                {
                    InterpChannelRow(curU, curU, w, true, uOutT);
                    InterpChannelRow(curV, curV, w, true, vOutT);
                    EmitRgbRow(h - 1, uOutT, vOutT, h - 1);
                }

                return rgb;
            }
        }
    }
}
