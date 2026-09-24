"""EXT_meshopt_compression bitstream decoder (meshoptimizer, MIT licensed).

Pure-Python port of unity/Editor/MeshyMeshopt.cs. The bitstream decoders follow it
line for line. The filters follow meshoptimizer's scalar reference
(src/vertexfilter.cpp) with every operation rounded to float32, so the output
matches the reference byte for byte. The test-suite checks this against
meshoptimizer's own test vectors (demo/tests.cpp).

Supports vertex codec version 0 and index codec versions 0/1, which is everything
EXT_meshopt_compression can produce.
"""

import math
import struct

__all__ = [
    "decode_vertex_buffer", "decode_index_buffer", "decode_index_sequence",
    "decode_filter_oct", "decode_filter_quat", "decode_filter_exp", "MeshoptError",
]


class MeshoptError(ValueError):
    pass


_BYTE_GROUP_SIZE = 16
_BYTE_GROUP_DECODE_LIMIT = 24
_TAIL_MIN_SIZE_V0 = 32
_BITS_V0 = (0, 2, 4, 8)
_UNZIGZAG8 = bytes((((~(v >> 1)) & 0xFF) if (v & 1) else (v >> 1)) for v in range(256))


# ---------------------------------------------------------------------------
# Mode ATTRIBUTES: vertex buffer
# ---------------------------------------------------------------------------

