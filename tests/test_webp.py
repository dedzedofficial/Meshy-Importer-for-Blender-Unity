import io
import os
import random
import struct
import unittest
import zlib

import _path  # noqa: F401
from meshy_core import webp_vp8

FIX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")


def _pillow():
    try:
        from PIL import Image, features
        return Image if features.check("webp") else None
    except ImportError:
        return None


class WebpTests(unittest.TestCase):
    def test_fixture_matches_libwebp(self):
        with open(os.path.join(FIX, "gradient_40x24.webp"), "rb") as f:
            data = f.read()
        with open(os.path.join(FIX, "gradient_40x24.rgb"), "rb") as f:
            ref = f.read()
        w, h, rgb = webp_vp8.decode_rgb_pure(data)
        self.assertEqual((w, h), (40, 24))
        self.assertEqual(rgb, ref)

    @unittest.skipUnless(_pillow(), "Pillow with WebP support not installed")
    def test_random_images_match_libwebp(self):
        Image = _pillow()
        rnd = random.Random(1234)
        for w, h, q in ((17, 13, 80), (64, 48, 30), (33, 47, 95), (130, 70, 60), (16, 16, 5), (31, 90, 100)):
            im = Image.new("RGB", (w, h))
            px = im.load()
            for y in range(h):
                for x in range(w):
                    if (x // 8 + y // 8) % 3 == 0:
                        px[x, y] = (rnd.randrange(256), rnd.randrange(256), rnd.randrange(256))
                    else:
                        px[x, y] = ((x * x + y) % 256, (y * 3) % 256, (x * 5) % 256)
            buf = io.BytesIO()
            im.save(buf, "WEBP", quality=q)
            data = buf.getvalue()
            ref = Image.open(io.BytesIO(data)).convert("RGB").tobytes()
            self.assertEqual(webp_vp8.decode_rgb_pure(data), (w, h, ref), "size %dx%d q%d" % (w, h, q))

    def test_png_writer(self):
        png = webp_vp8.rgb_to_png(2, 1, b"\x01\x02\x03\x04\x05\x06")
        self.assertEqual(png[:8], b"\x89PNG\r\n\x1a\n")
        w, h = struct.unpack(">II", png[16:24])
        self.assertEqual((w, h), (2, 1))
        idat_len = struct.unpack(">I", png[33:37])[0]
        raw = zlib.decompress(png[41:41 + idat_len])
        self.assertEqual(raw, b"\x00\x01\x02\x03\x04\x05\x06")

    def test_rejects_lossless(self):
        riff = b"RIFF" + struct.pack("<I", 20) + b"WEBP" + b"VP8L" + struct.pack("<I", 8) + b"\x00" * 8
        with self.assertRaisesRegex(ValueError, "lossless"):
            webp_vp8.decode_rgb_pure(riff)


if __name__ == "__main__":
    unittest.main()
