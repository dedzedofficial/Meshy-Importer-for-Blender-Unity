"""Lossy WebP (VP8 keyframe) decoder -> RGB24, plus a tiny PNG writer.

Pure-Python port of unity/Editor/MeshyWebpVp8.cs / MeshyWebpVp8.BitReader.cs, which
are themselves faithful ports of libwebp (BSD licensed,
https://github.com/webmproject/libwebp). VP8 decoding is bit-exact by spec
(RFC 6386), so the output must match libwebp's WebPDecodeRGB exactly (fancy
upsampling, no dithering) -- the test-suite checks this against Pillow.

Only what Meshy's textures use is supported: single-keyframe lossy VP8, no alpha
(no VP8L / ALPH). If Pillow is importable it is used instead, because it is far
faster; the pure-Python path exists so the importer never needs a pip install.

Rows are emitted top-down (row 0 = top of the image), like every image codec.
"""

import struct
import zlib

from . import webp_tables as T

__all__ = ["decode_rgb", "decode_rgb_pure", "rgb_to_png", "webp_to_png", "has_pillow"]


def has_pillow():
    try:
        from PIL import Image, features  # noqa: F401
        return bool(features.check("webp"))
    except Exception:
        return False


def decode_rgb(data, prefer_pillow=True):
    """Decode a .webp file's bytes. Returns (width, height, rgb_bytes) or raises ValueError."""
    if prefer_pillow and has_pillow():
        import io
        from PIL import Image
        with Image.open(io.BytesIO(data)) as im:
            im = im.convert("RGB")
            return im.width, im.height, im.tobytes()
    return decode_rgb_pure(data)


def decode_rgb_pure(data):
    off, size = _find_vp8_chunk(data)
    dec = _Decoder()
    dec.parse_and_decode(data, off, size)
    return dec.width, dec.height, dec.to_rgb()


def rgb_to_png(width, height, rgb):
    """Encode RGB24 (top-down) into a PNG file's bytes using only zlib."""
    stride = width * 3
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter type: None
        raw += rgb[y * stride:(y + 1) * stride]

    def chunk(tag, payload):
        c = struct.pack(">I", len(payload)) + tag + payload
        return c + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(bytes(raw), 6)) + chunk(b"IEND", b""))


def webp_to_png(data, prefer_pillow=True):
    w, h, rgb = decode_rgb(data, prefer_pillow)
    return rgb_to_png(w, h, rgb)


def _find_vp8_chunk(data):
    if data is None or len(data) < 20 or data[0:4] != b"RIFF" or data[8:12] != b"WEBP":
        raise ValueError("not a RIFF/WEBP file")
    pos = 12
    while pos + 8 <= len(data):
        tag = data[pos:pos + 4]
        chunk_size = struct.unpack_from("<I", data, pos + 4)[0]
        start = pos + 8
        if tag == b"VP8 ":
            return start, min(chunk_size, len(data) - start)
        if tag == b"VP8L":
            raise ValueError("lossless WebP (VP8L) is not supported by the pure-Python decoder")
        pos += 8 + chunk_size + (chunk_size & 1)
    raise ValueError("no VP8 chunk found")


# ---------------------------------------------------------------------------
# Boolean entropy decoder (libwebp bit_reader, BITS=24)
# ---------------------------------------------------------------------------

_LOG2 = [0] * 256
for _n in range(2, 256):
    _LOG2[_n] = _LOG2[_n >> 1] + 1


class _BitReader(object):
    __slots__ = ("buf", "pos", "end", "max", "value", "range", "bits", "eof")

    def __init__(self, buf, start, size):
        self.buf = buf
        self.pos = start
        self.end = start + size
        self.max = start + size - 4 + 1 if size >= 4 else start
        self.range = 255 - 1
        self.value = 0
        self.bits = -8
        self.eof = False
        self._load()

    def _load(self):
        p = self.pos
        if p < self.max:
            b = self.buf
            self.value = ((b[p] << 16) | (b[p + 1] << 8) | b[p + 2] | (self.value << 24)) & 0xFFFFFFFF
            self.pos = p + 3
            self.bits += 24
        elif p < self.end:
            self.bits += 8
            self.value = (self.buf[p] | (self.value << 8)) & 0xFFFFFFFF
            self.pos = p + 1
        elif not self.eof:
            self.value = (self.value << 8) & 0xFFFFFFFF
            self.bits += 8
            self.eof = True
        else:
            self.bits = 0

    def get_bit(self, prob):
        if self.bits < 0:
            self._load()
        rng = self.range
        pos = self.bits
        split = (rng * prob) >> 8
        if (self.value >> pos) > split:
            rng -= split
            self.value -= (split + 1) << pos
            bit = 1
        else:
            rng = split + 1
            bit = 0
        shift = 7 ^ _LOG2[rng]
        self.bits -= shift
        self.range = (rng << shift) - 1
        return bit

    def get_signed(self, v):
        if self.bits < 0:
            self._load()
        pos = self.bits
        split = self.range >> 1
        if (self.value >> pos) > split:
            self.bits -= 1
            self.range = (self.range - 1) | 1
            self.value -= (split + 1) << pos
            return -v
        self.bits -= 1
        self.range |= 1
        return v

    def get_value(self, bits):
        v = 0
        while bits > 0:
            bits -= 1
            v |= self.get_bit(0x80) << bits
        return v

    def get_signed_value(self, bits):
        value = self.get_value(bits)
        return -value if self.get_bit(0x80) else value


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _clip8(v):
    return v if 0 <= v <= 255 else (0 if v < 0 else 255)


def _sclip1(v):
    return -128 if v < -128 else (127 if v > 127 else v)


def _sclip2(v):
    return -16 if v < -16 else (15 if v > 15 else v)


def _s16(v):
    # libwebp stores coefficients as int16_t; replicate the wrap-around exactly.
    return ((v + 32768) & 0xFFFF) - 32768


