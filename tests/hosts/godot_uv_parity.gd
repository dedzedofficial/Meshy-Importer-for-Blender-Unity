extends SceneTree
## Runs meshy_uv_repair.gd on the cases JSON written by tests/cross_check_uv.py and
## prints the results as one JSON line.

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var cases = JSON.parse_string(FileAccess.get_file_as_string(args[0]))
	var repair = load("res://addons/meshy_importer/meshy_uv_repair.gd")
	var results := []
	for c in cases:
		var pos := []
		for p in c["pos"]:
			pos.append([float(p[0]), float(p[1]), float(p[2])])
		var idx := PackedInt64Array()
		for i in c["idx"]:
			idx.append(int(i))
		var uvs := []
		if c["uv"] != null:
			for uv in c["uv"]:
				uvs.append([_num(uv[0]), _num(uv[1])])
		var r: Dictionary = repair.repair_uvs(pos, uvs, idx)
		var st: Dictionary = r["stats"]
		results.append({"uv": r["uvs"], "bad": st["bad_vertices"], "collapsed": st["collapsed_triangles"],
			"repaired": st["repaired_vertices"], "projected": st["projected_vertices"], "regen": st["regenerated"]})
	print(JSON.stringify(results))
	quit()


func _num(v) -> float:
	if v is String:
		match v:
			"NaN": return NAN
			"Infinity": return INF
			"-Infinity": return -INF
	return float(v)
