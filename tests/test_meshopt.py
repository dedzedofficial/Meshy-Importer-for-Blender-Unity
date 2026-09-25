"""Vectors copied from meshoptimizer's demo/tests.cpp (MIT licensed)."""

import struct
import unittest

import _path  # noqa: F401
from meshy_core import meshopt
from meshy_fixtures import meshopt_encode_vertices_raw

INDEX_V0 = bytes([0xe0, 0xf0, 0x10, 0xfe, 0xff, 0xf0, 0x0c, 0xff, 0x02, 0x02, 0x02, 0x00, 0x76, 0x87, 0x56, 0x67,
                  0x78, 0xa9, 0x86, 0x65, 0x89, 0x68, 0x98, 0x01, 0x69, 0x00, 0x00])
INDEX_V1 = bytes([0xe1, 0xf0, 0x10, 0xfe, 0x1f, 0x3d, 0x00, 0x0a, 0x00, 0x76, 0x87, 0x56, 0x67, 0x78, 0xa9, 0x86,
                  0x65, 0x89, 0x68, 0x98, 0x01, 0x69, 0x00, 0x00])
SEQUENCE_V1 = bytes([0xd1, 0x00, 0x04, 0xcd, 0x01, 0x04, 0x07, 0x98, 0x1f, 0x00, 0x00, 0x00, 0x00])
VERTEX_V0 = bytes([0xa0, 0x01, 0x3f, 0x00, 0x00, 0x00, 0x58, 0x57, 0x58, 0x01, 0x26, 0x00, 0x00, 0x00, 0x01, 0x0c,
                   0x00, 0x00, 0x00, 0x58, 0x01, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x3f, 0x00,
                   0x00, 0x00, 0x17, 0x18, 0x17, 0x01, 0x26, 0x00, 0x00, 0x00, 0x01, 0x0c, 0x00, 0x00, 0x00, 0x17,
                   0x01, 0x08] + [0] * 35)


class MeshoptTests(unittest.TestCase):
    def test_index_v0(self):
        self.assertEqual(meshopt.decode_index_buffer(INDEX_V0, 12), [0, 1, 2, 2, 1, 3, 4, 6, 5, 7, 8, 9])

    def test_index_v1(self):
        self.assertEqual(meshopt.decode_index_buffer(INDEX_V1, 15),
                         [0, 1, 2, 2, 1, 3, 0, 1, 2, 2, 1, 5, 2, 1, 4])

    def test_index_sequence(self):
        self.assertEqual(meshopt.decode_index_sequence(SEQUENCE_V1, 6), [0, 1, 51, 2, 49, 1000])

    def test_vertex_v0(self):
        out = meshopt.decode_vertex_buffer(VERTEX_V0, 4, 12)
        rows = [struct.unpack_from("<3H2B2H", out, i * 12) for i in range(4)]
        self.assertEqual(rows, [(0, 0, 0, 0, 0, 0, 0), (300, 0, 0, 0, 0, 500, 0),
                                (0, 300, 0, 0, 0, 0, 500), (300, 300, 0, 0, 0, 500, 500)])

    def test_vertex_round_trip_multi_block(self):
        data = bytes((i * 37 + (i >> 3)) & 0xFF for i in range(700 * 16))
        enc = meshopt_encode_vertices_raw(data, 700, 16)
        self.assertEqual(meshopt.decode_vertex_buffer(enc, 700, 16), data)

    def test_filter_oct8(self):
        b = bytearray([0, 1, 127, 0, 0, 187, 127, 1, 255, 1, 127, 0, 14, 130, 127, 1])
        meshopt.decode_filter_oct(b, 4, 4)
        self.assertEqual(list(b), [0, 1, 127, 0, 0, 159, 82, 1, 255, 1, 127, 0, 1, 130, 241, 1])

    def test_filter_oct12(self):
        b = bytearray(struct.pack("<16H", 0, 1, 2047, 0, 0, 1870, 2047, 1, 2017, 1, 2047, 0, 14, 1300, 2047, 1))
        meshopt.decode_filter_oct(b, 4, 8)
        self.assertEqual(struct.unpack("<16H", b),
                         (0, 16, 32767, 0, 0, 32621, 3088, 1, 32764, 16, 471, 0, 307, 28541, 16093, 1))

    def test_filter_quat12(self):
        b = bytearray(struct.pack("<16H", 0, 1, 0, 0x7fc, 0, 1870, 0, 0x7fd, 2017, 1, 0, 0x7fe, 14, 1300, 0, 0x7ff))
        meshopt.decode_filter_quat(b, 4, 8)
        self.assertEqual(struct.unpack("<16H", b),
                         (32767, 0, 11, 0, 0, 25013, 0, 21166, 11, 0, 23504, 22830, 158, 14715, 0, 29277))

    def test_filter_exp(self):
        b = bytearray(struct.pack("<4I", 0, 0xff000003, 0x02fffff7, 0xfe7fffff))
        meshopt.decode_filter_exp(b, 4, 4)
        self.assertEqual(struct.unpack("<4I", b), (0, 0x3fc00000, 0xc2100000, 0x49fffffe))

    def test_rejects_bad_headers(self):
        with self.assertRaises(meshopt.MeshoptError):
            meshopt.decode_vertex_buffer(b"\x00" * 64, 4, 12)
        with self.assertRaises(meshopt.MeshoptError):
            meshopt.decode_index_buffer(b"\x00" * 64, 3)
        with self.assertRaises(meshopt.MeshoptError):
            meshopt.decode_vertex_buffer(bytes([0xa1]) + b"\x00" * 64, 4, 12)


if __name__ == "__main__":
    unittest.main()
