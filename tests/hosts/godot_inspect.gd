extends SceneTree
## Loads imported .meshy scenes and prints a summary line per file; exits 1 on failure.

func _init() -> void:
	var failed := false
	for path in OS.get_cmdline_user_args():
		var packed = load(path)
		if packed == null:
			print("FAIL %s: did not import" % path)
			failed = true
			continue
		var scene: Node = packed.instantiate()
		var meshes := scene.find_children("*", "MeshInstance3D", true, false)
		if meshes.is_empty():
			print("FAIL %s: no MeshInstance3D" % path)
			failed = true
			continue
		var mi: MeshInstance3D = meshes[0]
		var mesh: Mesh = mi.mesh
		var arrays := mesh.surface_get_arrays(0)
		var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var uvs: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV]
		var mat := mesh.surface_get_material(0) as StandardMaterial3D
		var tex := mat.albedo_texture if mat else null
		var umin := INF; var umax := -INF
		for uv in uvs:
			umin = min(umin, uv.x); umax = max(umax, uv.x)
		print("OK %s: %d verts, uv.x [%.4f..%.4f], uv1_scale=%s, albedo=%s, bad_uvs=%s" % [path, verts.size(), umin, umax,
			str(mat.uv1_scale) if mat else "-", str(tex.get_size()) if tex else "none", str(scene.get_meta("meshy_uv_bad_vertices", "?"))])
		if tex == null or verts.size() == 0 or uvs.size() != verts.size():
			failed = true
	quit(1 if failed else 0)
