@tool
extends RefCounted
## EXT_meshopt_compression decoder (meshoptimizer bitstream, MIT licensed).
##
## GDScript port of core/python/meshy_core/meshopt.py / unity/Editor/MeshyMeshopt.cs.
## Supports vertex codec v0 and index codecs v0/v1 -- everything the extension emits.
## Filters are evaluated in double precision, so results can differ from the float32
## reference by at most one unit in the last place of the decoded normal/quaternion.

const BYTE_GROUP_SIZE := 16
const BYTE_GROUP_DECODE_LIMIT := 24
const TAIL_MIN_SIZE_V0 := 32
const BITS_V0 := [0, 2, 4, 8]


static func _unzigzag8(v: int) -> int:
	return ((~(v >> 1)) & 0xFF) if (v & 1) else (v >> 1)


## Decodes an ATTRIBUTES stream. Returns the raw vertex bytes, or an empty array on error.
static func decode_vertex_buffer(buf: PackedByteArray, vertex_count: int, vertex_size: int) -> PackedByteArray:
	var end := buf.size()
	if vertex_size <= 0 or vertex_size > 256 or vertex_size % 4 != 0 or end < 1:
		push_error("meshopt: invalid vertex buffer")
		return PackedByteArray()
	if (buf[0] & 0xF0) != 0xA0 or (buf[0] & 0x0F) != 0:
		push_error("meshopt: unsupported vertex header 0x%02x" % buf[0])
		return PackedByteArray()
	var tail_pad: int = max(vertex_size, TAIL_MIN_SIZE_V0)
	if end - 1 < tail_pad:
		push_error("meshopt: vertex buffer truncated")
		return PackedByteArray()
	var last_vertex := buf.slice(end - vertex_size, end)
	var result := PackedByteArray()
	result.resize(vertex_count * vertex_size)
	var block_max: int = min((8192 / vertex_size) & ~(BYTE_GROUP_SIZE - 1), 256)
	var pos := 1
	var vertex_offset := 0
	while vertex_offset < vertex_count:
		var block: int = min(block_max, vertex_count - vertex_offset)
		var aligned := (block + BYTE_GROUP_SIZE - 1) & ~(BYTE_GROUP_SIZE - 1)
		for b in vertex_size:
			var plane := PackedByteArray()
			plane.resize(aligned)
			pos = _decode_bytes(buf, pos, end, plane, aligned)
			if pos < 0:
				return PackedByteArray()
			var p: int = last_vertex[b]
			var o := vertex_offset * vertex_size + b
			for i in block:
				p = (_unzigzag8(plane[i]) + p) & 0xFF
				result[o] = p
				o += vertex_size
			last_vertex[b] = p
		vertex_offset += block
	if end - pos != tail_pad:
		push_error("meshopt: vertex stream did not end at the tail boundary")
		return PackedByteArray()
	return result


static func _decode_bytes(data: PackedByteArray, pos: int, end: int, buffer: PackedByteArray, size: int) -> int:
	var header_size := (size / BYTE_GROUP_SIZE + 3) / 4
	if end - pos < header_size:
		push_error("meshopt: truncated group header")
		return -1
	var header := pos
	pos += header_size
	var i := 0
	while i < size:
		if end - pos < BYTE_GROUP_DECODE_LIMIT:
			push_error("meshopt: truncated byte group")
			return -1
		var ho := i / BYTE_GROUP_SIZE
		var bits: int = BITS_V0[(data[header + ho / 4] >> ((ho % 4) * 2)) & 3]
		if bits == 8:
			for k in 16:
				buffer[i + k] = data[pos + k]
			pos += 16
		elif bits == 2:
			var v := pos + 4
			var o := i
			for g in 4:
				var byte: int = data[pos + g]
				for s in [6, 4, 2, 0]:
					var enc: int = (byte >> s) & 3
					if enc == 3:
						buffer[o] = data[v]
						v += 1
					else:
						buffer[o] = enc
					o += 1
			pos = v
		elif bits == 4:
			var v := pos + 8
			var o := i
			for g in 8:
				var byte: int = data[pos + g]
				for s in [4, 0]:
					var enc: int = (byte >> s) & 15
					if enc == 15:
						buffer[o] = data[v]
						v += 1
					else:
						buffer[o] = enc
					o += 1
			pos = v
		i += BYTE_GROUP_SIZE
	return pos


static func _decode_vbyte(data: PackedByteArray, state: Array) -> int:
	# state = [pos]; returns the value and advances state[0]
	var pos: int = state[0]
	var lead: int = data[pos]
	pos += 1
	if lead < 128:
		state[0] = pos
		return lead
	var result := lead & 127
	var shift := 7
	for _k in 4:
		var group: int = data[pos]
		pos += 1
		result |= (group & 127) << shift
		shift += 7
		if group < 128:
			break
	state[0] = pos
	return result & 0xFFFFFFFF


