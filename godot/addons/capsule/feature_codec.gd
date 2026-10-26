extends RefCounted
## The shared five-kind feature wire format. Record field order is wire order.

var _stream := StreamPeerBuffer.new()
var _error := ""
var _visits := 0
const MAX_BYTES := 4194304

static func encode(schema: Dictionary, value: Variant) -> Dictionary:
	var codec = new()
	codec._stream.big_endian = false
	codec._write(schema, value, 0)
	return {"ok": codec._error.is_empty(), "bytes": codec._stream.data_array if codec._error.is_empty() else PackedByteArray(), "error": codec._error}

static func decode(schema: Dictionary, payload: PackedByteArray) -> Dictionary:
	var codec = new()
	codec._stream.big_endian = false
	codec._stream.data_array = payload
	if payload.size() > MAX_BYTES:
		return {"ok": false, "value": null, "error": "Payload quota."}
	var value = codec._read(schema, 0)
	if codec._stream.get_position() != payload.size():
		codec._error = "Trailing or truncated feature data."
	return {"ok": codec._error.is_empty(), "value": value if codec._error.is_empty() else null, "error": codec._error}

func _visit(depth: int) -> bool:
	_visits += 1
	if depth > 32 or _visits > 100000 or _stream.get_size() > MAX_BYTES:
		_error = "Feature data quota."
	return _error.is_empty()

func _write(schema: Dictionary, value: Variant, depth: int) -> void:
	if not _visit(depth):
		return
	match schema.get("kind", ""):
		"bool":
			if typeof(value) != TYPE_BOOL: _error = "Expected bool."
			else: _stream.put_u8(1 if value else 0)
		"u32":
			if (typeof(value) != TYPE_INT and typeof(value) != TYPE_FLOAT) or not is_finite(float(value)) or value < 0 or value > 4294967295 or float(value) != floor(float(value)):
				_error = "Expected uint32."
			else: _stream.put_u32(int(value))
		"text":
			if not value is String: _error = "Expected text."
			else:
				var bytes: PackedByteArray = value.to_utf8_buffer()
				if bytes.size() > int(schema.maxBytes): _error = "Text bound exceeded."
				else:
					_stream.put_u32(bytes.size())
					_stream.put_data(bytes)
		"list":
			if not value is Array or value.size() > int(schema.maxCount): _error = "List bound exceeded."
			else:
				_stream.put_u32(value.size())
				for item in value: _write(schema.element, item, depth + 1)
		"record":
			if not value is Dictionary: _error = "Expected record."
			else:
				for field in schema.fields:
					if not value.has(field): _error = "Missing record field."
					else: _write(schema.fields[field], value[field], depth + 1)
		_: _error = "Unknown feature schema."
	if _stream.get_size() > MAX_BYTES: _error = "Payload quota."

func _available(count: int) -> bool:
	if count < 0 or _stream.get_available_bytes() < count:
		_error = "Truncated feature data."
	return _error.is_empty()

func _read(schema: Dictionary, depth: int) -> Variant:
	if not _visit(depth): return null
	match schema.get("kind", ""):
		"bool":
			if not _available(1): return null
			var flag := _stream.get_u8()
			if flag > 1: _error = "Invalid bool."
			return flag == 1
		"u32":
			return _stream.get_u32() if _available(4) else null
		"text":
			if not _available(4): return null
			var length := _stream.get_u32()
			if length > int(schema.maxBytes): _error = "Text bound exceeded."
			if not _available(length): return null
			var bytes: PackedByteArray = _stream.get_data(length)[1]
			if not _utf8(bytes):
				_error = "Invalid UTF-8."
				return null
			return bytes.get_string_from_utf8()
		"list":
			if not _available(4): return null
			var count := _stream.get_u32()
			if count > int(schema.maxCount) or count > 100000:
				_error = "List bound exceeded."
				return null
			var values: Array = []
			for index in count:
				values.append(_read(schema.element, depth + 1))
				if not _error.is_empty(): return null
			return values
		"record":
			var value: Dictionary = {}
			for field in schema.fields:
				value[field] = _read(schema.fields[field], depth + 1)
				if not _error.is_empty(): return null
			return value
		_: _error = "Unknown feature schema."
	return null

static func _utf8(bytes: PackedByteArray) -> bool:
	var offset := 0
	while offset < bytes.size():
		var first := int(bytes[offset])
		offset += 1
		if first < 128: continue
		var remaining := 1 if first >= 194 and first <= 223 else 2 if first >= 224 and first <= 239 else 3 if first >= 240 and first <= 244 else -1
		if remaining < 0 or offset + remaining > bytes.size(): return false
		var point := first & (31 if remaining == 1 else 15 if remaining == 2 else 7)
		for index in remaining:
			var next := int(bytes[offset])
			offset += 1
			if next < 128 or next > 191: return false
			point = (point << 6) | (next & 63)
		if point < (128 if remaining == 1 else 2048 if remaining == 2 else 65536) or point > 1114111 or (point >= 55296 and point <= 57343): return false
	return true