def _mul1(a):
    return ((a * 20091) >> 16) + a


def _mul2(a):
    return (a * 35468) >> 16


def _avg3(a, b, c):
    return (a + 2 * b + c + 2) >> 2


def _avg2(a, b):
    return (a + b + 1) >> 1


class _MbData(object):
    __slots__ = ("segment", "is_i4x4", "imodes", "uv_mode", "coeffs", "code_y", "code_u", "code_v", "skip")

    def __init__(self):
        self.segment = 0
        self.is_i4x4 = False
        self.imodes = [0] * 16
        self.uv_mode = 0
        self.coeffs = [0] * 384
        self.code_y = [0] * 16
        self.code_u = [0] * 4
        self.code_v = [0] * 4
        self.skip = False


class _Decoder(object):
    def parse_and_decode(self, data, offset, size):
        if size < 10:
            raise ValueError("VP8 chunk too small")
        p = offset
        bits = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16)
        key_frame = (bits & 1) == 0
        profile = (bits >> 1) & 7
        show = (bits >> 4) & 1
        part_len = bits >> 5
        p += 3
        if not key_frame or profile > 3 or not show:
            raise ValueError("unsupported VP8 frame (not a visible keyframe)")
        if data[p:p + 3] != b"\x9d\x01\x2a":
            raise ValueError("bad VP8 start code")
        self.width = ((data[p + 4] << 8) | data[p + 3]) & 0x3FFF
        self.height = ((data[p + 6] << 8) | data[p + 5]) & 0x3FFF
        p += 7
        if self.width == 0 or self.height == 0:
            raise ValueError("zero-sized VP8 image")
        self.mb_w = (self.width + 15) >> 4
        self.mb_h = (self.height + 15) >> 4
        end = offset + size
        if p + part_len > end:
            raise ValueError("VP8 partition 0 truncated")

        self.br = _BitReader(data, p, part_len)
        after = p + part_len
        self.br.get_bit(0x80)  # colorspace
        self.br.get_bit(0x80)  # clamp type
        self._parse_segment_header()
        self._parse_filter_header()
        self._parse_partitions(data, after, end - after)
        self._parse_quant()
        self.br.get_bit(0x80)  # update_proba (ignored)
        self._parse_proba()
        self._allocate()
        self._precompute_filter_strengths()
        self._decode_all()
        if self.filter_type > 0:
            self._apply_loop_filter()

    # ---- headers -------------------------------------------------------
    def _parse_segment_header(self):
        br = self.br
        self.use_segment = br.get_bit(0x80) != 0
        self.update_map = False
        self.absolute_delta = True
        self.seg_quant = [0] * 4
        self.seg_filter = [0] * 4
        self.segment_probas = [255] * 3
        if self.use_segment:
            self.update_map = br.get_bit(0x80) != 0
            if br.get_bit(0x80):
                self.absolute_delta = br.get_bit(0x80) != 0
                for s in range(4):
                    self.seg_quant[s] = br.get_signed_value(7) if br.get_bit(0x80) else 0
                for s in range(4):
                    self.seg_filter[s] = br.get_signed_value(6) if br.get_bit(0x80) else 0
            if self.update_map:
                for s in range(3):
                    self.segment_probas[s] = br.get_value(8) if br.get_bit(0x80) else 255

    def _parse_filter_header(self):
        br = self.br
        self.f_simple = br.get_bit(0x80) != 0
        self.f_level = br.get_value(6)
        self.f_sharpness = br.get_value(3)
        self.f_use_lf_delta = br.get_bit(0x80) != 0
        self.ref_lf_delta = [0] * 4
        self.mode_lf_delta = [0] * 4
        if self.f_use_lf_delta and br.get_bit(0x80):
            for i in range(4):
                if br.get_bit(0x80):
                    self.ref_lf_delta[i] = br.get_signed_value(6)
            for i in range(4):
                if br.get_bit(0x80):
                    self.mode_lf_delta[i] = br.get_signed_value(6)
        self.filter_type = 0 if self.f_level == 0 else (1 if self.f_simple else 2)

    def _parse_partitions(self, data, start, size):
        self.num_parts_minus_one = (1 << self.br.get_value(2)) - 1
        last = self.num_parts_minus_one
        if size < 3 * last:
            raise ValueError("VP8 partitions truncated")
        parts = []
        sz = start
        part_start = start + last * 3
        size_left = size - last * 3
        for _ in range(last):
            psize = data[sz] | (data[sz + 1] << 8) | (data[sz + 2] << 16)
            if psize > size_left:
                psize = size_left
            parts.append(_BitReader(data, part_start, psize))
            part_start += psize
            size_left -= psize
            sz += 3
        parts.append(_BitReader(data, part_start, size_left))
        self.parts = parts

    def _parse_quant(self):
        br = self.br
        base_q0 = br.get_value(7)

        def opt4():
            return br.get_signed_value(4) if br.get_bit(0x80) else 0

        dqy1_dc = opt4()
        dqy2_dc = opt4()
        dqy2_ac = opt4()
        dquv_dc = opt4()
        dquv_ac = opt4()

        def clip(v, m):
            return 0 if v < 0 else (m if v > m else v)

        self.dqm = [None] * 4
        for i in range(4):
            if self.use_segment:
                q = self.seg_quant[i]
                if not self.absolute_delta:
                    q += base_q0
            else:
                if i > 0:
                    self.dqm[i] = self.dqm[0]
                    continue
                q = base_q0
            y2ac = (T.KAcTable[clip(q + dqy2_ac, 127)] * 101581) >> 16
            if y2ac < 8:
                y2ac = 8
            self.dqm[i] = (
                T.KDcTable[clip(q + dqy1_dc, 127)],       # y1 dc
                T.KAcTable[clip(q, 127)],                 # y1 ac
                T.KDcTable[clip(q + dqy2_dc, 127)] * 2,   # y2 dc
                y2ac,                                     # y2 ac
                T.KDcTable[clip(q + dquv_dc, 117)],       # uv dc
                T.KAcTable[clip(q + dquv_ac, 127)],       # uv ac
            )

    def _parse_proba(self):
        br = self.br
        probas = []
        for t in range(T.NUM_TYPES):
            bands = []
            for b in range(T.NUM_BANDS):
                ctxs = []
                for c in range(T.NUM_CTX):
                    row = []
                    for pp in range(T.NUM_PROBAS):
                        if br.get_bit(T.CoeffsUpdateProba[t][b][c][pp]):
                            row.append(br.get_value(8))
                        else:
                            row.append(T.CoeffsProba0[t][b][c][pp])
                    ctxs.append(row)
                bands.append(ctxs)
            probas.append(bands)
        self.coeff_probas = probas
        self.use_skip_proba = br.get_bit(0x80) != 0
        self.skip_p = br.get_value(8) if self.use_skip_proba else 0

    def _precompute_filter_strengths(self):
        self.fstrengths = [[None, None] for _ in range(4)]
        if self.filter_type <= 0:
            return
        for s in range(4):
            if self.use_segment:
                base = self.seg_filter[s]
                if not self.absolute_delta:
                    base += self.f_level
            else:
                base = self.f_level
            for i4x4 in (0, 1):
                level = base
                if self.f_use_lf_delta:
                    level += self.ref_lf_delta[0]
                    if i4x4:
                        level += self.mode_lf_delta[0]
                level = 0 if level < 0 else (63 if level > 63 else level)
                if level > 0:
                    ilevel = level
                    if self.f_sharpness > 0:
                        ilevel = (ilevel >> 2) if self.f_sharpness > 4 else (ilevel >> 1)
                        if ilevel > 9 - self.f_sharpness:
                            ilevel = 9 - self.f_sharpness
                    if ilevel < 1:
                        ilevel = 1
                    hev = 2 if level >= 40 else (1 if level >= 15 else 0)
                    info = (2 * level + ilevel, ilevel, hev, i4x4 != 0)  # limit, ilevel, hev, inner
                else:
                    info = (0, 0, 0, i4x4 != 0)
                self.fstrengths[s][i4x4] = info

    def _allocate(self):
        pw, ph = self.mb_w * 16, self.mb_h * 16
        self.ys = pw + 1
        self.y = bytearray(self.ys * (ph + 1))
        uw, uh = pw // 2, ph // 2
        self.uvs = uw + 1
        self.u = bytearray(self.uvs * (uh + 1))
        self.v = bytearray(self.uvs * (uh + 1))
        for plane, stride, w, h in ((self.y, self.ys, pw, ph), (self.u, self.uvs, uw, uh), (self.v, self.uvs, uw, uh)):
            for c in range(w + 1):
                plane[c] = 127
            for r in range(h):
                plane[(r + 1) * stride] = 129
        self.top_nz = [0] * self.mb_w
        self.top_nz_dc = [0] * self.mb_w
        self.intra_t = [0] * (4 * self.mb_w)
        self.intra_l = [0] * 4
        self.f_info = [[None] * self.mb_w for _ in range(self.mb_h)]
        self.f_inner = [[False] * self.mb_w for _ in range(self.mb_h)]

    # ---- per-MB loop ---------------------------------------------------
    def _decode_all(self):
        row = [_MbData() for _ in range(self.mb_w)]
        for mb_y in range(self.mb_h):
            self.left_nz = 0
            self.left_nz_dc = 0
            self.intra_l = [0, 0, 0, 0]
            self._parse_intra_mode_row(row)
            token_br = self.parts[mb_y & self.num_parts_minus_one]
            for mb_x in range(self.mb_w):
                self._decode_mb_residuals(token_br, mb_x, mb_y, row[mb_x])
            self._reconstruct_row(mb_y, row)

    def _parse_intra_mode_row(self, row):
        br = self.br
        for mb_x in range(self.mb_w):
            block = row[mb_x]
            top = 4 * mb_x
            if self.update_map:
                sp = self.segment_probas
                block.segment = br.get_bit(sp[1]) if br.get_bit(sp[0]) == 0 else br.get_bit(sp[2]) + 2
            else:
                block.segment = 0
            block.skip = self.use_skip_proba and br.get_bit(self.skip_p) != 0
            block.is_i4x4 = br.get_bit(145) == 0
            if not block.is_i4x4:
                if br.get_bit(156):
                    ymode = T.TM_PRED if br.get_bit(128) else T.H_PRED
                else:
                    ymode = T.V_PRED if br.get_bit(163) else T.DC_PRED
                block.imodes[0] = ymode
                for k in range(4):
                    self.intra_t[top + k] = ymode
                    self.intra_l[k] = ymode
            else:
                for yy in range(4):
                    ymode = self.intra_l[yy]
                    for xx in range(4):
                        ymode = self._read_bmode(self.intra_t[top + xx], ymode)
                        self.intra_t[top + xx] = ymode
                        block.imodes[yy * 4 + xx] = ymode
                    self.intra_l[yy] = ymode
            if br.get_bit(142) == 0:
                block.uv_mode = T.DC_PRED
            elif br.get_bit(114) == 0:
                block.uv_mode = T.V_PRED
            else:
                block.uv_mode = T.TM_PRED if br.get_bit(183) else T.H_PRED

    def _read_bmode(self, top, left):
        br = self.br
        p = T.KBModesProba[top][left]
        if br.get_bit(p[0]) == 0:
            return T.B_DC_PRED
        if br.get_bit(p[1]) == 0:
            return T.B_TM_PRED
        if br.get_bit(p[2]) == 0:
            return T.B_VE_PRED
        if br.get_bit(p[3]) == 0:
            if br.get_bit(p[4]) == 0:
                return T.B_HE_PRED
            return T.B_RD_PRED if br.get_bit(p[5]) == 0 else T.B_VR_PRED
        if br.get_bit(p[6]) == 0:
            return T.B_LD_PRED
        if br.get_bit(p[7]) == 0:
            return T.B_VL_PRED
        return T.B_HD_PRED if br.get_bit(p[8]) == 0 else T.B_HU_PRED

    # ---- residuals -----------------------------------------------------
    @staticmethod
    def _get_large_value(br, p):
        if br.get_bit(p[3]) == 0:
            if br.get_bit(p[4]) == 0:
                return 2
            return 3 + br.get_bit(p[5])
        if br.get_bit(p[6]) == 0:
            if br.get_bit(p[7]) == 0:
                return 5 + br.get_bit(159)
            v = 7 + 2 * br.get_bit(165)
            return v + br.get_bit(145)
        bit1 = br.get_bit(p[8])
        bit0 = br.get_bit(p[10] if bit1 else p[9])
        cat = 2 * bit1 + bit0
        tab = (T.KCat3, T.KCat4, T.KCat5, T.KCat6)[cat]
        v = 0
        for pr in tab:
            v += v + br.get_bit(pr)
        return v + 3 + (8 << cat)

    def _get_coeffs(self, br, typ, ctx, dq_dc, dq_ac, n, out, off):
        probas = self.coeff_probas[typ]
        bands = T.KBands
        zig = T.KZigzag
        p = probas[bands[n]][ctx]
        while n < 16:
            if br.get_bit(p[0]) == 0:
                return n
            while br.get_bit(p[1]) == 0:
                n += 1
                if n == 16:
                    return 16
                p = probas[bands[n]][0]
            if br.get_bit(p[2]) == 0:
                v = 1
                next_ctx = 1
            else:
                v = self._get_large_value(br, p)
                next_ctx = 2
            out[off + zig[n]] = _s16(br.get_signed(v) * (dq_ac if n > 0 else dq_dc))
            n += 1
            if n < 16:
                p = probas[bands[n]][next_ctx]
        return 16

    def _decode_mb_residuals(self, br, mb_x, mb_y, block):
        skip = block.skip
        if not skip:
            skip = self._parse_residuals(br, mb_x, block)
        else:
            self.left_nz = 0
            self.top_nz[mb_x] = 0
            if not block.is_i4x4:
                self.left_nz_dc = 0
                self.top_nz_dc[mb_x] = 0
            block.coeffs = [0] * 384
            block.code_y = [0] * 16
            block.code_u = [0] * 4
            block.code_v = [0] * 4
        if self.filter_type > 0:
            info = self.fstrengths[block.segment][1 if block.is_i4x4 else 0]
            self.f_info[mb_y][mb_x] = info
            self.f_inner[mb_y][mb_x] = info[3] or not skip

    def _parse_residuals(self, br, mb_x, block):
        q = self.dqm[block.segment]
        coeffs = [0] * 384
        block.coeffs = coeffs
        any_nz = False
        if not block.is_i4x4:
            dc = [0] * 16
            ctx = self.top_nz_dc[mb_x] + self.left_nz_dc
            nz_dc = self._get_coeffs(br, 1, ctx, q[2], q[3], 0, dc, 0)
            flag = 1 if nz_dc > 0 else 0
            self.top_nz_dc[mb_x] = flag
            self.left_nz_dc = flag
            if nz_dc > 1:
                _transform_wht(dc, coeffs)
            else:
                dc0 = _s16((dc[0] + 3) >> 3)
                for i in range(0, 256, 16):
                    coeffs[i] = dc0
            first = 1
            luma_type = 0
        else:
            first = 0
            luma_type = 3

        tnz = self.top_nz[mb_x] & 0x0F
        lnz = self.left_nz & 0x0F
        code_y = block.code_y
        for y in range(4):
            l = lnz & 1
            for x in range(4):
                ctx = l + (tnz & 1)
                off = (y * 4 + x) * 16
                nz = self._get_coeffs(br, luma_type, ctx, q[0], q[1], first, coeffs, off)
                l = 1 if nz > first else 0
                tnz = (tnz >> 1) | (l << 3)
                code = 3 if nz > 3 else (2 if nz > 1 else (1 if coeffs[off] != 0 else 0))
                code_y[y * 4 + x] = code
                if code:
                    any_nz = True
            lnz = (lnz >> 1) | (l << 3)
        out_tnz = tnz
        out_lnz = lnz

        for ch in range(2):
            shift = 4 + ch * 2
            tnzc = (self.top_nz[mb_x] >> shift) & 3
            lnzc = (self.left_nz >> shift) & 3
            codes = block.code_u if ch == 0 else block.code_v
            base = 256 + ch * 64
            for y in range(2):
                l = lnzc & 1
                for x in range(2):
                    ctx = l + (tnzc & 1)
                    off = base + (y * 2 + x) * 16
                    nz = self._get_coeffs(br, 2, ctx, q[4], q[5], 0, coeffs, off)
                    l = 1 if nz > 0 else 0
                    tnzc = (tnzc >> 1) | (l << 1)
                    code = 3 if nz > 3 else (2 if nz > 1 else (1 if coeffs[off] != 0 else 0))
                    codes[y * 2 + x] = code
                    if code:
                        any_nz = True
                lnzc = (lnzc >> 1) | (l << 1)
            out_tnz |= (tnzc << 4) << (ch * 2)
            out_lnz |= (lnzc << 4) << (ch * 2)

        self.top_nz[mb_x] = out_tnz
        self.left_nz = out_lnz
        return not any_nz

    # ---- reconstruction ------------------------------------------------
    def _reconstruct_row(self, mb_y, row):
        Y, ys = self.y, self.ys
        for mb_x in range(self.mb_w):
            block = row[mb_x]
            r0, c0 = mb_y * 16, mb_x * 16
            if block.is_i4x4:
                base = r0 * ys + c0 + ys + 1  # Idx(r0, c0)
                if mb_x >= self.mb_w - 1:
                    tr = [Y[base - ys + 15]] * 4
                else:
                    tr = [Y[base - ys + 16 + k] for k in range(4)]
                for n in range(16):
                    dy, dx = (n >> 2) * 4, (n & 3) * 4
                    idx = base + dy * ys + dx
                    top = idx - ys
                    if dx == 12:
                        top8 = list(Y[top:top + 4]) + tr
                    else:
                        top8 = list(Y[top:top + 8])
                    left4 = [Y[idx - 1], Y[idx + ys - 1], Y[idx + 2 * ys - 1], Y[idx + 3 * ys - 1]]
                    corner = Y[top - 1]
                    _predict_luma4(Y, ys, idx, block.imodes[n], top8, left4, corner)
                    _do_transform(block.code_y[n], block.coeffs, n * 16, Y, ys, idx)
            else:
                mode = _check_mode(mb_x, mb_y, block.imodes[0])
                idx0 = (r0 + 1) * ys + c0 + 1
                _predict_block(Y, ys, idx0, 16, mode)
                for n in range(16):
                    dy, dx = (n >> 2) * 4, (n & 3) * 4
                    _do_transform(block.code_y[n], block.coeffs, n * 16, Y, ys, idx0 + dy * ys + dx)

            uvs = self.uvs
            cidx = (mb_y * 8 + 1) * uvs + mb_x * 8 + 1
            uv_mode = _check_mode(mb_x, mb_y, block.uv_mode)
            _predict_block(self.u, uvs, cidx, 8, uv_mode)
            _predict_block(self.v, uvs, cidx, 8, uv_mode)
            _do_uv_transform(block.code_u, block.coeffs, 256, self.u, uvs, cidx)
            _do_uv_transform(block.code_v, block.coeffs, 320, self.v, uvs, cidx)

    # ---- loop filter ---------------------------------------------------
    def _apply_loop_filter(self):
        for mb_y in range(self.mb_h):
            for mb_x in range(self.mb_w):
                self._filter_mb(mb_x, mb_y)

    def _filter_mb(self, mb_x, mb_y):
        limit, ilevel, hev_t, _ = self.f_info[mb_y][mb_x]
        if limit == 0:
            return
        inner = self.f_inner[mb_y][mb_x]
        Y, ys = self.y, self.ys
        yidx = (mb_y * 16 + 1) * ys + mb_x * 16 + 1
        if self.filter_type == 1:
            if mb_x > 0:
                _simple_filter(Y, yidx, 1, ys, 16, limit + 4)
            if inner:
                for k in (1, 2, 3):
                    _simple_filter(Y, yidx + k * 4, 1, ys, 16, limit)
            if mb_y > 0:
                _simple_filter(Y, yidx, ys, 1, 16, limit + 4)
            if inner:
                for k in (1, 2, 3):
                    _simple_filter(Y, yidx + k * 4 * ys, ys, 1, 16, limit)
            return
        U, V, uvs = self.u, self.v, self.uvs
        cidx = (mb_y * 8 + 1) * uvs + mb_x * 8 + 1
        if mb_x > 0:
            _filter_loop(Y, yidx, 1, ys, 16, limit + 4, ilevel, hev_t, True)
            _filter_loop(U, cidx, 1, uvs, 8, limit + 4, ilevel, hev_t, True)
            _filter_loop(V, cidx, 1, uvs, 8, limit + 4, ilevel, hev_t, True)
        if inner:
            for k in (1, 2, 3):
                _filter_loop(Y, yidx + k * 4, 1, ys, 16, limit, ilevel, hev_t, False)
            _filter_loop(U, cidx + 4, 1, uvs, 8, limit, ilevel, hev_t, False)
            _filter_loop(V, cidx + 4, 1, uvs, 8, limit, ilevel, hev_t, False)
        if mb_y > 0:
            _filter_loop(Y, yidx, ys, 1, 16, limit + 4, ilevel, hev_t, True)
            _filter_loop(U, cidx, uvs, 1, 8, limit + 4, ilevel, hev_t, True)
            _filter_loop(V, cidx, uvs, 1, 8, limit + 4, ilevel, hev_t, True)
        if inner:
            for k in (1, 2, 3):
                _filter_loop(Y, yidx + k * 4 * ys, ys, 1, 16, limit, ilevel, hev_t, False)
            _filter_loop(U, cidx + 4 * uvs, uvs, 1, 8, limit, ilevel, hev_t, False)
            _filter_loop(V, cidx + 4 * uvs, uvs, 1, 8, limit, ilevel, hev_t, False)

    # ---- YUV -> RGB (fancy upsampling) --------------------------------
    def to_rgb(self):
        w, h = self.width, self.height
        uvw = (w + 1) // 2
        rgb = bytearray(w * h * 3)
        U, V, uvs = self.u, self.v, self.uvs

        def read_uv(r):
            s = (r + 1) * uvs + 1
            return list(U[s:s + uvw]), list(V[s:s + uvw])

        def emit(y_row, uo, vo):
            Y = self.y
            ys_off = (y_row + 1) * self.ys + 1
            off = y_row * w * 3
            for x in range(w):
                yv = (Y[ys_off + x] * 19077) >> 8
                u = uo[x]
                v = vo[x]
                r = yv + ((v * 26149) >> 8) - 14234
                g = yv - ((u * 6419) >> 8) - ((v * 13320) >> 8) + 8708
                b = yv + ((u * 33050) >> 8) - 17685
                rgb[off] = (r >> 6) if (r & ~16383) == 0 else (0 if r < 0 else 255)
                rgb[off + 1] = (g >> 6) if (g & ~16383) == 0 else (0 if g < 0 else 255)
                rgb[off + 2] = (b >> 6) if (b & ~16383) == 0 else (0 if b < 0 else 255)
                off += 3

        cur_u, cur_v = read_uv(0)
        emit(0, _interp(cur_u, cur_u, w, True), _interp(cur_v, cur_v, w, True))
        y = 0
        uv_row = 0
        while y + 2 < h:
            top_u, top_v = cur_u, cur_v
            uv_row += 1
            cur_u, cur_v = read_uv(uv_row)
            emit(y + 1, _interp(top_u, cur_u, w, True), _interp(top_v, cur_v, w, True))
            emit(y + 2, _interp(top_u, cur_u, w, False), _interp(top_v, cur_v, w, False))
            y += 2
        if (h & 1) == 0:
            emit(h - 1, _interp(cur_u, cur_u, w, True), _interp(cur_v, cur_v, w, True))
        return bytes(rgb)