def decode_vertex_buffer(buf, vertex_count, vertex_size):
    buf = bytes(buf)
    if vertex_size <= 0 or vertex_size > 256 or vertex_size % 4 != 0:
        raise MeshoptError("meshopt: invalid vertex size %d" % vertex_size)
    end = len(buf)
    if end < 1:
        raise MeshoptError("meshopt: empty vertex buffer")
    header = buf[0]
    if header & 0xF0 != 0xA0:
        raise MeshoptError("meshopt: bad vertex header 0x%02x" % header)
    version = header & 0x0F
    if version != 0:
        raise MeshoptError("meshopt: unsupported vertex codec version %d" % version)
    pos = 1
    tail_size = vertex_size
    tail_pad = max(tail_size, _TAIL_MIN_SIZE_V0)
    if end - pos < tail_pad:
        raise MeshoptError("meshopt: vertex buffer truncated (tail)")

    last_vertex = bytearray(buf[end - tail_size:end])
    result = bytearray(vertex_count * vertex_size)
    block_size_max = min((8192 // vertex_size) & ~(_BYTE_GROUP_SIZE - 1), 256)

    vertex_offset = 0
    while vertex_offset < vertex_count:
        block = min(block_size_max, vertex_count - vertex_offset)
        pos = _decode_vertex_block(buf, pos, end, result, vertex_offset * vertex_size, block, vertex_size, last_vertex)
        vertex_offset += block

    if end - pos != tail_pad:
        raise MeshoptError("meshopt: vertex stream did not end exactly at the tail boundary")
    return bytes(result)


def _decode_vertex_block(data, pos, end, out, out_off, count, size, last_vertex):
    aligned = (count + _BYTE_GROUP_SIZE - 1) & ~(_BYTE_GROUP_SIZE - 1)
    planes = []
    for _ in range(size):
        plane = bytearray(aligned)
        pos = _decode_bytes(data, pos, end, plane, aligned)
        planes.append(plane)
    unzig = _UNZIGZAG8
    for b in range(size):
        p = last_vertex[b]
        plane = planes[b]
        o = out_off + b
        for i in range(count):
            p = (unzig[plane[i]] + p) & 0xFF
            out[o] = p
            o += size
        last_vertex[b] = p
    return pos


def _decode_bytes(data, pos, end, buffer, size):
    header_size = (size // _BYTE_GROUP_SIZE + 3) // 4
    if end - pos < header_size:
        raise MeshoptError("meshopt: truncated group header")
    header = pos
    pos += header_size
    for i in range(0, size, _BYTE_GROUP_SIZE):
        if end - pos < _BYTE_GROUP_DECODE_LIMIT:
            raise MeshoptError("meshopt: truncated byte group")
        ho = i // _BYTE_GROUP_SIZE
        bits = _BITS_V0[(data[header + ho // 4] >> ((ho % 4) * 2)) & 3]
        if bits == 0:
            continue  # buffer is already zeroed
        if bits == 8:
            buffer[i:i + 16] = data[pos:pos + 16]
            pos += 16
        elif bits == 2:
            var = pos + 4
            o = i
            for g in range(4):
                b = data[pos + g]
                for s in (6, 4, 2, 0):
                    enc = (b >> s) & 3
                    if enc == 3:
                        buffer[o] = data[var]
                        var += 1
                    else:
                        buffer[o] = enc
                    o += 1
            pos = var
        else:  # 4
            var = pos + 8
            o = i
            for g in range(8):
                b = data[pos + g]
                for s in (4, 0):
                    enc = (b >> s) & 15
                    if enc == 15:
                        buffer[o] = data[var]
                        var += 1
                    else:
                        buffer[o] = enc
                    o += 1
            pos = var
    return pos


# ---------------------------------------------------------------------------
# Mode TRIANGLES: index buffer
# ---------------------------------------------------------------------------

def _decode_vbyte(data, pos):
    lead = data[pos]
    pos += 1
    if lead < 128:
        return lead, pos
    result = lead & 127
    shift = 7
    for _ in range(4):
        group = data[pos]
        pos += 1
        result |= (group & 127) << shift
        shift += 7
        if group < 128:
            break
    return result & 0xFFFFFFFF, pos


def _decode_index(data, pos, last):
    v, pos = _decode_vbyte(data, pos)
    d = (v >> 1) ^ -(v & 1)
    return (last + d) & 0xFFFFFFFF, pos


def decode_index_buffer(buf, index_count):
    buf = bytes(buf)
    if index_count % 3 != 0:
        raise MeshoptError("meshopt: triangle index count must be a multiple of 3")
    if len(buf) < 1 + index_count // 3 + 16:
        raise MeshoptError("meshopt: index buffer too small")
    if buf[0] & 0xF0 != 0xE0:
        raise MeshoptError("meshopt: bad triangle header 0x%02x" % buf[0])
    version = buf[0] & 0x0F
    if version > 1:
        raise MeshoptError("meshopt: unsupported triangle codec version %d" % version)

    edge_a = [0] * 16
    edge_b = [0] * 16
    vfifo = [0] * 16
    eo = 0
    vo = 0
    nxt = 0
    last = 0
    fecmax = 13 if version >= 1 else 15

    code = 1
    code_end = code + index_count // 3
    data = code_end
    safe_end = len(buf) - 16
    aux_table = safe_end
    result = []
    append = result.append

    while code < code_end:
        codetri = buf[code]
        code += 1
        if codetri < 0xF0:
            fe = codetri >> 4
            a = edge_a[(eo - 1 - fe) & 15]
            b = edge_b[(eo - 1 - fe) & 15]
            fec = codetri & 15
            if fec < fecmax:
                cf = vfifo[(vo - 1 - fec) & 15]
                c = nxt if fec == 0 else cf
                fec0 = 1 if fec == 0 else 0
                nxt += fec0
                vfifo[vo] = c
                vo = (vo + fec0) & 15
            else:
                if data > safe_end:
                    raise MeshoptError("meshopt: triangle stream truncated")
                if fec != 15:
                    last = (last + (fec * 2 - 27)) & 0xFFFFFFFF
                    c = last
                else:
                    c, data = _decode_index(buf, data, last)
                    last = c
                vfifo[vo] = c
                vo = (vo + 1) & 15
            edge_a[eo] = c; edge_b[eo] = b; eo = (eo + 1) & 15
            edge_a[eo] = a; edge_b[eo] = c; eo = (eo + 1) & 15
            append(a); append(b); append(c)
        elif codetri < 0xFE:
            codeaux = buf[aux_table + (codetri & 15)]
            feb = codeaux >> 4
            fec = codeaux & 15
            a = nxt
            nxt += 1
            bf = vfifo[(vo - feb) & 15]
            b = nxt if feb == 0 else bf
            feb0 = 1 if feb == 0 else 0
            nxt += feb0
            cf = vfifo[(vo - fec) & 15]
            c = nxt if fec == 0 else cf
            fec0 = 1 if fec == 0 else 0
            nxt += fec0
            append(a); append(b); append(c)
            vfifo[vo] = a; vo = (vo + 1) & 15
            vfifo[vo] = b; vo = (vo + feb0) & 15
            vfifo[vo] = c; vo = (vo + fec0) & 15
            edge_a[eo] = b; edge_b[eo] = a; eo = (eo + 1) & 15
            edge_a[eo] = c; edge_b[eo] = b; eo = (eo + 1) & 15
            edge_a[eo] = a; edge_b[eo] = c; eo = (eo + 1) & 15
        else:
            if data > safe_end:
                raise MeshoptError("meshopt: triangle stream truncated")
            codeaux = buf[data]
            data += 1
            fea = 0 if codetri == 0xFE else 15
            feb = codeaux >> 4
            fec = codeaux & 15
            if codeaux == 0:
                nxt = 0
            if fea == 0:
                a = nxt
                nxt += 1
            else:
                a = 0
            if feb == 0:
                b = nxt
                nxt += 1
            else:
                b = vfifo[(vo - feb) & 15]
            if fec == 0:
                c = nxt
                nxt += 1
            else:
                c = vfifo[(vo - fec) & 15]
            if fea == 15:
                a, data = _decode_index(buf, data, last)
                last = a
            if feb == 15:
                b, data = _decode_index(buf, data, last)
                last = b
            if fec == 15:
                c, data = _decode_index(buf, data, last)
                last = c
            append(a); append(b); append(c)
            vfifo[vo] = a; vo = (vo + 1) & 15
            vfifo[vo] = b; vo = (vo + (1 if feb in (0, 15) else 0)) & 15
            vfifo[vo] = c; vo = (vo + (1 if fec in (0, 15) else 0)) & 15
            edge_a[eo] = b; edge_b[eo] = a; eo = (eo + 1) & 15
            edge_a[eo] = c; edge_b[eo] = b; eo = (eo + 1) & 15
            edge_a[eo] = a; edge_b[eo] = c; eo = (eo + 1) & 15

    if data != safe_end:
        raise MeshoptError("meshopt: triangle stream left unread data")
    return result


# ---------------------------------------------------------------------------
# Mode INDICES: generic index sequence
# ---------------------------------------------------------------------------

def decode_index_sequence(buf, index_count):
    buf = bytes(buf)
    if len(buf) < 1 + index_count + 4:
        raise MeshoptError("meshopt: index sequence buffer too small")
    if buf[0] & 0xF0 != 0xD0:
        raise MeshoptError("meshopt: bad index-sequence header 0x%02x" % buf[0])
    if buf[0] & 0x0F > 1:
        raise MeshoptError("meshopt: unsupported index-sequence codec version %d" % (buf[0] & 0x0F))
    pos = 1
    safe_end = len(buf) - 4
    last = [0, 0]
    result = []
    for _ in range(index_count):
        if pos >= safe_end:
            raise MeshoptError("meshopt: index sequence truncated")
        v, pos = _decode_vbyte(buf, pos)
        current = v & 1
        v >>= 1
        d = (v >> 1) ^ -(v & 1)
        index = (last[current] + d) & 0xFFFFFFFF
        last[current] = index
        result.append(index)
    if pos != safe_end:
        raise MeshoptError("meshopt: index sequence left unread data")
    return result


def indices_to_bytes(indices, stride):
    fmt = "<%d%s" % (len(indices), "H" if stride == 2 else "I")
    if stride == 2:
        return struct.pack(fmt, *[i & 0xFFFF for i in indices])
    return struct.pack(fmt, *indices)


# ---------------------------------------------------------------------------
# Filters (float32-exact scalar reference)
# ---------------------------------------------------------------------------

_F32 = struct.Struct("<f")


def _f(x):
    return _F32.unpack(_F32.pack(x))[0]


def _round_trunc(v):
    # int(v + (v >= 0 ? 0.5f : -0.5f)) evaluated in float32, then C truncation
    return int(_f(v + (0.5 if v >= 0 else -0.5)))


def decode_filter_oct(buf, count, stride):
    """In-place octahedral normal/tangent filter on a bytearray (stride 4 or 8)."""
    if stride not in (4, 8):
        raise MeshoptError("meshopt: octahedral filter needs stride 4 or 8")
    fmt = struct.Struct("<4b" if stride == 4 else "<4h")
    mx = 127.0 if stride == 4 else 32767.0
    for i in range(count):
        off = i * stride
        ix, iy, iz, iw = fmt.unpack_from(buf, off)
        x = float(ix)
        y = float(iy)
        z = _f(_f(float(iz) - abs(x)) - abs(y))
        t = 0.0 if z >= 0.0 else z
        x = _f(x + (t if x >= 0.0 else -t))
        y = _f(y + (t if y >= 0.0 else -t))
        ll = _f(_f(_f(x * x) + _f(y * y)) + _f(z * z))
        s = _f(mx / _f(math.sqrt(ll))) if ll > 0 else 0.0
        xf = _round_trunc(_f(x * s))
        yf = _round_trunc(_f(y * s))
        zf = _round_trunc(_f(z * s))
        if stride == 4:
            fmt.pack_into(buf, off, _wrap(xf, 8), _wrap(yf, 8), _wrap(zf, 8), iw)
        else:
            fmt.pack_into(buf, off, _wrap(xf, 16), _wrap(yf, 16), _wrap(zf, 16), iw)


def decode_filter_quat(buf, count, stride):
    if stride != 8:
        raise MeshoptError("meshopt: quaternion filter needs stride 8")
    fmt = struct.Struct("<4h")
    scale = _f(32767.0 / _f(math.sqrt(2.0)))
    for i in range(count):
        off = i * 8
        d = list(fmt.unpack_from(buf, off))
        sf = d[3] | 3
        s = float(sf)
        x, y, z = float(d[0]), float(d[1]), float(d[2])
        ws = _f(s * s)
        ww = _f(_f(_f(_f(ws * 2.0) - _f(x * x)) - _f(y * y)) - _f(z * z))
        w = _f(math.sqrt(ww if ww >= 0.0 else 0.0))
        ss = _f(scale / s)
        xf = _round_trunc(_f(x * ss))
        yf = _round_trunc(_f(y * ss))
        zf = _round_trunc(_f(z * ss))
        wf = int(_f(_f(w * ss) + 0.5))
        qc = d[3] & 3
        out = [0, 0, 0, 0]
        out[(qc + 1) & 3] = _wrap(xf, 16)
        out[(qc + 2) & 3] = _wrap(yf, 16)
        out[(qc + 3) & 3] = _wrap(zf, 16)
        out[qc & 3] = _wrap(wf, 16)
        fmt.pack_into(buf, off, *out)


def decode_filter_exp(buf, count, stride):
    if stride % 4 != 0:
        raise MeshoptError("meshopt: exponential filter needs a stride divisible by 4")
    n = count * (stride // 4)
    vals = struct.unpack_from("<%dI" % n, buf, 0)
    out = []
    for v in vals:
        m = v & 0xFFFFFF
        if m & 0x800000:
            m -= 1 << 24
        e = v >> 24
        if e & 0x80:
            e -= 256
        pow2 = _F32.unpack(struct.pack("<I", ((e + 127) << 23) & 0xFFFFFFFF))[0]
        out.append(_f(pow2 * float(m)))
    struct.pack_into("<%df" % n, buf, 0, *out)


def _wrap(v, bits):
    m = 1 << bits
    v &= m - 1
    return v - m if v >= (m >> 1) else v