static func _decode_index(data: PackedByteArray, state: Array, last: int) -> int:
	var v := _decode_vbyte(data, state)
	var d := (v >> 1) ^ -(v & 1)
	return (last + d) & 0xFFFFFFFF


## Decodes a TRIANGLES stream. Returns the indices, or an empty array on error.
static func decode_index_buffer(buf: PackedByteArray, index_count: int) -> PackedInt64Array:
	var out := PackedInt64Array()
	if index_count % 3 != 0 or buf.size() < 1 + index_count / 3 + 16 or (buf[0] & 0xF0) != 0xE0 or (buf[0] & 0x0F) > 1:
		push_error("meshopt: invalid triangle stream")
		return out
	var version: int = buf[0] & 0x0F
	var edge_a := PackedInt64Array(); edge_a.resize(16)
	var edge_b := PackedInt64Array(); edge_b.resize(16)
	var vfifo := PackedInt64Array(); vfifo.resize(16)
	var eo := 0
	var vo := 0
	var nxt := 0
	var last := 0
	var fecmax := 13 if version >= 1 else 15
	var code := 1
	var code_end := code + index_count / 3
	var st := [code_end]
	var safe_end := buf.size() - 16
	var aux_table := safe_end
	out.resize(index_count)
	var w := 0
	while code < code_end:
		var codetri: int = buf[code]
		code += 1
		if codetri < 0xF0:
			var fe := codetri >> 4
			var a: int = edge_a[(eo - 1 - fe) & 15]
			var b: int = edge_b[(eo - 1 - fe) & 15]
			var fec := codetri & 15
			var c: int
			if fec < fecmax:
				var cf: int = vfifo[(vo - 1 - fec) & 15]
				c = nxt if fec == 0 else cf
				var fec0 := 1 if fec == 0 else 0
				nxt += fec0
				vfifo[vo] = c
				vo = (vo + fec0) & 15
			else:
				if st[0] > safe_end:
					push_error("meshopt: triangle stream truncated")
					return PackedInt64Array()
				if fec != 15:
					last = (last + (fec * 2 - 27)) & 0xFFFFFFFF
					c = last
				else:
					c = _decode_index(buf, st, last)
					last = c
				vfifo[vo] = c
				vo = (vo + 1) & 15
			edge_a[eo] = c; edge_b[eo] = b; eo = (eo + 1) & 15
			edge_a[eo] = a; edge_b[eo] = c; eo = (eo + 1) & 15
			out[w] = a; out[w + 1] = b; out[w + 2] = c
			w += 3
		elif codetri < 0xFE:
			var codeaux: int = buf[aux_table + (codetri & 15)]
			var feb := codeaux >> 4
			var fec := codeaux & 15
			var a := nxt
			nxt += 1
			var bf: int = vfifo[(vo - feb) & 15]
			var b: int = nxt if feb == 0 else bf
			var feb0 := 1 if feb == 0 else 0
			nxt += feb0
			var cf: int = vfifo[(vo - fec) & 15]
			var c: int = nxt if fec == 0 else cf
			var fec0 := 1 if fec == 0 else 0
			nxt += fec0
			out[w] = a; out[w + 1] = b; out[w + 2] = c
			w += 3
			vfifo[vo] = a; vo = (vo + 1) & 15
			vfifo[vo] = b; vo = (vo + feb0) & 15
			vfifo[vo] = c; vo = (vo + fec0) & 15
			edge_a[eo] = b; edge_b[eo] = a; eo = (eo + 1) & 15
			edge_a[eo] = c; edge_b[eo] = b; eo = (eo + 1) & 15
			edge_a[eo] = a; edge_b[eo] = c; eo = (eo + 1) & 15
		else:
			if st[0] > safe_end:
				push_error("meshopt: triangle stream truncated")
				return PackedInt64Array()
			var codeaux: int = buf[st[0]]
			st[0] += 1
			var fea := 0 if codetri == 0xFE else 15
			var feb := codeaux >> 4
			var fec := codeaux & 15
			if codeaux == 0:
				nxt = 0
			var a := 0
			if fea == 0:
				a = nxt
				nxt += 1
			var b: int
			if feb == 0:
				b = nxt
				nxt += 1
			else:
				b = vfifo[(vo - feb) & 15]
			var c: int
			if fec == 0:
				c = nxt
				nxt += 1
			else:
				c = vfifo[(vo - fec) & 15]
			if fea == 15:
				a = _decode_index(buf, st, last)
				last = a
			if feb == 15:
				b = _decode_index(buf, st, last)
				last = b
			if fec == 15:
				c = _decode_index(buf, st, last)
				last = c
			out[w] = a; out[w + 1] = b; out[w + 2] = c
			w += 3
			vfifo[vo] = a; vo = (vo + 1) & 15
			vfifo[vo] = b; vo = (vo + (1 if feb == 0 or feb == 15 else 0)) & 15
			vfifo[vo] = c; vo = (vo + (1 if fec == 0 or fec == 15 else 0)) & 15
			edge_a[eo] = b; edge_b[eo] = a; eo = (eo + 1) & 15
			edge_a[eo] = c; edge_b[eo] = b; eo = (eo + 1) & 15
			edge_a[eo] = a; edge_b[eo] = c; eo = (eo + 1) & 15
	if st[0] != safe_end:
		push_error("meshopt: triangle stream left unread data")
		return PackedInt64Array()
	return out


