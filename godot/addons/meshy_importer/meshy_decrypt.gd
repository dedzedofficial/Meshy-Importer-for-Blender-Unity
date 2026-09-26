@tool
extends RefCounted
## .meshy container -> GLB bytes (see core/python/meshy_core/decode.py for the layout).
## AES-256-CTR is built from Godot's AESContext in ECB mode: the keystream is the
## encryption of the counter blocks nonce || uint32be(2 + i).

const MAGIC := "MESHY.AI"
const KEY := "JSON{\"accessors\":[{\"bufferView\":"
const HEADER_SIZE := 32
const ENCRYPTED_SIZE := 8192
const TAG_SIZE := 16


const TROUBLESHOOTING := "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/blob/main/TROUBLESHOOTING.md"
const HELP_WRONG_FILE := TROUBLESHOOTING + "#wrong-file-errors"
const HELP_FORMAT_CHANGED := TROUBLESHOOTING + "#meshy-changed-its-web-format"


static func _starts_with(data: PackedByteArray, offset: int, ascii: String) -> bool:
	if data.size() < offset + ascii.length():
		return false
	for i in ascii.length():
		if data[offset + i] != ascii.unicode_at(i):
			return false
	return true


## One plain sentence explaining why `data` is not a usable .meshy file, or "" when it
## looks like a complete container. Port of core/python/meshy_core/decode.py
## describe_wrong_file; keep the wording in sync.
static func describe_wrong_file(data: PackedByteArray) -> String:
	var min_size := HEADER_SIZE + ENCRYPTED_SIZE + TAG_SIZE
	if data.is_empty():
		return "The file is empty. The download probably failed; save the model response again."
	if _starts_with(data, 0, MAGIC):
		if data.size() < min_size:
			return "The .meshy file is cut off (only %d bytes). Save the complete model response again." % data.size()
		return ""
	var start := 0
	if data.size() >= 3 and data[0] == 0xEF and data[1] == 0xBB and data[2] == 0xBF:
		start = 3
	while start < data.size() and start < 512 and data[start] in [0x20, 0x09, 0x0D, 0x0A]:
		start += 1
	var head := data.slice(start, start + 64).get_string_from_ascii().to_lower()
	if _starts_with(data, 0, "glTF"):
		return "This is a plain GLB, not a .meshy payload. Import it as a .glb instead; it does not need the Meshy Importer."
	for tag in ["<!doctype", "<html", "<?xml", "<head", "<body"]:
		if head.begins_with(tag):
			return "This is a web page (HTML), not the model. In the Network tab, save the model request's response instead of the page."
	if head.begins_with("{") or head.begins_with("["):
		return "This is a JSON API response, not the model. Pick the request whose response starts with MESHY.AI."
	if _starts_with(data, 0, "Kaydara FBX Binary") or head.begins_with("; fbx"):
		return "This is an FBX file (Meshy's normal Download). Import it directly as .fbx; it does not need the Meshy Importer."
	if data.size() >= 4 and data[0] == 0x50 and data[1] == 0x4B and data[2] == 3 and data[3] == 4:
		return "This is a ZIP archive. Unzip it; if it holds .glb/.fbx/.obj files, import those directly."
	if (data.size() >= 8 and data[0] == 0x89 and _starts_with(data, 1, "PNG")) \
			or (data.size() >= 3 and data[0] == 0xFF and data[1] == 0xD8 and data[2] == 0xFF) \
			or (_starts_with(data, 0, "RIFF") and _starts_with(data, 8, "WEBP")):
		return "This is an image, not a model. Save the model request's response instead."
	for tag in ["# ", "mtllib", "o ", "v ", "g "]:
		if head.begins_with(tag):
			return "This is an OBJ file (Meshy's normal Download). Import it directly as .obj; it does not need the Meshy Importer."
	return "This is not a .meshy file: it does not start with MESHY.AI. Make sure you saved the model response, not another request."


## Returns {"glb": PackedByteArray} or {"error": String}.
static func decode(data: PackedByteArray) -> Dictionary:
	var problem := describe_wrong_file(data)
	if problem != "":
		return {"error": problem + " Help: " + HELP_WRONG_FILE}
	var nonce := data.slice(10, 22)
	var counters := PackedByteArray()
	counters.resize(ENCRYPTED_SIZE)
	for block in ENCRYPTED_SIZE / 16:
		var off := block * 16
		for k in 12:
			counters[off + k] = nonce[k]
		var ctr := 2 + block
		counters[off + 12] = (ctr >> 24) & 0xFF
		counters[off + 13] = (ctr >> 16) & 0xFF
		counters[off + 14] = (ctr >> 8) & 0xFF
		counters[off + 15] = ctr & 0xFF
	var aes := AESContext.new()
	aes.start(AESContext.MODE_ECB_ENCRYPT, KEY.to_ascii_buffer())
	var keystream := aes.update(counters)
	aes.finish()
	var glb := data.slice(HEADER_SIZE, HEADER_SIZE + ENCRYPTED_SIZE)
	for i in ENCRYPTED_SIZE:
		glb[i] = glb[i] ^ keystream[i]
	glb.append_array(data.slice(HEADER_SIZE + ENCRYPTED_SIZE + TAG_SIZE))
	if glb.slice(0, 4).get_string_from_ascii() != "glTF":
		return {"error": "Meshy decryption produced an invalid GLB header. Meshy may have changed the .meshy format; update the importer or report the file. Help: " + HELP_FORMAT_CHANGED}
	glb.encode_u32(8, glb.size())
	return {"glb": glb}
