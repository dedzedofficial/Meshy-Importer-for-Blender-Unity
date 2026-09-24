import math
import unittest

import _path  # noqa: F401
from meshy_core.uv_repair import repair_uvs
from meshy_fixtures import grid_mesh


def _close(a, b, tol=1e-6):
    return abs(a[0] - b[0]) <= tol and abs(a[1] - b[1]) <= tol


class UvRepairTests(unittest.TestCase):
    def setUp(self):
        self.pos, self.uv, self.idx = grid_mesh()

    def test_clean_atlas_is_untouched(self):
        new, stats = repair_uvs(self.pos, self.uv, self.idx)
        self.assertIsNone(new)
        self.assertEqual(stats["bad_vertices"], 0)

    def test_tiny_valid_triangles_are_not_flagged(self):
        # Scaling UVs to a 1/4096 corner keeps every triangle valid.
        uv = [(u / 4096.0, v / 4096.0) for u, v in self.uv]
        new, stats = repair_uvs(self.pos, uv, self.idx)
        self.assertIsNone(new)

    def test_nan_and_outlier_are_filled_from_neighbours(self):
        uv = list(self.uv)
        centre = 40  # interior vertex of the 9x9 grid
        uv[centre] = (float("nan"), 0.5)
        uv[12] = (50.0, -40.0)
        new, stats = repair_uvs(self.pos, uv, self.idx)
        self.assertEqual(stats["bad_vertices"], 2)
        self.assertFalse(stats["regenerated"])
        self.assertTrue(all(math.isfinite(c) for p in new for c in p))
        # A regular grid's neighbour average is close to the true value.
        self.assertLess(abs(new[centre][0] - self.uv[centre][0]), 0.07)
        self.assertLess(abs(new[12][1] - self.uv[12][1]), 0.07)
        # Every other vertex keeps its exact UV.
        for i in range(len(uv)):
            if i not in (centre, 12):
                self.assertEqual(new[i], self.uv[i])

    def test_collapsed_vertex_is_detected(self):
        # Move one vertex's UV so that it lies exactly on its neighbours' line in every triangle.
        pos = [(0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (2, 0, 0), (2, 1, 0)]
        uv = [(0, 0), (0.5, 0), (0, 1), (0.5, 1), (1, 0), (1, 1)]
        idx = [0, 1, 2, 1, 3, 2, 1, 4, 3, 4, 5, 3]
        bad_uv = list(uv)
        bad_uv[2] = (0, 0)
        bad_uv[3] = (0.5, 0)
        new, stats = repair_uvs(pos, bad_uv, idx)
        self.assertGreater(stats["collapsed_triangles"], 0)

    def test_missing_uvs_are_generated(self):
        new, stats = repair_uvs(self.pos, None, self.idx)
        self.assertTrue(stats["regenerated"])
        self.assertEqual(len(new), len(self.pos))
        self.assertTrue(all(0.0 <= c <= 1.0 for p in new for c in p))

    def test_all_zero_uvs_are_regenerated(self):
        new, stats = repair_uvs(self.pos, [(0.0, 0.0)] * len(self.pos), self.idx)
        self.assertTrue(stats["regenerated"])
        self.assertGreater(len(set(new)), len(self.pos) // 2)

    def test_seam_copy(self):
        # Two quads sharing an edge, split at the seam (vertices 4,5 duplicate 1,3).
        pos = [(0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0), (1, 1, 0), (2, 0, 0), (2, 1, 0)]
        uv = [(0, 0), (0.4, 0), (0, 1), (0.4, 1), (0.6, 0), (0.6, 1), (1, 0), (1, 1)]
        idx = [0, 1, 2, 1, 3, 2, 4, 6, 5, 6, 7, 5]
        bad = list(uv)
        bad[4] = (float("nan"), float("nan"))
        bad[5] = (float("inf"), 0.0)
        bad[6] = (float("nan"), 0.0)
        new, stats = repair_uvs(pos, bad, idx)
        self.assertLessEqual(stats["bad_vertices"], len(pos) // 2)
        self.assertTrue(all(math.isfinite(c) for p in new for c in p))
        self.assertEqual(new[0], uv[0])

    def test_is_deterministic(self):
        uv = list(self.uv)
        uv[30] = (float("nan"), 0.0)
        a, _ = repair_uvs(self.pos, uv, self.idx)
        b, _ = repair_uvs(self.pos, uv, self.idx)
        self.assertEqual(a, b)


if __name__ == "__main__":
    unittest.main()