def _interp(top, cur, length, is_top):
    out = [0] * length
    last_pair = (length - 1) >> 1
    tl = top[0]
    l = cur[0]
    out[0] = ((3 * tl + l + 2) >> 2) if is_top else ((3 * l + tl + 2) >> 2)
    for x in range(1, last_pair + 1):
        t = top[x]
        c = cur[x]
        avg = tl + t + l + c + 8
        diag12 = (avg + 2 * (t + l)) >> 3
        diag03 = (avg + 2 * (tl + c)) >> 3
        if is_top:
            out[2 * x - 1] = (diag12 + tl) >> 1
            out[2 * x] = (diag03 + t) >> 1
        else:
            out[2 * x - 1] = (diag03 + l) >> 1
            out[2 * x] = (diag12 + c) >> 1
        tl = t
        l = c
    if (length & 1) == 0:
        out[length - 1] = ((3 * tl + l + 2) >> 2) if is_top else ((3 * l + tl + 2) >> 2)
    return out


# ---- transforms ----------------------------------------------------------

def _transform_wht(inp, out):
    tmp = [0] * 16
    for i in range(4):
        a0 = inp[i] + inp[12 + i]
        a1 = inp[4 + i] + inp[8 + i]
        a2 = inp[4 + i] - inp[8 + i]
        a3 = inp[i] - inp[12 + i]
        tmp[i] = a0 + a1
        tmp[8 + i] = a0 - a1
        tmp[4 + i] = a3 + a2
        tmp[12 + i] = a3 - a2
    o = 0
    for i in range(4):
        dc = tmp[i * 4] + 3
        a0 = dc + tmp[3 + i * 4]
        a1 = tmp[1 + i * 4] + tmp[2 + i * 4]
        a2 = tmp[1 + i * 4] - tmp[2 + i * 4]
        a3 = dc - tmp[3 + i * 4]
        out[o] = _s16((a0 + a1) >> 3)
        out[o + 16] = _s16((a3 + a2) >> 3)
        out[o + 32] = _s16((a0 - a1) >> 3)
        out[o + 48] = _s16((a3 - a2) >> 3)
        o += 64


