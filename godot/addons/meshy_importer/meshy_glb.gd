@tool
extends RefCounted
## Rewrites a decrypted Meshy GLB so Godot's own glTF importer can read it:
## decodes EXT_meshopt_compression and rewrites KHR_mesh_quantization attributes as
## FLOAT (Godot implements neither), and optionally repairs broken TEXCOORD_0 sets.
## KHR_texture_transform and EXT_texture_webp are handled by Godot itself.
## Mirrors core/python/meshy_core/normalize.py.

const Meshopt := preload("meshy_meshopt.gd")
const UvRepair := preload("meshy_uv_repair.gd")

const COMPONENTS := {5120: [1, 127.0], 5121: [1, 255.0], 5122: [2, 32767.0], 5123: [2, 65535.0], 5125: [4, 0.0], 5126: [4, 0.0]}
const TYPE_SIZES := {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT2": 4, "MAT3": 9, "MAT4": 16}


## Returns {"glb": PackedByteArray, "uv_stats": Array} or {"error": String}.
static func normalize(glb: PackedByteArray, repair_uvs: bool) -> Dictionary:
	var parsed := _read_chunks(glb)
	if parsed.has("error"):
		return parsed
	var gltf: Dictionary = parsed["json"]
	var bin: PackedByteArray = parsed["bin"]

	var buffers := []
	var buffer_defs: Array = gltf.get("buffers", [])
	for i in buffer_defs.size():
		if buffer_defs[i].has("uri"):
			return {"error": "The payload references an external buffer, which a .meshy file should not do."}
		buffers.append(bin if i == 0 else PackedByteArray())

	var views := []
	var view_defs: Array = gltf.get("bufferViews", [])
	for i in view_defs.size():
		var bv: Dictionary = view_defs[i]
		var ext = bv.get("extensions", {}).get("EXT_meshopt_compression")
		var data: PackedByteArray
		if ext != null:
			data = _decode_meshopt(ext, buffers)
			if data.is_empty() and int(ext["count"]) > 0:
				return {"error": "Could not decode EXT_meshopt_compression data in bufferView %d." % i}
			bv["extensions"].erase("EXT_meshopt_compression")
			if bv["extensions"].is_empty():
				bv.erase("extensions")
			if str(ext.get("mode", "ATTRIBUTES")) == "ATTRIBUTES":
				bv["byteStride"] = int(ext["byteStride"])
		else:
			var src: PackedByteArray = buffers[int(bv.get("buffer", 0))]
			var off := int(bv.get("byteOffset", 0))
			data = src.slice(off, off + int(bv["byteLength"]))
		views.append(data)
	_remove_extension(gltf, "EXT_meshopt_compression")
	_dequantize(gltf, views)

	var uv_stats := []
	if repair_uvs:
		for mesh in gltf.get("meshes", []):
			for prim in mesh.get("primitives", []):
				var st = _repair_primitive(gltf, views, prim)
				if st != null:
					uv_stats.append(st)

	return {"glb": _write_glb(gltf, views), "uv_stats": uv_stats}


static func _decode_meshopt(ext: Dictionary, buffers: Array) -> PackedByteArray:
	var src: PackedByteArray = buffers[int(ext.get("buffer", 0))]
	var off := int(ext.get("byteOffset", 0))
	var data := src.slice(off, off + int(ext["byteLength"]))
	var count := int(ext["count"])
	var stride := int(ext["byteStride"])
	var mode := str(ext.get("mode", "ATTRIBUTES"))
	var filter := str(ext.get("filter", "NONE"))
	if mode == "ATTRIBUTES":
		var out := Meshopt.decode_vertex_buffer(data, count, stride)
		if filter == "OCTAHEDRAL":
			Meshopt.decode_filter_oct(out, count, stride)
		elif filter == "QUATERNION":
			Meshopt.decode_filter_quat(out, count, stride)
		elif filter == "EXPONENTIAL":
			Meshopt.decode_filter_exp(out, count, stride)
		return out
	var indices := Meshopt.decode_index_buffer(data, count) if mode == "TRIANGLES" else Meshopt.decode_index_sequence(data, count)
	return Meshopt.indices_to_bytes(indices, stride)


static func _dequantize(gltf: Dictionary, views: Array) -> void:
	var done := {}
	for mesh in gltf.get("meshes", []):
		for prim in mesh.get("primitives", []):
			var groups := [prim.get("attributes", {})]
			groups.append_array(prim.get("targets", []))
			for attrs in groups:
				for name in attrs.keys():
					if not (name in ["POSITION", "NORMAL", "TANGENT"] or str(name).begins_with("TEXCOORD_")):
						continue
					var idx := int(attrs[name])
					var acc: Dictionary = gltf["accessors"][idx]
					if int(acc["componentType"]) == 5126:
						continue
					if not done.has(idx):
						done[idx] = _add_float_accessor(gltf, views, read_accessor(gltf, views, idx), str(acc["type"]),
							name == "POSITION")
					attrs[name] = done[idx]
	_remove_extension(gltf, "KHR_mesh_quantization")


static func _repair_primitive(gltf: Dictionary, views: Array, prim: Dictionary):
	if int(prim.get("mode", 4)) != 4:
		return null
	var attrs: Dictionary = prim.get("attributes", {})
	if not attrs.has("POSITION"):
		return null
	var positions := read_accessor(gltf, views, int(attrs["POSITION"]))
	var indices := PackedInt64Array()
	if prim.has("indices"):
		for v in read_accessor(gltf, views, int(prim["indices"])):
			indices.append(int(v))
	else:
		for i in positions.size():
			indices.append(i)
	var uvs := read_accessor(gltf, views, int(attrs["TEXCOORD_0"])) if attrs.has("TEXCOORD_0") else []
	var normals := read_accessor(gltf, views, int(attrs["NORMAL"])) if attrs.has("NORMAL") else []
	var result: Dictionary = UvRepair.repair_uvs(positions, uvs, indices, normals)
	if result["uvs"] != null:
		attrs["TEXCOORD_0"] = _add_float_accessor(gltf, views, result["uvs"], "VEC2")
	return result["stats"]


## Returns an Array of Arrays (or of floats for SCALAR accessors), normalized per the accessor.
static func read_accessor(gltf: Dictionary, views: Array, index: int) -> Array:
	var acc: Dictionary = gltf["accessors"][index]
	var count := int(acc["count"])
	var n: int = TYPE_SIZES[str(acc["type"])]
	var ctype := int(acc["componentType"])
	var csize: int = COMPONENTS[ctype][0]
	var norm: float = COMPONENTS[ctype][1] if acc.get("normalized", false) else 0.0
	var out := []
	out.resize(count)
	if not acc.has("bufferView"):
		for i in count:
			out[i] = 0.0 if n == 1 else _zeros(n)
		return out
	var bv: Dictionary = gltf["bufferViews"][int(acc["bufferView"])]
	var data: PackedByteArray = views[int(acc["bufferView"])]
	var stride := int(bv.get("byteStride", n * csize))
	var base := int(acc.get("byteOffset", 0))
	for i in count:
		var comps := []
		for c in n:
			var o := base + i * stride + c * csize
			var v: float
			match ctype:
				5120: v = data.decode_s8(o)
				5121: v = data.decode_u8(o)
				5122: v = data.decode_s16(o)
				5123: v = data.decode_u16(o)
				5125: v = data.decode_u32(o)
				_: v = data.decode_float(o)
			if norm > 0.0:
				v = max(v / norm, -1.0)
			comps.append(v)
		out[i] = comps[0] if n == 1 else comps
	return out


static func _zeros(n: int) -> Array:
	var a := []
	a.resize(n)
	a.fill(0.0)
	return a


static func _add_float_accessor(gltf: Dictionary, views: Array, values: Array, type_name: String, bounds := false) -> int:
	var n: int = TYPE_SIZES[type_name]
	var data := PackedByteArray()
	data.resize(values.size() * n * 4)
	var o := 0
	for v in values:
		for c in n:
			data.encode_float(o, v[c])
			o += 4
	gltf["bufferViews"].append({"buffer": 0, "byteLength": data.size(), "target": 34962})
	views.append(data)
	var acc := {"bufferView": gltf["bufferViews"].size() - 1, "componentType": 5126,
		"count": values.size(), "type": type_name}
	if bounds and not values.is_empty():
		var mn: Array = (values[0] as Array).duplicate()
		var mx: Array = (values[0] as Array).duplicate()
		for v in values:
			for c in n:
				mn[c] = min(mn[c], v[c])
				mx[c] = max(mx[c], v[c])
		acc["min"] = mn
		acc["max"] = mx
	gltf["accessors"].append(acc)
	return gltf["accessors"].size() - 1


static func _remove_extension(gltf: Dictionary, name: String) -> void:
	for key in ["extensionsUsed", "extensionsRequired"]:
		if gltf.has(key):
			gltf[key].erase(name)
			if gltf[key].is_empty():
				gltf.erase(key)


## JSON.parse_string turns every number into a float; write whole numbers back as
## integers so the GLB stays valid for strict glTF readers too.
static func _intify(v):
	if v is Dictionary:
		var d := {}
		for k in v:
			d[k] = _intify(v[k])
		return d
	if v is Array:
		var a := []
		for x in v:
			a.append(_intify(x))
		return a
	if v is float and is_finite(v) and v == floor(v) and absf(v) < 9.0e15:
		return int(v)
	return v


static func _read_chunks(glb: PackedByteArray) -> Dictionary:
	if glb.size() < 12 or glb.slice(0, 4).get_string_from_ascii() != "glTF":
		return {"error": "Not a GLB (missing glTF magic)."}
	var json_text := ""
	var bin := PackedByteArray()
	var have_bin := false
	var offset := 12
	while offset + 8 <= glb.size():
		var length := glb.decode_u32(offset)
		var ctype := glb.decode_u32(offset + 4)
		var start := offset + 8
		if start + length > glb.size():
			break
		if ctype == 0x4E4F534A:  # "JSON"
			json_text = glb.slice(start, start + length).get_string_from_utf8()
		elif ctype == 0x004E4942 and not have_bin:  # "BIN\0"
			bin = glb.slice(start, start + length)
			have_bin = true
		offset = start + length
	var parsed = JSON.parse_string(json_text)
	if not (parsed is Dictionary):
		return {"error": "The GLB JSON chunk could not be parsed."}
	return {"json": parsed, "bin": bin}


static func _write_glb(gltf: Dictionary, views: Array) -> PackedByteArray:
	var blob := PackedByteArray()
	var view_defs: Array = gltf.get("bufferViews", [])
	for i in view_defs.size():
		while blob.size() % 4 != 0:
			blob.append(0)
		view_defs[i]["buffer"] = 0
		view_defs[i]["byteOffset"] = blob.size()
		view_defs[i]["byteLength"] = (views[i] as PackedByteArray).size()
		blob.append_array(views[i])
	while blob.size() % 4 != 0:
		blob.append(0)
	gltf["buffers"] = [{"byteLength": blob.size()}]
	var js := JSON.stringify(_intify(gltf)).to_utf8_buffer()
	while js.size() % 4 != 0:
		js.append(0x20)
	var out := PackedByteArray()
	out.resize(12)
	out.encode_u32(0, 0x46546C67)  # "glTF"
	out.encode_u32(4, 2)
	var header := PackedByteArray()
	header.resize(8)
	header.encode_u32(0, js.size())
	header.encode_u32(4, 0x4E4F534A)
	out.append_array(header)
	out.append_array(js)
	header.encode_u32(0, blob.size())
	header.encode_u32(4, 0x004E4942)
	out.append_array(header)
	out.append_array(blob)
	out.encode_u32(8, out.size())
	return out
