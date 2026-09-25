"""Builds synthetic .meshy payloads for tests (no real Meshy content is committed).

The synthetic model mimics what Meshy exports: a quantized (KHR_mesh_quantization)
grid whose vertex buffer is EXT_meshopt_compression-encoded, UVs squeezed into a
1/16 corner with a KHR_texture_transform that scales them back out, and an
EXT_texture_webp base-colour texture.

Usage as a script: python tests/meshy_fixtures.py out.meshy [--broken-uvs]
"""

import json
import math
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "core", "python"))

from meshy_core.decode import encode_meshy_bytes  # noqa: E402

WEBP_FIXTURE = os.path.join(HERE, "fixtures", "gradient_40x24.webp")
GRID = 9  # vertices per side
UV_SCALE = 16.0


def _zigzag8(delta):
    s = delta - 256 if delta >= 128 else delta
    return ((s << 1) & 0xFF) if s >= 0 else ((((~s) << 1) | 1) & 0xFF)


def meshopt_encode_vertices_raw(data, count, size):
    """Minimal EXT_meshopt_compression vertex encoder (v0, every group stored at 8 bits)."""
    assert size % 4 == 0 and len(data) == count * size
    out = bytearray([0xA0])
    block_max = min((8192 // size) & ~15, 256)
    last = bytearray(data[0:size])  # tail vertex = starting predictor
    start = 0
    while start < count:
        block = min(block_max, count - start)
        aligned = (block + 15) & ~15
        for b in range(size):
            plane = bytearray(aligned)
            p = last[b]
            for i in range(block):
                v = data[(start + i) * size + b]
                plane[i] = _zigzag8((v - p) & 0xFF)
                p = v
            groups = aligned // 16
            header = bytearray((groups + 3) // 4)
            for g in range(groups):
                header[g // 4] |= 3 << ((g % 4) * 2)
            out += header
            out += plane
        for b in range(size):
            last[b] = data[(start + block - 1) * size + b]
        start += block
    tail = bytearray(max(size, 32))
    tail[-size:] = data[0:size]
    return bytes(out + tail)


def grid_mesh():
    positions, uvs, indices = [], [], []
    for j in range(GRID):
        for i in range(GRID):
            x, y = i / (GRID - 1.0), j / (GRID - 1.0)
            positions.append((x, y, 0.1 * math.sin(3 * x) * math.cos(2 * y)))
            uvs.append((x, y))
    for j in range(GRID - 1):
        for i in range(GRID - 1):
            a = j * GRID + i
            indices += [a, a + 1, a + GRID, a + 1, a + GRID + 1, a + GRID]
    return positions, uvs, indices


def build_glb(broken_uvs=False, with_texture=True):
    positions, uvs, indices = grid_mesh()
    n = len(positions)
    # Quantized interleaved vertex: POSITION u16x3 (+pad) | TEXCOORD_0 u16x2 normalized => 12 bytes
    qpos = [tuple(int(round(c * 1000)) + 1000 for c in p) for p in positions]
    quv = [tuple(int(round(c / UV_SCALE * 65535)) for c in uv) for uv in uvs]
    if broken_uvs:
        # a hole of identical UVs around one interior vertex plus a wild outlier
        centre = (GRID // 2) * GRID + GRID // 2
        quv[centre] = (0, 0)
        quv[centre + 1] = (0, 0)
        quv[1] = (65535, 65535)
    vbuf = b"".join(struct.pack("<3HxxHH", *(qpos[i] + quv[i])) for i in range(n))
    compressed = meshopt_encode_vertices_raw(vbuf, n, 12)
    ibuf = struct.pack("<%dH" % len(indices), *indices)

    blob = bytearray()

    def add(data):
        while len(blob) % 4:
            blob.append(0)
        off = len(blob)
        blob.extend(data)
        return off

    comp_off = add(compressed)
    idx_off = add(ibuf)
    views = [
        {"buffer": 1, "byteLength": len(vbuf), "byteStride": 12, "target": 34962,
         "extensions": {"EXT_meshopt_compression": {"buffer": 0, "byteOffset": comp_off,
                                                    "byteLength": len(compressed), "byteStride": 12,
                                                    "count": n, "mode": "ATTRIBUTES"}}},
        {"buffer": 0, "byteOffset": idx_off, "byteLength": len(ibuf), "target": 34963},
    ]
    gltf = {
        "asset": {"version": "2.0", "generator": "meshy_fixtures"},
        "extensionsUsed": ["EXT_meshopt_compression", "KHR_mesh_quantization", "KHR_texture_transform"],
        "extensionsRequired": ["EXT_meshopt_compression", "KHR_mesh_quantization"],
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0, "name": "Grid", "scale": [0.001, 0.001, 0.001],
                   "translation": [-1.0, -1.0, -1.0]}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "TEXCOORD_0": 1}, "indices": 2, "material": 0}]}],
        "accessors": [
            {"bufferView": 0, "byteOffset": 0, "componentType": 5123, "count": n, "type": "VEC3",
             "min": [min(p[k] for p in qpos) for k in range(3)], "max": [max(p[k] for p in qpos) for k in range(3)]},
            {"bufferView": 0, "byteOffset": 8, "componentType": 5123, "normalized": True, "count": n, "type": "VEC2"},
            {"bufferView": 1, "componentType": 5123, "count": len(indices), "type": "SCALAR"},
        ],
        "materials": [{"name": "Mat", "pbrMetallicRoughness": {"metallicFactor": 0.0}}],
    }
    tex_ref = {"extensions": {"KHR_texture_transform": {"offset": [0.0, 0.0], "scale": [UV_SCALE, UV_SCALE]}}}
    if with_texture:
        with open(WEBP_FIXTURE, "rb") as f:
            webp = f.read()
        img_off = add(webp)
        views.append({"buffer": 0, "byteOffset": img_off, "byteLength": len(webp)})
        gltf["images"] = [{"bufferView": 2, "mimeType": "image/webp"}]
        gltf["textures"] = [{"extensions": {"EXT_texture_webp": {"source": 0}}}]
        gltf["extensionsUsed"].append("EXT_texture_webp")
        gltf["extensionsRequired"].append("EXT_texture_webp")
        tex_ref["index"] = 0
        gltf["materials"][0]["pbrMetallicRoughness"]["baseColorTexture"] = tex_ref
    while len(blob) % 4:
        blob.append(0)
    gltf["bufferViews"] = views
    gltf["buffers"] = [{"byteLength": len(blob)},
                       {"byteLength": len(vbuf), "extensions": {"EXT_meshopt_compression": {"fallback": True}}}]

    js = json.dumps(gltf).encode("utf-8")
    js += b" " * ((4 - len(js) % 4) % 4)
    # Real payloads are always larger than the 8 KiB encrypted prefix; pad the JSON so
    # the fixture is too (JSON chunks may be padded with spaces).
    js += b" " * max(0, 9000 - len(js) - len(blob))
    js += b" " * ((4 - len(js) % 4) % 4)
    body = struct.pack("<I4s", len(js), b"JSON") + js + struct.pack("<I4s", len(blob), b"BIN\x00") + bytes(blob)
    return b"glTF" + struct.pack("<II", 2, 12 + len(body)) + body


def build_meshy(broken_uvs=False, with_texture=True, nonce=b"\x07" * 12):
    return encode_meshy_bytes(build_glb(broken_uvs, with_texture), nonce)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    with open(sys.argv[1], "wb") as f:
        f.write(build_meshy(broken_uvs="--broken-uvs" in sys.argv))
    print("wrote", sys.argv[1])
