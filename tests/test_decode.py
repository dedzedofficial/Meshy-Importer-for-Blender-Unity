import os
import struct
import unittest

import _path  # noqa: F401
from meshy_core import decode


class AesTests(unittest.TestCase):
    def test_fips197_aes256_known_answer(self):
        key = bytes(range(32))
        pt = bytes.fromhex("00112233445566778899aabbccddeeff")
        self.assertEqual(decode.aes256_encrypt_block(key, pt).hex(), "8ea2b7ca516745bfeafc49904b496089")

    def test_ctr_is_symmetric(self):
        data = os.urandom(1000)
        nonce = os.urandom(12)
        once = decode.aes_ctr(data, decode._KEY, nonce)
        self.assertNotEqual(once, data)
        self.assertEqual(decode.aes_ctr(once, decode._KEY, nonce), data)


class ContainerTests(unittest.TestCase):
    def _glb(self, size):
        body = os.urandom(size - 12)
        return b"glTF" + struct.pack("<II", 2, size) + body

    def test_round_trip(self):
        glb = self._glb(20000)
        meshy = decode.encode_meshy_bytes(glb, b"\x01" * 12)
        self.assertEqual(meshy[:8], b"MESHY.AI")
        self.assertEqual(len(meshy), len(glb) + 32 + 16)
        self.assertEqual(decode.decode_meshy_bytes(meshy), glb)

    def test_length_field_is_rewritten(self):
        glb = bytearray(self._glb(9000))
        struct.pack_into("<I", glb, 8, 123)  # Meshy's header length is not trustworthy
        out = decode.decode_meshy_bytes(decode.encode_meshy_bytes(bytes(glb)))
        self.assertEqual(struct.unpack_from("<I", out, 8)[0], len(out))

    def test_rejects_bad_header(self):
        with self.assertRaises(decode.MeshyFormatError):
            decode.decode_meshy_bytes(b"NOTMESHY" + b"\x00" * 9000)

    def test_rejects_short_file(self):
        with self.assertRaises(decode.MeshyFormatError):
            decode.decode_meshy_bytes(b"MESHY.AI" + b"\x00" * 100)

    def test_rejects_renamed_glb(self):
        with self.assertRaisesRegex(decode.MeshyFormatError, "plain GLB"):
            decode.decode_meshy_bytes(self._glb(9000))

    def test_wrong_key_or_format_change_is_reported(self):
        meshy = bytearray(decode.encode_meshy_bytes(self._glb(9000)))
        meshy[10] ^= 0xFF  # corrupt the nonce
        with self.assertRaisesRegex(decode.MeshyFormatError, "invalid GLB header"):
            decode.decode_meshy_bytes(bytes(meshy))


if __name__ == "__main__":
    unittest.main()