def _store(plane, idx, v):
    x = plane[idx] + (v >> 3)
    plane[idx] = x if 0 <= x <= 255 else (0 if x < 0 else 255)


def _transform_one(c, off, plane, stride, idx):
    tmp = [0] * 16
    for i in range(4):
        in0 = c[off + i]
        in4 = c[off + i + 4]
        in8 = c[off + i + 8]
        in12 = c[off + i + 12]
        a = in0 + in8
        b = in0 - in8
        cc = _mul2(in4) - _mul1(in12)
        d = _mul1(in4) + _mul2(in12)
        tmp[i * 4] = a + d
        tmp[i * 4 + 1] = b + cc
        tmp[i * 4 + 2] = b - cc
        tmp[i * 4 + 3] = a - d
    for i in range(4):
        dc = tmp[i] + 4
        t4 = tmp[4 + i]
        t8 = tmp[8 + i]
        t12 = tmp[12 + i]
        a = dc + t8
        b = dc - t8
        cc = _mul2(t4) - _mul1(t12)
        d = _mul1(t4) + _mul2(t12)
        p = idx + i * stride
        _store(plane, p, a + d)
        _store(plane, p + 1, b + cc)
        _store(plane, p + 2, b - cc)
        _store(plane, p + 3, a - d)


def _transform_ac3(c, off, plane, stride, idx):
    a = c[off] + 4
    c4 = _mul2(c[off + 4])
    d4 = _mul1(c[off + 4])
    c1 = _mul2(c[off + 1])
    d1 = _mul1(c[off + 1])
    for row, dc in enumerate((a + d4, a + c4, a - c4, a - d4)):
        p = idx + row * stride
        _store(plane, p, dc + d1)
        _store(plane, p + 1, dc + c1)
        _store(plane, p + 2, dc - c1)
        _store(plane, p + 3, dc - d1)


