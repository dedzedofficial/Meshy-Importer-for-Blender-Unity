@tool
extends EditorSceneFormatImporter
## Imports Meshy .meshy payloads: decrypt -> decode meshopt / repair UVs (meshy_glb.gd)
## -> Godot's own GLTFDocument. The result goes through Godot's normal scene import,
## so drag-and-drop, reimport and the Advanced Import Settings dialog all work.

const Decrypt := preload("meshy_decrypt.gd")
const Glb := preload("meshy_glb.gd")


func _get_extensions() -> PackedStringArray:
	return PackedStringArray(["meshy"])


func _get_import_flags() -> int:
	return IMPORT_SCENE | IMPORT_ANIMATION


func _import_scene(path: String, flags: int, options: Dictionary) -> Object:
	var bytes := FileAccess.get_file_as_bytes(path)
	if bytes.is_empty() and FileAccess.get_open_error() != OK:
		_fail(path, "Could not read the file (error %d)." % FileAccess.get_open_error())
		return null
	var decoded := Decrypt.decode(bytes)
	if decoded.has("error"):
		_fail(path, decoded["error"])
		return null
	var repair := bool(options.get("meshy/auto_repair_uvs", true))
	var normalized := Glb.normalize(decoded["glb"], repair)
	if normalized.has("error"):
		_fail(path, normalized["error"])
		return null
	var glb: PackedByteArray = normalized["glb"]

	if bool(options.get("meshy/save_decoded_glb", false)):
		var out_path := path.get_basename() + ".glb"
		if FileAccess.file_exists(out_path):
			push_warning("Meshy Importer: %s already exists; not overwriting it." % out_path)
		else:
			var f := FileAccess.open(out_path, FileAccess.WRITE)
			if f:
				f.store_buffer(glb)
				f.close()

	var state := GLTFState.new()
	state.filename = path.get_file().get_basename()
	var doc := GLTFDocument.new()
	var err := doc.append_from_buffer(glb, path.get_base_dir(), state, flags)
	if err != OK:
		_fail(path, "Godot's glTF importer rejected the decoded model (error %d)." % err)
		return null
	var scene: Node = doc.generate_scene(state, float(options.get("animation/fps", 30.0)),
		bool(options.get("animation/trimming", false)), bool(options.get("animation/remove_immutable_tracks", true)))
	if scene == null:
		_fail(path, "Could not build a scene from the decoded model.")
		return null

	var bad := 0
	var regenerated := 0
	for st in normalized["uv_stats"]:
		bad += int(st["bad_vertices"])
		if st["regenerated"]:
			regenerated += 1
	scene.set_meta("meshy_importer_version", "1.5.0")
	scene.set_meta("meshy_uv_bad_vertices", bad)
	scene.set_meta("meshy_uv_regenerated_meshes", regenerated)
	if bad > 0:
		print("Meshy Importer: %s: repaired %d bad vertex UV(s)%s." % [path, bad,
			(", regenerated UVs on %d mesh(es)" % regenerated) if regenerated > 0 else ""])
	return scene


## Errors go to the Output panel; the last one is kept for Project > Tools > Meshy Importer >
## Copy Diagnostics.
static func _fail(path: String, message: String) -> void:
	push_error("Meshy Importer: %s: %s" % [path, message])
	Engine.set_meta("meshy_last_error", "%s: %s" % [path.get_file(), message])
