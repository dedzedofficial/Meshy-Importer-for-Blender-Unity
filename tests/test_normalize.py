import json
import os
import struct
import subprocess
import sys
import tempfile
import unittest

import _path
from meshy_core import decode, normalize
from meshy_core.glb import GltfDoc, read_chunks
from meshy_fixtures import UV_SCALE, build_meshy, grid_mesh

FIX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")


class NormalizeTests(unittest.TestCase):
    def test_full_normalize_produces_plain_gltf(self):
        glb, report = normalize.normalize_meshy_bytes(build_meshy(), normalize.NormalizeOptions.for_host("full"))
        gltf, _ = read_chunks(glb)
        self.assertNotIn("extensionsRequired", gltf)
        self.assertEqual(gltf.get("extensionsUsed", []), [])
        self.assertEqual(report.meshopt_views, 1)
        self.assertEqual(report.png_images, 1)
        self.assertEqual(gltf["images"][0]["mimeType"], "image/png")
        self.assertEqual(struct.unpack_from("<I", glb, 8)[0], len(glb))

        doc = GltfDoc.from_glb(glb)
        prim = gltf["meshes"][0]["primitives"][0]
        pos = doc.read_accessor(prim["attributes"]["POSITION"])
        uv = doc.read_accessor(prim["attributes"]["TEXCOORD_0"])
        ref_pos, ref_uv, ref_idx = grid_mesh()
        self.assertEqual(gltf["accessors"][prim["attributes"]["POSITION"]]["componentType"], 5126)
        for p, r in zip(pos, ref_pos):
            self.assertAlmostEqual(p[0], round(r[0] * 1000) + 1000, places=3)
        for u, r in zip(uv, ref_uv):  # texture transform baked: back in 0..1
            self.assertAlmostEqual(u[0], r[0], delta=1e-3)
            self.assertAlmostEqual(u[1], r[1], delta=1e-3)
        self.assertEqual(doc.read_accessor(prim["indices"]), ref_idx)
        tex = gltf["materials"][0]["pbrMetallicRoughness"]["baseColorTexture"]
        self.assertNotIn("extensions", tex)

    def test_blender_preset_only_decodes_meshopt(self):
        glb, _ = normalize.normalize_meshy_bytes(build_meshy(), normalize.NormalizeOptions.for_host("blender"))
        gltf, _ = read_chunks(glb)
        self.assertNotIn("EXT_meshopt_compression", gltf.get("extensionsUsed", []))
        self.assertIn("KHR_mesh_quantization", gltf["extensionsRequired"])
        self.assertIn("EXT_texture_webp", gltf["extensionsRequired"])
        self.assertEqual(len(gltf["buffers"]), 1)

    def test_uv_repair_runs_on_broken_payload(self):
        glb, report = normalize.normalize_meshy_bytes(build_meshy(broken_uvs=True),
                                                      normalize.NormalizeOptions.for_host("full"))
        self.assertEqual(len(report.uv), 1)
        self.assertGreater(report.uv[0]["bad_vertices"], 0)
        self.assertFalse(report.uv[0]["regenerated"])

    def test_png_matches_webp_pixels(self):
        glb, _ = normalize.normalize_meshy_bytes(build_meshy(), normalize.NormalizeOptions.for_host(
            "full", prefer_pillow=False))
        doc = GltfDoc.from_glb(glb)
        png = doc.views[doc.gltf["images"][0]["bufferView"]]
        import zlib
        w, h = struct.unpack(">II", png[16:24])
        idat_len = struct.unpack(">I", png[33:37])[0]
        raw = zlib.decompress(png[41:41 + idat_len])
        rgb = b"".join(raw[y * (w * 3 + 1) + 1:(y + 1) * (w * 3 + 1)] for y in range(h))
        with open(os.path.join(FIX, "gradient_40x24.rgb"), "rb") as f:
            self.assertEqual(rgb, f.read())

    def test_cli(self):
        with tempfile.TemporaryDirectory() as tmp:
            src = os.path.join(tmp, "model.meshy")
            with open(src, "wb") as f:
                f.write(build_meshy())
            env = dict(os.environ, PYTHONPATH=os.path.join(_path.ROOT, "core", "python"))
            res = subprocess.run([sys.executable, "-m", "meshy_core", src], env=env,
                                 capture_output=True, text=True)
            self.assertEqual(res.returncode, 0, res.stderr)
            with open(os.path.join(tmp, "model.glb"), "rb") as f:
                gltf, _ = read_chunks(f.read())
            self.assertNotIn("extensionsRequired", gltf)
            raw = subprocess.run([sys.executable, "-m", "meshy_core", "--raw", src, "-o",
                                  os.path.join(tmp, "raw.glb")], env=env, capture_output=True, text=True)
            self.assertEqual(raw.returncode, 0, raw.stderr)
            with open(os.path.join(tmp, "raw.glb"), "rb") as f:
                self.assertEqual(f.read(), decode.decode_meshy_bytes(build_meshy()))


if __name__ == "__main__":
    unittest.main()