def _transform_dc(c, off, plane, stride, idx):
    dc = c[off] + 4
    for j in range(4):
        p = idx + j * stride
        for i in range(4):
            _store(plane, p + i, dc)


def _do_transform(code, c, off, plane, stride, idx):
    if code == 3:
        _transform_one(c, off, plane, stride, idx)
    elif code == 2:
        _transform_ac3(c, off, plane, stride, idx)
    elif code == 1:
        _transform_dc(c, off, plane, stride, idx)


def _do_uv_transform(codes, c, base, plane, stride, idx):
    if not (codes[0] or codes[1] or codes[2] or codes[3]):
        return
    targets = (idx, idx + 4, idx + 4 * stride, idx + 4 * stride + 4)
    if codes[0] >= 2 or codes[1] >= 2 or codes[2] >= 2 or codes[3] >= 2:
        for k in range(4):
            _transform_one(c, base + k * 16, plane, stride, targets[k])
    else:
        for k in range(4):
            if c[base + k * 16] != 0:
                _transform_dc(c, base + k * 16, plane, stride, targets[k])


# ---- intra prediction ----------------------------------------------------

def _check_mode(mb_x, mb_y, mode):
    if mode == T.DC_PRED:
        if mb_x == 0:
            return T.B_DC_PRED_NOTOPLEFT if mb_y == 0 else T.B_DC_PRED_NOLEFT
        return T.B_DC_PRED_NOTOP if mb_y == 0 else T.DC_PRED
    return mode


