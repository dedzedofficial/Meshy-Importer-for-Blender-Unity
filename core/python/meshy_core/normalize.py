"""Turn a .meshy payload (or its GLB) into a plain glTF 2.0 GLB any importer can read.

Steps, each optional except the first two:
  1. decrypt the .meshy container (decode.py);
  2. decode EXT_meshopt_compression bufferViews and drop the extension;
  3. dequantize: rewrite KHR_mesh_quantization attributes as FLOAT accessors;
  4. bake KHR_texture_transform into TEXCOORD_n and drop the extension;
  5. repair broken UVs (uv_repair.py);
  6. transcode EXT_texture_webp images to PNG;
  7. scale the whole model (a new root node, so skinning and animation are untouched).

Blender needs 1-2 (its glTF importer rejects meshopt); Unreal needs all of them.
"""

import math

from . import meshopt
from .decode import decode_meshy_bytes
from .glb import GltfDoc, FLOAT
from .uv_repair import repair_uvs
from .webp_vp8 import webp_to_png

__all__ = ["NormalizeOptions", "normalize_glb", "normalize_meshy_bytes", "normalize_meshy_file"]

_TRANSFORM_SLOTS = (
    ("pbrMetallicRoughness", "baseColorTexture"),
    ("pbrMetallicRoughness", "metallicRoughnessTexture"),
    (None, "normalTexture"),
    (None, "emissiveTexture"),
    (None, "occlusionTexture"),
)


class NormalizeOptions(object):
    def __init__(self, decode_meshopt=True, dequantize=False, bake_texture_transform=False,
                 repair_uvs=False, webp_to_png=False, prefer_pillow=True, log=None, scale=1.0):
        self.decode_meshopt = decode_meshopt
        self.dequantize = dequantize
        self.bake_texture_transform = bake_texture_transform
        self.repair_uvs = repair_uvs
        self.webp_to_png = webp_to_png
        self.prefer_pillow = prefer_pillow
        self.log = log or (lambda msg: None)
        self.scale = float(scale)

    @classmethod
    def for_host(cls, host, **kw):
        """Presets: 'blender' (meshopt only) or 'full' (everything, e.g. Unreal/CLI)."""
        if host == "blender":
            return cls(**kw)
        base = dict(decode_meshopt=True, dequantize=True, bake_texture_transform=True,
                    repair_uvs=True, webp_to_png=True)
        base.update(kw)
        return cls(**base)


class NormalizeReport(object):
    def __init__(self):
        self.meshopt_views = 0
        self.dequantized_accessors = 0
        self.baked_uv_sets = 0
        self.png_images = 0
        self.uv = []  # one stats dict per repaired primitive

    def summary(self):
        bad = sum(s["bad_vertices"] for s in self.uv)
        regen = sum(1 for s in self.uv if s["regenerated"])
        return ("meshopt views decoded: %d, accessors dequantized: %d, UV sets baked: %d, "
                "WebP->PNG: %d, UV: %d bad vertices across %d primitive(s), %d regenerated"
                % (self.meshopt_views, self.dequantized_accessors, self.baked_uv_sets,
                   self.png_images, bad, len(self.uv), regen))


# ---------------------------------------------------------------------------

def _meshopt_view_decoder(report):
    def decode(gltf, index, bv, buffers):
        ext = (bv.get("extensions") or {}).get("EXT_meshopt_compression")
        if ext is None:
            return None
        src = buffers[ext.get("buffer", 0)]
        if src is None:
            raise ValueError("EXT_meshopt_compression source buffer has no data")
        off = ext.get("byteOffset", 0)
        data = src[off:off + ext["byteLength"]]
        count = ext["count"]
        stride = ext["byteStride"]
        mode = ext.get("mode", "ATTRIBUTES")
        filt = ext.get("filter", "NONE")
        if mode == "ATTRIBUTES":
            out = bytearray(meshopt.decode_vertex_buffer(data, count, stride))
            if filt == "OCTAHEDRAL":
                meshopt.decode_filter_oct(out, count, stride)
            elif filt == "QUATERNION":
                meshopt.decode_filter_quat(out, count, stride)
            elif filt == "EXPONENTIAL":
                meshopt.decode_filter_exp(out, count, stride)
        elif mode == "TRIANGLES":
            out = meshopt.indices_to_bytes(meshopt.decode_index_buffer(data, count), stride)
        else:
            out = meshopt.indices_to_bytes(meshopt.decode_index_sequence(data, count), stride)
        del bv["extensions"]["EXT_meshopt_compression"]
        if stride and mode == "ATTRIBUTES":
            bv["byteStride"] = stride
        report.meshopt_views += 1
        return bytes(out)
    return decode


