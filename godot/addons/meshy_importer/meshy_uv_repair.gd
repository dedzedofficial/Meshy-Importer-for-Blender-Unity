@tool
extends RefCounted
## Smart UV repair. GDScript port of core/python/meshy_core/uv_repair.py (the reference
## implementation, which has the tests); the C# port is unity/Editor/MeshyUvRepair.cs.
## Keep all three in step -- tests/cross_check_uv.py compares them.
##
## Rule: repair only what is broken, never touch valid UVs.
##  1. Bad vertices: NaN/infinite UVs; outliers outside [p5 - 2R, p95 + 2R]
##     (R = p95 - p5, per axis); vertices whose every triangle is collapsed
##     (UV area <= 1e-6 * longest UV edge^2 while the 3D triangle is not a sliver).
##  2. Nothing bad: unchanged. More than half bad, or no UVs: box projection.
##  3. Otherwise: ring fill from good triangle neighbours, then copy from a good
##     vertex at the same position (split seams) and ring fill again, then box
##     projection for anything left.

const REGENERATE_FRACTION := 0.5
const SLIVER_EPS := 1e-6
const WELD_EPS := 1e-5


static func _finite(v: float) -> bool:
	return not (is_nan(v) or is_inf(v))


static func _percentile(sorted_vals: Array, frac: float) -> float:
	return sorted_vals[int(floor(frac * (sorted_vals.size() - 1)))]


static func _is_collapsed(pa: Array, pb: Array, pc: Array, ua: Array, ub: Array, uc: Array) -> bool:
	var e1x: float = pb[0] - pa[0]; var e1y: float = pb[1] - pa[1]; var e1z: float = pb[2] - pa[2]
	var e2x: float = pc[0] - pa[0]; var e2y: float = pc[1] - pa[1]; var e2z: float = pc[2] - pa[2]
	var e3x: float = pc[0] - pb[0]; var e3y: float = pc[1] - pb[1]; var e3z: float = pc[2] - pb[2]
	var cx := e1y * e2z - e1z * e2y
	var cy := e1z * e2x - e1x * e2z
	var cz := e1x * e2y - e1y * e2x
	var pos_area := sqrt(cx * cx + cy * cy + cz * cz)
	var pos_edge2: float = max(e1x * e1x + e1y * e1y + e1z * e1z, e2x * e2x + e2y * e2y + e2z * e2z, e3x * e3x + e3y * e3y + e3z * e3z)
	if pos_edge2 <= 0.0 or pos_area <= SLIVER_EPS * pos_edge2:
		return false
	var f1x: float = ub[0] - ua[0]; var f1y: float = ub[1] - ua[1]
	var f2x: float = uc[0] - ua[0]; var f2y: float = uc[1] - ua[1]
	var f3x: float = uc[0] - ub[0]; var f3y: float = uc[1] - ub[1]
	var uv_area := absf(f1x * f2y - f1y * f2x)
	var uv_edge2: float = max(f1x * f1x + f1y * f1y, f2x * f2x + f2y * f2y, f3x * f3x + f3y * f3y)
	return uv_area <= SLIVER_EPS * uv_edge2


static func _bounds(positions: Array) -> Array:
	var mn: Array = (positions[0] as Array).duplicate()
	var mx: Array = (positions[0] as Array).duplicate()
	for p in positions:
		for k in 3:
			mn[k] = min(mn[k], p[k])
			mx[k] = max(mx[k], p[k])
	return [mn, mx]


static func box_project(positions: Array, indices: PackedInt64Array, normals: Array, only = null) -> Array:
	var out := []
	out.resize(positions.size())
	if positions.is_empty():
		return out
	var b := _bounds(positions)
	var mn: Array = b[0]
	var mx: Array = b[1]
	var ext: float = max(mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2])
	if ext <= 0.0:
		ext = 1.0
	if normals.is_empty():
		normals = []
		for _p in positions:
			normals.append([0.0, 0.0, 0.0])
		var t := 0
		while t + 2 < indices.size():
			var a := indices[t]; var bb := indices[t + 1]; var c := indices[t + 2]
			var pa: Array = positions[a]; var pb: Array = positions[bb]; var pc: Array = positions[c]
			var e1 := [pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]]
			var e2 := [pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2]]
			var nrm := [e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]]
			for v in [a, bb, c]:
				for k in 3:
					normals[v][k] += nrm[k]
			t += 3
	for i in positions.size():
		if only != null and not (only as Dictionary).has(i):
			continue
		var p: Array = positions[i]
		var n: Array = normals[i]
		var ax := absf(n[0]); var ay := absf(n[1]); var az := absf(n[2])
		var x: float = (p[0] - mn[0]) / ext
		var y: float = (p[1] - mn[1]) / ext
		var z: float = (p[2] - mn[2]) / ext
		if ax >= ay and ax >= az:
			out[i] = [z, y]
		elif ay >= az:
			out[i] = [x, z]
		else:
			out[i] = [x, y]
	return out


static func _ring_fill(bad: Array, out: Array, neighbours: Array) -> int:
	var repaired := 0
	while true:
		var updates := []
		for i in bad.size():
			if not bad[i]:
				continue
			var su := 0.0
			var sv := 0.0
			var cnt := 0
			for j in neighbours[i]:
				if not bad[j]:
					su += out[j][0]
					sv += out[j][1]
					cnt += 1
			if cnt > 0:
				updates.append([i, [su / cnt, sv / cnt]])
		if updates.is_empty():
			return repaired
		for u in updates:
			out[u[0]] = u[1]
			bad[u[0]] = false
		repaired += updates.size()
	return repaired


