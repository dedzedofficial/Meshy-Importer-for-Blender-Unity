"""Smart UV repair shared by every host (reference implementation).

The same algorithm is ported to C# (unity/Editor/MeshyUvRepair.cs) and GDScript
(godot/addons/meshy_importer/meshy_uv_repair.gd); keep all three in step.

Rule: repair only what is broken, never touch valid UVs.

1. Detect bad vertices:
   - NaN / infinite UVs;
   - outliers: outside [p5 - 2R, p95 + 2R] on either axis, where p5/p95 are the
     5th/95th percentiles of the mesh's finite UVs and R = p95 - p5;
   - collapsed triangles: UV area <= 1e-6 * (longest UV edge)^2 while the 3D
     triangle is not itself a sliver (same test in 3D). A vertex is bad when
     every triangle that uses it is collapsed.
2. If nothing is bad, return unchanged. If more than half the vertices are bad
   (or there are no UVs), regenerate the whole set with a box projection.
3. Otherwise repair bad vertices only, in this order:
   a. ring fill: average UV of the good vertices that share a triangle with it,
      one ring at a time until no progress (stays inside the same UV chart);
   b. seam copy: UV of a good vertex at the same 3D position, then ring fill again;
   c. anything left (a fully broken island) gets a box projection.

Every check is affine-invariant, so hosts can run this on raw or transformed UVs.
"""

import math

__all__ = ["repair_uvs", "box_project", "REGENERATE_FRACTION"]

REGENERATE_FRACTION = 0.5
SLIVER_EPS = 1e-6
WELD_EPS = 1e-5


def _is_collapsed(pa, pb, pc, ua, ub, uc):
    """Scale-free sliver test: zero UV area on a triangle that has real 3D area.

    Areas are compared with the triangle's own longest edge, so tiny-but-valid
    triangles in a dense atlas are never flagged.
    """
    e1 = (pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2])
    e2 = (pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2])
    e3 = (pc[0] - pb[0], pc[1] - pb[1], pc[2] - pb[2])
    cx = e1[1] * e2[2] - e1[2] * e2[1]
    cy = e1[2] * e2[0] - e1[0] * e2[2]
    cz = e1[0] * e2[1] - e1[1] * e2[0]
    pos_area = math.sqrt(cx * cx + cy * cy + cz * cz)
    pos_edge2 = max(e1[0] * e1[0] + e1[1] * e1[1] + e1[2] * e1[2],
                    e2[0] * e2[0] + e2[1] * e2[1] + e2[2] * e2[2],
                    e3[0] * e3[0] + e3[1] * e3[1] + e3[2] * e3[2])
    if pos_edge2 <= 0 or pos_area <= SLIVER_EPS * pos_edge2:
        return False  # degenerate in 3D as well: nothing to fix
    f1 = (ub[0] - ua[0], ub[1] - ua[1])
    f2 = (uc[0] - ua[0], uc[1] - ua[1])
    f3 = (uc[0] - ub[0], uc[1] - ub[1])
    uv_area = abs(f1[0] * f2[1] - f1[1] * f2[0])
    uv_edge2 = max(f1[0] * f1[0] + f1[1] * f1[1], f2[0] * f2[0] + f2[1] * f2[1], f3[0] * f3[0] + f3[1] * f3[1])
    return uv_area <= SLIVER_EPS * uv_edge2


def _percentile(sorted_vals, frac):
    return sorted_vals[int(math.floor(frac * (len(sorted_vals) - 1)))]


def _bounds(points):
    mn = [min(p[k] for p in points) for k in range(3)]
    mx = [max(p[k] for p in points) for k in range(3)]
    return mn, mx


def _vertex_normals(positions, indices):
    normals = [[0.0, 0.0, 0.0] for _ in positions]
    for t in range(0, len(indices) - 2, 3):
        a, b, c = indices[t], indices[t + 1], indices[t + 2]
        pa, pb, pc = positions[a], positions[b], positions[c]
        e1 = (pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2])
        e2 = (pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2])
        nx = e1[1] * e2[2] - e1[2] * e2[1]
        ny = e1[2] * e2[0] - e1[0] * e2[2]
        nz = e1[0] * e2[1] - e1[1] * e2[0]
        for v in (a, b, c):
            normals[v][0] += nx
            normals[v][1] += ny
            normals[v][2] += nz
    return normals


def box_project(positions, indices, normals=None, only=None):
    """Box projection into [0,1] using the mesh bounds. Returns a list of (u, v).

    Each vertex projects along the dominant axis of its normal (X -> (z, y),
    Y -> (x, z), Z -> (x, y)), scaled by the largest bounding-box extent.
    When `only` is given, other entries are None.
    """
    if not positions:
        return []
    mn, mx = _bounds(positions)
    ext = max(mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2])
    if ext <= 0:
        ext = 1.0
    if normals is None:
        normals = _vertex_normals(positions, indices)
    out = []
    for i, p in enumerate(positions):
        if only is not None and i not in only:
            out.append(None)
            continue
        nrm = normals[i]
        ax, ay, az = abs(nrm[0]), abs(nrm[1]), abs(nrm[2])
        x = (p[0] - mn[0]) / ext
        y = (p[1] - mn[1]) / ext
        z = (p[2] - mn[2]) / ext
        if ax >= ay and ax >= az:
            out.append((z, y))
        elif ay >= az:
            out.append((x, z))
        else:
            out.append((x, y))
    return out