def _fill(plane, stride, idx, size, v):
    row = bytes([v]) * size
    for j in range(size):
        p = idx + j * stride
        plane[p:p + size] = row


def _predict_block(plane, stride, idx, size, mode):
    """16x16 luma or 8x8 chroma prediction; idx points at the block's top-left pixel."""
    top = idx - stride
    shift = 5 if size == 16 else 4
    if mode == T.DC_PRED:
        dc = size
        for j in range(size):
            dc += plane[idx + j * stride - 1] + plane[top + j]
        _fill(plane, stride, idx, size, dc >> shift)
    elif mode == T.TM_PRED:
        corner = plane[top - 1]
        toprow = plane[top:top + size]
        for y in range(size):
            left = plane[idx + y * stride - 1] - corner
            p = idx + y * stride
            plane[p:p + size] = bytes(_clip8(t + left) for t in toprow)
    elif mode == T.V_PRED:
        toprow = bytes(plane[top:top + size])
        for j in range(size):
            p = idx + j * stride
            plane[p:p + size] = toprow
    elif mode == T.H_PRED:
        for j in range(size):
            p = idx + j * stride
            plane[p:p + size] = bytes([plane[p - 1]]) * size
    elif mode == T.B_DC_PRED_NOTOP:
        dc = size >> 1
        for j in range(size):
            dc += plane[idx + j * stride - 1]
        _fill(plane, stride, idx, size, dc >> (shift - 1))
    elif mode == T.B_DC_PRED_NOLEFT:
        dc = size >> 1
        for i in range(size):
            dc += plane[top + i]
        _fill(plane, stride, idx, size, dc >> (shift - 1))
    elif mode == T.B_DC_PRED_NOTOPLEFT:
        _fill(plane, stride, idx, size, 0x80)


