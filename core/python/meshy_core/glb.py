"""Minimal GLB container + accessor helpers.

GltfDoc keeps the JSON as a dict and the binary data as one bytes object per
bufferView, so bufferViews can be decoded/replaced/added freely and repacked
into a single tightly aligned BIN chunk on save.
"""

import json
import math
import struct

__all__ = ["GltfDoc", "GlbError", "COMPONENT_FORMATS", "TYPE_SIZES"]


class GlbError(ValueError):
    pass


# componentType -> (struct code, byte size, normalization divisor for `normalized: true`)
COMPONENT_FORMATS = {
    5120: ("b", 1, 127.0),
    5121: ("B", 1, 255.0),
    5122: ("h", 2, 32767.0),
    5123: ("H", 2, 65535.0),
    5125: ("I", 4, None),
    5126: ("f", 4, None),
}
TYPE_SIZES = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT2": 4, "MAT3": 9, "MAT4": 16}

FLOAT = 5126
ARRAY_BUFFER = 34962
ELEMENT_ARRAY_BUFFER = 34963


def read_chunks(glb):
    if len(glb) < 12 or glb[:4] != b"glTF":
        raise GlbError("Not a GLB (missing glTF magic).")
    json_text = None
    bin_chunk = None
    offset = 12
    while offset + 8 <= len(glb):
        length, ctype = struct.unpack_from("<I4s", glb, offset)
        start = offset + 8
        if start + length > len(glb):
            break
        if ctype == b"JSON":
            json_text = glb[start:start + length].decode("utf-8")
        elif ctype == b"BIN\x00" and bin_chunk is None:
            bin_chunk = bytes(glb[start:start + length])
        offset = start + length
    if json_text is None:
        raise GlbError("GLB has no JSON chunk.")
    return json.loads(json_text), bin_chunk


class GltfDoc(object):
    def __init__(self, gltf, views):
        self.gltf = gltf
        self.views = views  # list of bytes/bytearray, index = bufferView index

    # ---- load / save -------------------------------------------------------
    @classmethod
    def from_glb(cls, glb, view_decoder=None):
        """view_decoder(doc_json, bv_index, bv_dict, buffers) -> bytes or None (None = raw slice)."""
        gltf, bin_chunk = read_chunks(glb)
        buffers = []
        for i, buf in enumerate(gltf.get("buffers", [])):
            if "uri" in buf:
                raise GlbError("references an external buffer file, which a single .meshy payload should not do")
            if i == 0 and bin_chunk is not None:
                buffers.append(bin_chunk)
            else:
                # EXT_meshopt_compression "fallback" buffers have no data at all.
                buffers.append(None)
        views = []
        for i, bv in enumerate(gltf.get("bufferViews", [])):
            data = view_decoder(gltf, i, bv, buffers) if view_decoder else None
            if data is None:
                src = buffers[bv.get("buffer", 0)] if bv.get("buffer", 0) < len(buffers) else None
                if src is None:
                    raise GlbError("bufferView %d points at a buffer with no data" % i)
                off = bv.get("byteOffset", 0)
                data = src[off:off + bv["byteLength"]]
            views.append(bytes(data))
        return cls(gltf, views)

    def to_glb(self):
        blob = bytearray()
        for i, bv in enumerate(self.gltf.get("bufferViews", [])):
            while len(blob) % 4:
                blob.append(0)
            data = self.views[i]
            bv["buffer"] = 0
            bv["byteOffset"] = len(blob)
            bv["byteLength"] = len(data)
            if "extensions" in bv and not bv["extensions"]:
                del bv["extensions"]
            blob += data
        while len(blob) % 4:
            blob.append(0)
        self.gltf["buffers"] = [{"byteLength": len(blob)}] if blob else []
        js = json.dumps(self.gltf, separators=(",", ":")).encode("utf-8")
        js += b" " * ((4 - len(js) % 4) % 4)
        out = bytearray(b"glTF" + struct.pack("<II", 2, 0))
        out += struct.pack("<I4s", len(js), b"JSON") + js
        if blob:
            out += struct.pack("<I4s", len(blob), b"BIN\x00") + blob
        struct.pack_into("<I", out, 8, len(out))
        return bytes(out)

    # ---- helpers -----------------------------------------------------------
    def add_view(self, data, target=None, stride=None):
        bv = {"buffer": 0, "byteLength": len(data)}
        if target:
            bv["target"] = target
        if stride:
            bv["byteStride"] = stride
        self.gltf.setdefault("bufferViews", []).append(bv)
        self.views.append(bytes(data))
        return len(self.views) - 1

    def add_float_accessor(self, values, type_name, with_bounds=False):
        n = TYPE_SIZES[type_name]
        flat = [c for v in values for c in v] if n > 1 else list(values)
        data = struct.pack("<%df" % len(flat), *flat)
        view = self.add_view(data, ARRAY_BUFFER)
        acc = {"bufferView": view, "componentType": FLOAT, "count": len(values), "type": type_name}
        if with_bounds and values:
            acc["min"] = [min(v[k] for v in values) for k in range(n)]
            acc["max"] = [max(v[k] for v in values) for k in range(n)]
        self.gltf.setdefault("accessors", []).append(acc)
        return len(self.gltf["accessors"]) - 1

    def read_accessor(self, index):
        """Return a list of tuples (or scalars for SCALAR), normalized per the accessor."""
        acc = self.gltf["accessors"][index]
        count = acc["count"]
        n = TYPE_SIZES[acc["type"]]
        code, size, norm = COMPONENT_FORMATS[acc["componentType"]]
        if "bufferView" in acc:
            bv = self.gltf["bufferViews"][acc["bufferView"]]
            data = self.views[acc["bufferView"]]
            stride = bv.get("byteStride") or n * size
            base = acc.get("byteOffset", 0)
            if stride == n * size:
                flat = struct.unpack_from("<%d%s" % (count * n, code), data, base)
            else:
                elem = struct.Struct("<%d%s" % (n, code))
                flat = []
                for i in range(count):
                    flat.extend(elem.unpack_from(data, base + i * stride))
        else:
            flat = [0] * (count * n)
        if "sparse" in acc:
            flat = list(flat)
            sp = acc["sparse"]
            icode, isize, _ = COMPONENT_FORMATS[sp["indices"]["componentType"]]
            idata = self.views[sp["indices"]["bufferView"]]
            ids = struct.unpack_from("<%d%s" % (sp["count"], icode), idata, sp["indices"].get("byteOffset", 0))
            vdata = self.views[sp["values"]["bufferView"]]
            vals = struct.unpack_from("<%d%s" % (sp["count"] * n, code), vdata, sp["values"].get("byteOffset", 0))
            for k, idx in enumerate(ids):
                flat[idx * n:(idx + 1) * n] = vals[k * n:(k + 1) * n]
        if acc.get("normalized") and norm:
            flat = [max(float(v) / norm, -1.0) for v in flat]
        elif code != "f":
            flat = [float(v) for v in flat] if acc["componentType"] != 5125 else list(flat)
        if n == 1:
            return list(flat)
        return [tuple(flat[i * n:(i + 1) * n]) for i in range(count)]

    def remove_extension(self, name):
        for key in ("extensionsUsed", "extensionsRequired"):
            lst = self.gltf.get(key)
            if lst and name in lst:
                lst.remove(name)
                if not lst:
                    del self.gltf[key]

    def uses_extension(self, name):
        return name in self.gltf.get("extensionsUsed", []) or name in self.gltf.get("extensionsRequired", [])


def is_finite2(uv):
    return math.isfinite(uv[0]) and math.isfinite(uv[1])