def _ring_fill(bad, out, neighbours):
    repaired = 0
    while True:
        updates = []
        for i, is_bad in enumerate(bad):
            if not is_bad:
                continue
            su = sv = 0.0
            cnt = 0
            for j in neighbours[i]:
                if not bad[j]:
                    su += out[j][0]
                    sv += out[j][1]
                    cnt += 1
            if cnt:
                updates.append((i, (su / cnt, sv / cnt)))
        if not updates:
            return repaired
        for i, uv in updates:  # applied after the pass: one ring at a time
            out[i] = uv
            bad[i] = False
        repaired += len(updates)


def repair_uvs(positions, uvs, indices, normals=None):
    """Return (new_uvs_or_None, stats). new_uvs is None when nothing changed.

    positions: list of (x, y, z); uvs: list of (u, v) per vertex (or None);
    indices: flat triangle list; normals: optional list of (x, y, z).
    """
    n = len(positions)
    stats = {"vertices": n, "triangles": len(indices) // 3, "bad_vertices": 0,
             "collapsed_triangles": 0, "repaired_vertices": 0, "projected_vertices": 0,
             "regenerated": False}
    if n == 0:
        return None, stats
    if not uvs or len(uvs) != n:
        stats["regenerated"] = True
        stats["bad_vertices"] = n
        stats["projected_vertices"] = n
        return box_project(positions, indices, normals), stats

    bad = [not (math.isfinite(uv[0]) and math.isfinite(uv[1])) for uv in uvs]

    finite = [uv for uv, b in zip(uvs, bad) if not b]
    if finite:
        us = sorted(uv[0] for uv in finite)
        vs = sorted(uv[1] for uv in finite)
        u5, u95 = _percentile(us, 0.05), _percentile(us, 0.95)
        v5, v95 = _percentile(vs, 0.05), _percentile(vs, 0.95)
        ru, rv = u95 - u5, v95 - v5
        ulo, uhi = u5 - 2 * ru, u95 + 2 * ru
        vlo, vhi = v5 - 2 * rv, v95 + 2 * rv
        for i, uv in enumerate(uvs):
            if not bad[i] and (uv[0] < ulo or uv[0] > uhi or uv[1] < vlo or uv[1] > vhi):
                bad[i] = True

    tri_count = len(indices) // 3
    incident = [0] * n
    collapsed_incident = [0] * n
    for t in range(tri_count):
        a, b, c = indices[3 * t], indices[3 * t + 1], indices[3 * t + 2]
        incident[a] += 1
        incident[b] += 1
        incident[c] += 1
        if bad[a] or bad[b] or bad[c]:
            continue
        if _is_collapsed(positions[a], positions[b], positions[c], uvs[a], uvs[b], uvs[c]):
            stats["collapsed_triangles"] += 1
            collapsed_incident[a] += 1
            collapsed_incident[b] += 1
            collapsed_incident[c] += 1
    for i in range(n):
        if collapsed_incident[i] and collapsed_incident[i] == incident[i]:
            bad[i] = True

    bad_count = sum(bad)
    stats["bad_vertices"] = bad_count
    if bad_count == 0:
        return None, stats
    if bad_count > REGENERATE_FRACTION * n:
        stats["regenerated"] = True
        stats["projected_vertices"] = n
        return box_project(positions, indices, normals), stats

    out = [(float(uv[0]), float(uv[1])) for uv in uvs]

    neighbours = [set() for _ in range(n)]
    for t in range(tri_count):
        a, b, c = indices[3 * t], indices[3 * t + 1], indices[3 * t + 2]
        neighbours[a].update((b, c))
        neighbours[b].update((a, c))
        neighbours[c].update((a, b))
    neighbours = [sorted(s) for s in neighbours]

    repaired = _ring_fill(bad, out, neighbours)

    if any(bad):
        mn, mx = _bounds(positions)
        diag = math.sqrt(sum((mx[k] - mn[k]) ** 2 for k in range(3)))
        cell = diag * WELD_EPS if diag > 0 else 1e-12
        groups = {}
        keys = []
        for p in positions:
            k = (int(math.floor(p[0] / cell + 0.5)), int(math.floor(p[1] / cell + 0.5)),
                 int(math.floor(p[2] / cell + 0.5)))
            keys.append(k)
            groups.setdefault(k, []).append(len(keys) - 1)
        copied = []
        for i in range(n):
            if bad[i]:
                for j in groups[keys[i]]:
                    if not bad[j]:
                        copied.append((i, out[j]))
                        break
        for i, uv in copied:
            out[i] = uv
            bad[i] = False
        repaired += len(copied)
        if copied:
            repaired += _ring_fill(bad, out, neighbours)

    left = {i for i in range(n) if bad[i]}
    if left:
        proj = box_project(positions, indices, normals, only=left)
        for i in left:
            out[i] = proj[i]
        stats["projected_vertices"] = len(left)
    stats["repaired_vertices"] = repaired
    return out, stats