def _predict_luma4(plane, stride, idx, mode, top8, left4, corner):
    def put(r, c, v):
        plane[idx + r * stride + c] = v

    if mode == T.B_DC_PRED:
        dc = 4
        for i in range(4):
            dc += top8[i] + left4[i]
        _fill(plane, stride, idx, 4, dc >> 3)
    elif mode == T.B_TM_PRED:
        for y in range(4):
            for x in range(4):
                put(y, x, _clip8(top8[x] + left4[y] - corner))
    elif mode == T.B_VE_PRED:
        vals = bytes((_avg3(corner, top8[0], top8[1]), _avg3(top8[0], top8[1], top8[2]),
                      _avg3(top8[1], top8[2], top8[3]), _avg3(top8[2], top8[3], top8[4])))
        for j in range(4):
            p = idx + j * stride
            plane[p:p + 4] = vals
    elif mode == T.B_HE_PRED:
        a, b, c, d, e = corner, left4[0], left4[1], left4[2], left4[3]
        for j, v in enumerate((_avg3(a, b, c), _avg3(b, c, d), _avg3(c, d, e), _avg3(d, e, e))):
            p = idx + j * stride
            plane[p:p + 4] = bytes((v, v, v, v))
    elif mode == T.B_RD_PRED:
        i4, j4, k4, l4 = left4
        x4 = corner
        a4, b4, c4, d4 = top8[0], top8[1], top8[2], top8[3]
        put(3, 0, _avg3(j4, k4, l4))
        t = _avg3(i4, j4, k4); put(3, 1, t); put(2, 0, t)
        t = _avg3(x4, i4, j4); put(3, 2, t); put(2, 1, t); put(1, 0, t)
        t = _avg3(a4, x4, i4); put(3, 3, t); put(2, 2, t); put(1, 1, t); put(0, 0, t)
        t = _avg3(b4, a4, x4); put(2, 3, t); put(1, 2, t); put(0, 1, t)
        t = _avg3(c4, b4, a4); put(1, 3, t); put(0, 2, t)
        put(0, 3, _avg3(d4, c4, b4))
    elif mode == T.B_VR_PRED:
        i4, j4, k4 = left4[0], left4[1], left4[2]
        x4 = corner
        a4, b4, c4, d4 = top8[0], top8[1], top8[2], top8[3]
        t = _avg2(x4, a4); put(0, 0, t); put(2, 1, t)
        t = _avg2(a4, b4); put(0, 1, t); put(2, 2, t)
        t = _avg2(b4, c4); put(0, 2, t); put(2, 3, t)
        put(0, 3, _avg2(c4, d4))
        put(3, 0, _avg3(k4, j4, i4))
        put(2, 0, _avg3(j4, i4, x4))
        t = _avg3(i4, x4, a4); put(1, 0, t); put(3, 1, t)
        t = _avg3(x4, a4, b4); put(1, 1, t); put(3, 2, t)
        t = _avg3(a4, b4, c4); put(1, 2, t); put(3, 3, t)
        put(1, 3, _avg3(b4, c4, d4))
    elif mode == T.B_LD_PRED:
        a4, b4, c4, d4, e4, f4, g4, h4 = top8
        put(0, 0, _avg3(a4, b4, c4))
        t = _avg3(b4, c4, d4); put(0, 1, t); put(1, 0, t)
        t = _avg3(c4, d4, e4); put(0, 2, t); put(1, 1, t); put(2, 0, t)
        t = _avg3(d4, e4, f4); put(0, 3, t); put(1, 2, t); put(2, 1, t); put(3, 0, t)
        t = _avg3(e4, f4, g4); put(1, 3, t); put(2, 2, t); put(3, 1, t)
        t = _avg3(f4, g4, h4); put(2, 3, t); put(3, 2, t)
        put(3, 3, _avg3(g4, h4, h4))
    elif mode == T.B_VL_PRED:
        a4, b4, c4, d4, e4, f4, g4, h4 = top8
        put(0, 0, _avg2(a4, b4))
        t = _avg2(b4, c4); put(0, 1, t); put(2, 0, t)
        t = _avg2(c4, d4); put(0, 2, t); put(2, 1, t)
        t = _avg2(d4, e4); put(0, 3, t); put(2, 2, t)
        put(1, 0, _avg3(a4, b4, c4))
        t = _avg3(b4, c4, d4); put(1, 1, t); put(3, 0, t)
        t = _avg3(c4, d4, e4); put(1, 2, t); put(3, 1, t)
        t = _avg3(d4, e4, f4); put(1, 3, t); put(3, 2, t)
        put(2, 3, _avg3(e4, f4, g4))
        put(3, 3, _avg3(f4, g4, h4))
    elif mode == T.B_HD_PRED:
        i4, j4, k4, l4 = left4
        x4 = corner
        a4, b4, c4 = top8[0], top8[1], top8[2]
        t = _avg2(i4, x4); put(0, 0, t); put(1, 2, t)
        t = _avg2(j4, i4); put(1, 0, t); put(2, 2, t)
        t = _avg2(k4, j4); put(2, 0, t); put(3, 2, t)
        put(3, 0, _avg2(l4, k4))
        put(0, 3, _avg3(a4, b4, c4))
        put(0, 2, _avg3(x4, a4, b4))
        t = _avg3(i4, x4, a4); put(0, 1, t); put(1, 3, t)
        t = _avg3(j4, i4, x4); put(1, 1, t); put(2, 3, t)
        t = _avg3(k4, j4, i4); put(2, 1, t); put(3, 3, t)
        put(3, 1, _avg3(l4, k4, j4))
    elif mode == T.B_HU_PRED:
        i4, j4, k4, l4 = left4
        put(0, 0, _avg2(i4, j4))
        t = _avg2(j4, k4); put(0, 2, t); put(1, 0, t)
        t = _avg2(k4, l4); put(1, 2, t); put(2, 0, t)
        put(0, 1, _avg3(i4, j4, k4))
        t = _avg3(j4, k4, l4); put(0, 3, t); put(1, 1, t)
        t = _avg3(k4, l4, l4); put(1, 3, t); put(2, 1, t)
        put(2, 3, l4); put(2, 2, l4)
        put(3, 0, l4); put(3, 1, l4); put(3, 2, l4); put(3, 3, l4)