def _primitives(doc):
    for mesh in doc.gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            yield prim


def _dequantize(doc, report):
    done = {}
    for prim in _primitives(doc):
        attrs = prim.get("attributes", {})
        for name in list(attrs):
            if not (name in ("POSITION", "NORMAL", "TANGENT") or name.startswith("TEXCOORD_")):
                continue
            idx = attrs[name]
            acc = doc.gltf["accessors"][idx]
            if acc["componentType"] == FLOAT:
                continue
            if idx not in done:
                done[idx] = doc.add_float_accessor(doc.read_accessor(idx), acc["type"], with_bounds=(name == "POSITION"))
                report.dequantized_accessors += 1
            attrs[name] = done[idx]
        for target in prim.get("targets", []):
            for name in list(target):
                acc = doc.gltf["accessors"][target[name]]
                if acc["componentType"] != FLOAT:
                    target[name] = doc.add_float_accessor(doc.read_accessor(target[name]), acc["type"],
                                                          with_bounds=(name == "POSITION"))
                    report.dequantized_accessors += 1
    doc.remove_extension("KHR_mesh_quantization")


def _texture_transform(material, tex_coord):
    for parent, slot in _TRANSFORM_SLOTS:
        holder = material.get(parent, {}) if parent else material
        ref = holder.get(slot)
        if not ref or ref.get("texCoord", 0) != tex_coord:
            continue
        tt = (ref.get("extensions") or {}).get("KHR_texture_transform")
        if tt:
            return tt
    return None


def _apply_transform(uv, tt):
    ox, oy = tt.get("offset", [0.0, 0.0])
    sx, sy = tt.get("scale", [1.0, 1.0])
    r = tt.get("rotation", 0.0)
    u, v = uv[0] * sx, uv[1] * sy
    if r:
        c, s = math.cos(r), math.sin(r)
        u, v = c * u + s * v, -s * u + c * v
    return (u + ox, v + oy)


def _strip_transforms(material):
    for parent, slot in _TRANSFORM_SLOTS:
        holder = material.get(parent, {}) if parent else material
        ref = holder.get(slot)
        if ref and "extensions" in ref:
            tt = ref["extensions"].pop("KHR_texture_transform", None)
            if tt and "texCoord" in tt:
                ref["texCoord"] = tt["texCoord"]
            if not ref["extensions"]:
                del ref["extensions"]


def _bake_texture_transform(doc, report):
    materials = doc.gltf.get("materials", [])
    cache = {}
    for prim in _primitives(doc):
        if "material" not in prim:
            continue
        mat = materials[prim["material"]]
        attrs = prim.get("attributes", {})
        for set_index in (0, 1):
            name = "TEXCOORD_%d" % set_index
            if name not in attrs:
                continue
            tt = _texture_transform(mat, set_index)
            if not tt:
                continue
            key = (attrs[name], repr(sorted(tt.items())))
            if key not in cache:
                uvs = doc.read_accessor(attrs[name])
                cache[key] = doc.add_float_accessor([_apply_transform(uv, tt) for uv in uvs], "VEC2")
                report.baked_uv_sets += 1
            attrs[name] = cache[key]
    for mat in materials:
        _strip_transforms(mat)
    doc.remove_extension("KHR_texture_transform")


def _read_indices(doc, prim, count):
    if "indices" in prim:
        return [int(i) for i in doc.read_accessor(prim["indices"])]
    mode = prim.get("mode", 4)
    if mode != 4:
        return None
    return list(range(count))


def _repair(doc, report, log):
    done = set()
    for prim in _primitives(doc):
        if prim.get("mode", 4) != 4:
            continue
        attrs = prim.get("attributes", {})
        if "POSITION" not in attrs:
            continue
        key = (attrs["POSITION"], attrs.get("TEXCOORD_0"), prim.get("indices"))
        if key in done:
            continue
        done.add(key)
        positions = doc.read_accessor(attrs["POSITION"])
        indices = _read_indices(doc, prim, len(positions))
        if indices is None:
            continue
        uvs = doc.read_accessor(attrs["TEXCOORD_0"]) if "TEXCOORD_0" in attrs else None
        normals = doc.read_accessor(attrs["NORMAL"]) if "NORMAL" in attrs else None
        new_uvs, stats = repair_uvs(positions, uvs, indices, normals)
        report.uv.append(stats)
        if new_uvs is not None:
            attrs["TEXCOORD_0"] = doc.add_float_accessor(new_uvs, "VEC2")
            log("UV repair: %d bad vertices, %d repaired, %d projected%s"
                % (stats["bad_vertices"], stats["repaired_vertices"], stats["projected_vertices"],
                   " (regenerated)" if stats["regenerated"] else ""))


