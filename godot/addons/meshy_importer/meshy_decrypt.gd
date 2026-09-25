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


## Returns {"glb": PackedByteArray} or {"error": String}.
static func decode(data: PackedByteArray) -> Dictionary:
	if data.size() >= 4 and data.slice(0, 4).get_string_from_ascii() == "glTF":
		return {"error": "This is a plain GLB, not a .meshy payload. Import it as a .glb instead."}
	if data.size() < HEADER_SIZE + ENCRYPTED_SIZE + TAG_SIZE or data.slice(0, 8).get_string_from_ascii() != MAGIC:
		return {"error": "Not a valid Meshy .meshy file: missing MESHY.AI header."}
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
		return {"error": "Meshy decryption produced an invalid GLB header. The .meshy encryption format may have changed."}
	glb.encode_u32(8, glb.size())
	return {"glb": glb}