# ---- loop filter primitives ----------------------------------------------

def _simple_filter(plane, idx, hs, vs, size, thresh):
    t2 = 2 * thresh + 1
    for _ in range(size):
        p1 = plane[idx - 2 * hs]
        p0 = plane[idx - hs]
        q0 = plane[idx]
        q1 = plane[idx + hs]
        if 4 * abs(p0 - q0) + abs(p1 - q1) <= t2:
            a = 3 * (q0 - p0) + _sclip1(p1 - q1)
            a1 = _sclip2((a + 4) >> 3)
            a2 = _sclip2((a + 3) >> 3)
            plane[idx - hs] = _clip8(p0 + a2)
            plane[idx] = _clip8(q0 - a1)
        idx += vs


def _filter_loop(plane, idx, hs, vs, size, thresh, ithresh, hev_thresh, edge):
    """FilterLoop26 (macroblock edge, edge=True) / FilterLoop24 (inner edge)."""
    t2 = 2 * thresh + 1
    for _ in range(size):
        p3 = plane[idx - 4 * hs]
        p2 = plane[idx - 3 * hs]
        p1 = plane[idx - 2 * hs]
        p0 = plane[idx - hs]
        q0 = plane[idx]
        q1 = plane[idx + hs]
        q2 = plane[idx + 2 * hs]
        q3 = plane[idx + 3 * hs]
        if (4 * abs(p0 - q0) + abs(p1 - q1) <= t2
                and abs(p3 - p2) <= ithresh and abs(p2 - p1) <= ithresh and abs(p1 - p0) <= ithresh
                and abs(q3 - q2) <= ithresh and abs(q2 - q1) <= ithresh and abs(q1 - q0) <= ithresh):
            if abs(p1 - p0) > hev_thresh or abs(q1 - q0) > hev_thresh:
                a = 3 * (q0 - p0) + _sclip1(p1 - q1)
                a1 = _sclip2((a + 4) >> 3)
                a2 = _sclip2((a + 3) >> 3)
                plane[idx - hs] = _clip8(p0 + a2)
                plane[idx] = _clip8(q0 - a1)
            elif edge:
                a = _sclip1(3 * (q0 - p0) + _sclip1(p1 - q1))
                a1 = (27 * a + 63) >> 7
                a2 = (18 * a + 63) >> 7
                a3 = (9 * a + 63) >> 7
                plane[idx - 3 * hs] = _clip8(p2 + a3)
                plane[idx - 2 * hs] = _clip8(p1 + a2)
                plane[idx - hs] = _clip8(p0 + a1)
                plane[idx] = _clip8(q0 - a1)
                plane[idx + hs] = _clip8(q1 - a2)
                plane[idx + 2 * hs] = _clip8(q2 - a3)
            else:
                a = 3 * (q0 - p0)
                a1 = _sclip2((a + 4) >> 3)
                a2 = _sclip2((a + 3) >> 3)
                a3 = (a1 + 1) >> 1
                plane[idx - 2 * hs] = _clip8(p1 + a3)
                plane[idx - hs] = _clip8(p0 + a2)
                plane[idx] = _clip8(q0 - a1)
                plane[idx + hs] = _clip8(q1 - a3)
        idx += vs