def _webp_to_png(doc, report, prefer_pillow):
    images = doc.gltf.get("images", [])
    converted = {}
    for tex in doc.gltf.get("textures", []):
        ext = (tex.get("extensions") or {}).pop("EXT_texture_webp", None)
        if tex.get("extensions") == {}:
            del tex["extensions"]
        if ext is None:
            continue
        if "source" in tex:
            continue  # the file already carries a PNG/JPEG fallback
        src = ext["source"]
        if src not in converted:
            img = images[src]
            if "bufferView" not in img:
                raise ValueError("WebP image %d has no embedded data" % src)
            # Replace the image in place so no importer ever sees a WebP image.
            img["bufferView"] = doc.add_view(webp_to_png(doc.views[img["bufferView"]], prefer_pillow))
            img["mimeType"] = "image/png"
            converted[src] = True
            report.png_images += 1
        tex["source"] = src
    doc.remove_extension("EXT_texture_webp")


def normalize_glb(glb, options=None):
    """Return (plain_glb_bytes, NormalizeReport)."""
    options = options or NormalizeOptions()
    report = NormalizeReport()
    decoder = _meshopt_view_decoder(report) if options.decode_meshopt else None
    doc = GltfDoc.from_glb(glb, decoder)
    if options.decode_meshopt:
        doc.remove_extension("EXT_meshopt_compression")
    if options.dequantize:
        _dequantize(doc, report)
    if options.bake_texture_transform:
        _bake_texture_transform(doc, report)
    if options.repair_uvs:
        _repair(doc, report, options.log)
    if options.webp_to_png:
        _webp_to_png(doc, report, options.prefer_pillow)
    if options.scale != 1.0:
        _apply_scale(doc, options.scale)
    _drop_unused(doc)
    return doc.to_glb(), report


def _apply_scale(doc, factor):
    """Parent every scene's root nodes to one new node carrying a uniform scale."""
    if not (factor > 0 and math.isfinite(factor)):
        raise ValueError("scale must be a positive number, got %r" % factor)
    g = doc.gltf
    nodes = g.setdefault("nodes", [])
    scenes = g.get("scenes")
    if not scenes:
        children = set(c for n in nodes for c in n.get("children", []))
        scenes = g["scenes"] = [{"nodes": [i for i in range(len(nodes)) if i not in children]}]
        g["scene"] = 0
    for scene in scenes:
        nodes.append({"name": "MeshyScale", "scale": [factor] * 3, "children": list(scene.get("nodes", []))})
        scene["nodes"] = [len(nodes) - 1]


def _drop_unused(doc):
    """Remove bufferViews nothing references any more (compressed originals, old images...)."""
    g = doc.gltf
    used = set()
    for acc in g.get("accessors", []):
        if "bufferView" in acc:
            used.add(acc["bufferView"])
        sp = acc.get("sparse")
        if sp:
            used.add(sp["indices"]["bufferView"])
            used.add(sp["values"]["bufferView"])
    for img in g.get("images", []):
        if "bufferView" in img:
            used.add(img["bufferView"])
    views = g.get("bufferViews", [])
    remap = {}
    new_views, new_data = [], []
    for i, bv in enumerate(views):
        if i in used:
            remap[i] = len(new_views)
            new_views.append(bv)
            new_data.append(doc.views[i])
    for acc in g.get("accessors", []):
        if "bufferView" in acc:
            acc["bufferView"] = remap[acc["bufferView"]]
        sp = acc.get("sparse")
        if sp:
            sp["indices"]["bufferView"] = remap[sp["indices"]["bufferView"]]
            sp["values"]["bufferView"] = remap[sp["values"]["bufferView"]]
    for img in g.get("images", []):
        if "bufferView" in img:
            img["bufferView"] = remap[img["bufferView"]]
    g["bufferViews"] = new_views
    doc.views = new_data


def normalize_meshy_bytes(data, options=None):
    return normalize_glb(decode_meshy_bytes(data), options)


def normalize_meshy_file(path, options=None):
    with open(path, "rb") as f:
        return normalize_meshy_bytes(f.read(), options)
