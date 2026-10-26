extends SceneTree

func _initialize() -> void:
	var codec = load("res://addons/capsule/feature_codec.gd")
	if codec == null:
		quit(1)
		return
	# Golden bytes originate from the TypeScript codec, including ordered records and Unicode.
	var vectors = JSON.parse_string(FileAccess.get_file_as_string("res://tests/codec-vectors.json"))
	for vector in vectors:
		var encoded: Dictionary = codec.encode(vector.schema, vector.value)
		if not encoded.ok or encoded.bytes.hex_encode() != vector.hex:
			_fail("Golden encoding")
			return
		var decoded: Dictionary = codec.decode(vector.schema, str(vector.hex).hex_decode())
		# JSON fixtures represent numbers as floats; normalize the decoded integer values.
		if not decoded.ok or JSON.parse_string(JSON.stringify(decoded.value)) != vector.value:
			_fail("Golden decoding")
			return
	for pair in [
		[{"kind": "bool"}, PackedByteArray([2])],
		[{"kind": "bool"}, PackedByteArray([1, 0])],
		[{"kind": "u32"}, PackedByteArray([1, 0, 0])],
		[{"kind": "text", "maxBytes": 8}, PackedByteArray([2, 0, 0, 0, 192, 128])],
		[{"kind": "text", "maxBytes": 8}, PackedByteArray([3, 0, 0, 0, 237, 160, 128])],
		[{"kind": "list", "maxCount": 2, "element": {"kind": "bool"}}, PackedByteArray([3, 0, 0, 0, 1, 1, 1])]
	]:
		if codec.decode(pair[0], pair[1]).ok:
			_fail("Malformed wire data accepted")
			return
	for value in [-1, 4294967296, 1.5, "1"]:
		if codec.encode({"kind": "u32"}, value).ok:
			_fail("Invalid uint32 accepted")
			return
	print("CAPSULE_FEATURE_CODEC_PASS: golden vector and 10 invalid inputs")
	quit(0)

func _fail(message: String) -> void:
	push_error(message)
	quit(1)
