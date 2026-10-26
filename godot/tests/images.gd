extends SceneTree

class ImageHost:
	extends "res://addons/capsule/development_host.gd"
	var downloads := 0
	var last_path := ""
	var data: PackedByteArray
	func _download(_path: String, _limit: int) -> PackedByteArray:
		downloads += 1
		last_path = _path
		return data

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var host := ImageHost.new()
	var image := Image.create(2, 2, false, Image.FORMAT_RGBA8)
	image.fill(Color.RED)
	host.data = image.save_png_to_buffer()
	var first := {"key": "first", "image": "atlas", "path": "atlas.png", "width": 2, "height": 2, "rect": [0, 0, 1, 1]}
	var second := {"key": "second", "image": "atlas", "path": "atlas.png", "width": 2, "height": 2, "rect": [1, 1, 1, 1]}
	var unused := {"key": "unused", "path": "unused.png", "width": 2, "height": 2, "rect": [0, 0, 2, 2]}
	var result := {"candidate": true, "revision": "neutral", "theme": {"assets": [first, second, unused]}, "nodes": [{"kind": "Image", "asset": "first"}, {"kind": "Image", "asset": "second"}]}
	var prepared: Dictionary = await host._prepare_images(result)
	if prepared.size() != 2 or host.downloads != 1 or prepared.first.atlas != prepared.second.atlas or not host.last_path.ends_with("/atlas"):
		_fail(host, "Referenced-only preparation and shared atlas source")
		return
	prepared = await host._prepare_images(result)
	if prepared.size() != 2 or host.downloads != 1:
		_fail(host, "Source cache reuse")
		return
	var hasher := HashingContext.new()
	hasher.start(HashingContext.HASH_SHA256)
	hasher.update(host.data)
	var digest := hasher.finish().hex_encode()
	host._candidate_image_hashes = {"atlas": digest}
	result.revision = "verified"
	prepared = await host._prepare_images(result)
	if prepared.size() != 2 or host.downloads != 2:
		_fail(host, "Verified source was not prepared")
		return
	var texture: Texture2D = prepared.first.atlas
	result.revision = "next"
	prepared = await host._prepare_images(result)
	if prepared.size() != 2 or host.downloads != 2 or prepared.first.atlas != texture:
		_fail(host, "Unchanged content was downloaded again across revisions")
		return
	result.revision = "corrupt"
	host._candidate_image_hashes = {"atlas": "b".repeat(64)}
	prepared = await host._prepare_images(result)
	if not prepared.is_empty():
		_fail(host, "Image content identity mismatch accepted")
		return
	result.revision = "invalid"
	host._candidate_image_hashes = {}
	host.data[16] = 127 # Impossible width; reject before invoking the decoder.
	prepared = await host._prepare_images(result)
	if not prepared.is_empty():
		_fail(host, "Unbounded image dimensions accepted")
		return
	host.free()
	print("CAPSULE_IMAGE_PREPARATION_PASS: referenced-only, atlas cache and dimension bounds")
	quit(0)

func _fail(host: Control, message: String) -> void:
	host.free()
	push_error(message)
	quit(1)