## positions: Array of [x, y, z]; uvs: Array of [u, v] (or empty for "no UVs");
## indices: flat triangle list; normals: Array of [x, y, z] or empty.
## Returns {"uvs": Array or null (unchanged), "stats": Dictionary}.
static func repair_uvs(positions: Array, uvs: Array, indices: PackedInt64Array, normals: Array = []) -> Dictionary:
	var n := positions.size()
	var stats := {"vertices": n, "triangles": indices.size() / 3, "bad_vertices": 0, "collapsed_triangles": 0,
		"repaired_vertices": 0, "projected_vertices": 0, "regenerated": false}
	if n == 0:
		return {"uvs": null, "stats": stats}
	if uvs.size() != n:
		stats["regenerated"] = true
		stats["bad_vertices"] = n
		stats["projected_vertices"] = n
		return {"uvs": box_project(positions, indices, normals), "stats": stats}

	var bad := []
	bad.resize(n)
	var us := []
	var vs := []
	for i in n:
		var uv: Array = uvs[i]
		bad[i] = not (_finite(uv[0]) and _finite(uv[1]))
		if not bad[i]:
			us.append(uv[0])
			vs.append(uv[1])
	if not us.is_empty():
		us.sort()
		vs.sort()
		var u5 := _percentile(us, 0.05); var u95 := _percentile(us, 0.95)
		var v5 := _percentile(vs, 0.05); var v95 := _percentile(vs, 0.95)
		var ru := u95 - u5
		var rv := v95 - v5
		for i in n:
			var uv: Array = uvs[i]
			if not bad[i] and (uv[0] < u5 - 2 * ru or uv[0] > u95 + 2 * ru or uv[1] < v5 - 2 * rv or uv[1] > v95 + 2 * rv):
				bad[i] = true

	var tri_count := indices.size() / 3
	var incident := []
	incident.resize(n)
	incident.fill(0)
	var collapsed_incident := []
	collapsed_incident.resize(n)
	collapsed_incident.fill(0)
	for t in tri_count:
		var a := indices[3 * t]; var b := indices[3 * t + 1]; var c := indices[3 * t + 2]
		incident[a] += 1; incident[b] += 1; incident[c] += 1
		if bad[a] or bad[b] or bad[c]:
			continue
		if _is_collapsed(positions[a], positions[b], positions[c], uvs[a], uvs[b], uvs[c]):
			stats["collapsed_triangles"] += 1
			collapsed_incident[a] += 1; collapsed_incident[b] += 1; collapsed_incident[c] += 1
	for i in n:
		if collapsed_incident[i] > 0 and collapsed_incident[i] == incident[i]:
			bad[i] = true

	var bad_count := 0
	for x in bad:
		if x:
			bad_count += 1
	stats["bad_vertices"] = bad_count
	if bad_count == 0:
		return {"uvs": null, "stats": stats}
	if bad_count > REGENERATE_FRACTION * n:
		stats["regenerated"] = true
		stats["projected_vertices"] = n
		return {"uvs": box_project(positions, indices, normals), "stats": stats}

	var out := []
	out.resize(n)
	for i in n:
		out[i] = [float(uvs[i][0]), float(uvs[i][1])]

	var neighbour_sets := []
	neighbour_sets.resize(n)
	for i in n:
		neighbour_sets[i] = {}
	for t in tri_count:
		var a := indices[3 * t]; var b := indices[3 * t + 1]; var c := indices[3 * t + 2]
		neighbour_sets[a][b] = true; neighbour_sets[a][c] = true
		neighbour_sets[b][a] = true; neighbour_sets[b][c] = true
		neighbour_sets[c][a] = true; neighbour_sets[c][b] = true
	var neighbours := []
	neighbours.resize(n)
	for i in n:
		var keys: Array = neighbour_sets[i].keys()
		keys.sort()
		neighbours[i] = keys

	var repaired := _ring_fill(bad, out, neighbours)

	if bad.has(true):
		var bb := _bounds(positions)
		var diag := 0.0
		for k in 3:
			diag += pow(bb[1][k] - bb[0][k], 2)
		diag = sqrt(diag)
		var cell := diag * WELD_EPS if diag > 0.0 else 1e-12
		var groups := {}
		var keys := []
		keys.resize(n)
		for i in n:
			var p: Array = positions[i]
			var key := Vector3i(int(floor(p[0] / cell + 0.5)), int(floor(p[1] / cell + 0.5)), int(floor(p[2] / cell + 0.5)))
			keys[i] = key
			if not groups.has(key):
				groups[key] = []
			groups[key].append(i)
		var copied := []
		for i in n:
			if bad[i]:
				for j in groups[keys[i]]:
					if not bad[j]:
						copied.append([i, out[j]])
						break
		for cp in copied:
			out[cp[0]] = cp[1]
			bad[cp[0]] = false
		repaired += copied.size()
		if not copied.is_empty():
			repaired += _ring_fill(bad, out, neighbours)

	var left := {}
	for i in n:
		if bad[i]:
			left[i] = true
	if not left.is_empty():
		var proj := box_project(positions, indices, normals, left)
		for i in left:
			out[i] = proj[i]
		stats["projected_vertices"] = left.size()
	stats["repaired_vertices"] = repaired
	return {"uvs": out, "stats": stats}