## Decodes an INDICES stream. Returns the indices, or an empty array on error.
static func decode_index_sequence(buf: PackedByteArray, index_count: int) -> PackedInt64Array:
	var out := PackedInt64Array()
	if buf.size() < 1 + index_count + 4 or (buf[0] & 0xF0) != 0xD0 or (buf[0] & 0x0F) > 1:
		push_error("meshopt: invalid index sequence")
		return out
	var st := [1]
	var safe_end := buf.size() - 4
	var last := [0, 0]
	out.resize(index_count)
	for i in index_count:
		if st[0] >= safe_end:
			push_error("meshopt: index sequence truncated")
			return PackedInt64Array()
		var v := _decode_vbyte(buf, st)
		var current := v & 1
		v >>= 1
		var d := (v >> 1) ^ -(v & 1)
		var index: int = (last[current] + d) & 0xFFFFFFFF
		last[current] = index
		out[i] = index
	if st[0] != safe_end:
		push_error("meshopt: index sequence left unread data")
		return PackedInt64Array()
	return out


static func indices_to_bytes(indices: PackedInt64Array, stride: int) -> PackedByteArray:
	var out := PackedByteArray()
	out.resize(indices.size() * stride)
	for i in indices.size():
		if stride == 2:
			out.encode_u16(i * 2, indices[i] & 0xFFFF)
		else:
			out.encode_u32(i * 4, indices[i])
	return out


static func _round_away(v: float) -> int:
	return int(v + (0.5 if v >= 0.0 else -0.5))


static func _wrap(v: int, bits: int) -> int:
	var m := 1 << bits
	v &= m - 1
	return v - m if v >= (m >> 1) else v


static func decode_filter_oct(buf: PackedByteArray, count: int, stride: int) -> void:
	var mx := 127.0 if stride == 4 else 32767.0
	for i in count:
		var off := i * stride
		var x: float
		var y: float
		var zi: float
		if stride == 4:
			x = buf.decode_s8(off)
			y = buf.decode_s8(off + 1)
			zi = buf.decode_s8(off + 2)
		else:
			x = buf.decode_s16(off)
			y = buf.decode_s16(off + 2)
			zi = buf.decode_s16(off + 4)
		var z := zi - absf(x) - absf(y)
		var t := 0.0 if z >= 0.0 else z
		x += t if x >= 0.0 else -t
		y += t if y >= 0.0 else -t
		var l := sqrt(x * x + y * y + z * z)
		var s := mx / l if l > 0.0 else 0.0
		if stride == 4:
			buf.encode_s8(off, _wrap(_round_away(x * s), 8))
			buf.encode_s8(off + 1, _wrap(_round_away(y * s), 8))
			buf.encode_s8(off + 2, _wrap(_round_away(z * s), 8))
		else:
			buf.encode_s16(off, _wrap(_round_away(x * s), 16))
			buf.encode_s16(off + 2, _wrap(_round_away(y * s), 16))
			buf.encode_s16(off + 4, _wrap(_round_away(z * s), 16))


static func decode_filter_quat(buf: PackedByteArray, count: int, stride: int) -> void:
	var scale := 32767.0 / sqrt(2.0)
	for i in count:
		var off := i * 8
		var d3 := buf.decode_s16(off + 6)
		var s := float(d3 | 3)
		var x := float(buf.decode_s16(off))
		var y := float(buf.decode_s16(off + 2))
		var z := float(buf.decode_s16(off + 4))
		var ww := s * s * 2.0 - x * x - y * y - z * z
		var w := sqrt(ww if ww >= 0.0 else 0.0)
		var ss := scale / s
		var qc := d3 & 3
		var vals := [0, 0, 0, 0]
		vals[(qc + 1) & 3] = _wrap(_round_away(x * ss), 16)
		vals[(qc + 2) & 3] = _wrap(_round_away(y * ss), 16)
		vals[(qc + 3) & 3] = _wrap(_round_away(z * ss), 16)
		vals[qc & 3] = _wrap(int(w * ss + 0.5), 16)
		for k in 4:
			buf.encode_s16(off + 2 * k, vals[k])


static func decode_filter_exp(buf: PackedByteArray, count: int, stride: int) -> void:
	var n := count * (stride / 4)
	for i in n:
		var v := buf.decode_u32(i * 4)
		var m := v & 0xFFFFFF
		if m & 0x800000:
			m -= 1 << 24
		var e := v >> 24
		if e & 0x80:
			e -= 256
		buf.encode_float(i * 4, float(m) * pow(2.0, e))
