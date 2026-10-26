extends Control
## Opt-in local authoring host. Release exports never connect to the workshop.

signal publication_activated(revision: String)
signal diagnostic_changed(message: String)

@export var endpoint: String = ""
@export var enable_demo_provider: bool = false
@export var first_party_profile: bool = false
@export var admitted_addon_id: String = ""
var active_revision: String = ""
var generation: String = "0"
var last_error: String = ""
var demo_requests: int = 0
var rendered_nodes: Array = []
var _runtime: RefCounted
var _http: HTTPRequest
var _fetching := false
var _next_poll := 0.0
var _candidate_revision := ""
var _candidate_images: Dictionary = {}
var _active_images: Dictionary = {}
var _active: Control
var _providers: Dictionary = {}
var _request_providers: Dictionary = {}
var _preparing := false
var _source_images: Dictionary = {}

func register_provider(provider: RefCounted) -> bool:
	if _runtime != null or not provider.definition is Dictionary or not provider.definition.has("name"):
		return false
	var key := str(provider.definition.name)
	if _providers.has(key):
		return false
	_providers[key] = provider
	provider.bind(self)
	return true

func publish_feature_state(feature: String, member: String, payload: PackedByteArray) -> bool:
	if _runtime == null:
		return true
	return _call(6, {"feature": feature, "member": member, "payload": Marshalls.raw_to_base64(payload)}).get("accepted", false)

func resolve_feature_request(token: String, payload: PackedByteArray, rejected: bool = false) -> bool:
	if _runtime == null or not _request_providers.has(token):
		return false
	_request_providers.erase(token)
	return _call(7, {"token": token, "payload": Marshalls.raw_to_base64(payload), "rejected": rejected}).get("accepted", false)

static func valid_endpoint(value: String) -> bool:
	var pattern := RegEx.new()
	pattern.compile("^http://(127\\.0\\.0\\.1|localhost|\\[::1\\])(:[0-9]{1,5})?/?$")
	var matched := pattern.search(value)
	if matched == null:
		return false
	var port := matched.get_string(2).trim_prefix(":")
	return port.is_empty() or (port.to_int() >= 1 and port.to_int() <= 65535)

static func _parse(text: String) -> Variant:
	var parser := JSON.new()
	return parser.data if parser.parse(text) == OK else null

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	if Engine.is_editor_hint() or not OS.is_debug_build():
		set_process(false)
		return
	if endpoint.is_empty():
		endpoint = ProjectSettings.get_setting("capsule/development/endpoint", "http://127.0.0.1:5173")
	if not valid_endpoint(endpoint):
		_error("Capsule requires a loopback HTTP endpoint.")
		set_process(false)
		return
	endpoint = endpoint.trim_suffix("/")
	if not ClassDB.class_exists("CapsuleRuntime"):
		_error("Capsule native extension is unavailable for this platform.")
		set_process(false)
		return
	_runtime = ClassDB.instantiate("CapsuleRuntime")
	var resources := ProjectSettings.globalize_path("res://addons/capsule/bin")
	var provider_descriptors: Array = []
	for provider in _providers.values():
		provider_descriptors.append(provider.descriptor())
	var configuration := {"resources": resources, "demo": enable_demo_provider, "providers": provider_descriptors, "firstParty": first_party_profile}
	if not admitted_addon_id.is_empty():
		configuration["addonId"] = admitted_addon_id
	if not _runtime.open(resources.path_join("capsule_host.dll"), JSON.stringify(configuration)):
		_error(str(_call(0).get("error", "Capsule runtime could not start.")))
		set_process(false)
		return
	_http = HTTPRequest.new()
	_http.timeout = 4.0
	_http.body_size_limit = 900000
	_http.max_redirects = 0
	add_child(_http)

func _process(delta: float) -> void:
	if not _runtime or not _http or _preparing or _fetching:
		return
	var result := _call(2)
	if result.get("error"):
		_error(str(result.error))
	demo_requests = int(result.get("requests", demo_requests))
	if result.has("nodes"):
		_present(result)
		return
	_next_poll -= delta
	if _next_poll <= 0 and not _fetching:
		_next_poll = 0.1
		_fetch_publication()

func _call(command: int, request: Dictionary = {}) -> Dictionary:
	return _call_json(command, JSON.stringify(request))

func _call_json(command: int, request: String) -> Dictionary:
	var response = _parse(_runtime.exchange(command, request))
	if not response is Dictionary:
		return {"error": "Invalid Capsule native response."}
	for token in response.get("cancelledRequests", []):
		var provider = _request_providers.get(str(token))
		_request_providers.erase(str(token))
		if is_instance_valid(provider):
			provider.request_cancelled.emit(str(token))
	for invocation in response.get("featureRequests", []):
		var provider = _providers.get(str(invocation.feature))
		if is_instance_valid(provider):
			_request_providers[str(invocation.token)] = provider
			provider.request_received.emit(str(invocation.token), str(invocation.operation), Marshalls.base64_to_raw(str(invocation.payload)))
	return response

func _download(path: String, limit: int) -> PackedByteArray:
	_http.body_size_limit = limit
	if _http.request(endpoint + path) != OK:
		return PackedByteArray()
	var response: Array = await _http.request_completed
	if response[0] != HTTPRequest.RESULT_SUCCESS or response[1] != 200:
		return PackedByteArray()
	return response[3]

func _fetch_publication() -> void:
	_fetching = true
	var bytes := await _download("/capsule/dev/v1/package", 900000)
	if not is_inside_tree():
		return
	var publication = _parse(bytes.get_string_from_utf8())
	if not publication is Dictionary:
		_error("Capsule workshop disconnected or publication unavailable.")
		_fetching = false
		return
	if publication.get("compiler", {}).get("success", true) == false:
		_error(str(publication.compiler.get("error", "Compilation failed.")))
		_fetching = false
		return
	var revision: String = str(publication.get("hash", ""))
	if revision == active_revision or revision == _candidate_revision:
		if revision == active_revision:
			_error("")
		_fetching = false
		return
	# Forward original JSON so integer contract fields do not become decimal tokens.
	var result := _call_json(1, bytes.get_string_from_utf8())
	if result.get("error"):
		_candidate_revision = revision
		_error(str(result.error))
	else:
		_candidate_revision = revision
		_candidate_images = {}
	_fetching = false

func _prepare_images(result: Dictionary) -> Dictionary:
	var images: Dictionary = (_candidate_images if result.candidate else _active_images).duplicate()
	var recipes: Dictionary = {}
	for recipe in result.theme.get("assets", []):
		recipes[str(recipe.key)] = recipe
	var sources: Dictionary = _source_images.get(str(result.revision), {})
	_source_images[str(result.revision)] = sources
	for node in result.nodes:
		if node.kind != "Image" or images.has(str(node.asset)):
			continue
		var recipe: Dictionary = recipes.get(str(node.asset), {})
		if recipe.is_empty():
			return {}
		var source: String = str(recipe.path)
		if not sources.has(source):
			var data := await _download("/portable-ui/art/" + str(result.revision).uri_encode() + "/" + str(recipe.get("image", recipe.key)).uri_encode(), 4194304)
			if data.size() < 24 or data.slice(0, 8).hex_encode() != "89504e470d0a1a0a":
				return {}
			# Bound decoded allocation before calling the PNG decoder.
			var width := (int(data[16]) << 24) | (int(data[17]) << 16) | (int(data[18]) << 8) | int(data[19])
			var height := (int(data[20]) << 24) | (int(data[21]) << 16) | (int(data[22]) << 8) | int(data[23])
			var decoded := width * height * 4
			var total := data.size()
			for cached in sources.values():
				decoded += int(cached.decoded)
				total += int(cached.encoded)
			if width <= 0 or height <= 0 or width > 8192 or height > 8192 or decoded > (268435456 if first_party_profile else 67108864) or total > (67108864 if first_party_profile else 16777216):
				return {}
			var image := Image.new()
			if image.load_png_from_buffer(data) != OK or image.get_width() != int(recipe.width) or image.get_height() != int(recipe.height):
				return {}
			sources[source] = {"texture": ImageTexture.create_from_image(image), "encoded": data.size(), "decoded": width * height * 4}
		var texture := AtlasTexture.new()
		texture.atlas = sources[source].texture
		var rect: Array = recipe.rect
		texture.region = Rect2(rect[0], rect[1], rect[2], rect[3])
		images[str(node.asset)] = texture
	return images

func _present(result: Dictionary) -> void:
	_preparing = true
	var images := await _prepare_images(result)
	if not is_inside_tree():
		return
	var candidate := Control.new()
	candidate.mouse_filter = Control.MOUSE_FILTER_IGNORE
	candidate.visible = false
	add_child(candidate)
	var controls: Dictionary = {}
	var prepared := true
	for node in result.nodes:
		var control: Control
		match node.kind:
			"Text":
				var label := Label.new()
				label.text = node.text
				for style in result.theme.text:
					if style.key == node.style:
						label.add_theme_font_size_override("font_size", int(style.size))
						label.add_theme_color_override("font_color", _color(style.color))
				control = label
			"Button":
				var button := Button.new()
				button.pressed.connect(_activate.bind(str(node.handle), str(result.generation)))
				button.disabled = (int(node.events) & 1) == 0
				control = button
			"Image":
				if not images.has(str(node.asset)):
					prepared = false
					break
				var picture := TextureRect.new()
				picture.texture = images[str(node.asset)]
				picture.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
				control = picture
			"Box", "ClipBox":
				control = Panel.new()
				control.clip_contents = node.kind == "ClipBox"
				var surface := StyleBoxFlat.new()
				surface.bg_color = Color.TRANSPARENT
				for style in result.theme.get("surfaces", []):
					if style.key == node.style:
						surface.bg_color = _color(style.color)
				control.add_theme_stylebox_override("panel", surface)
			_:
				prepared = false
				break
		if not prepared:
			break
		control.name = "Node_" + str(node.handle)
		control.mouse_filter = Control.MOUSE_FILTER_STOP if node.kind == "Button" else Control.MOUSE_FILTER_IGNORE
		var parent: Control = candidate if str(node.parent) == "0" else controls.get(str(node.parent))
		if parent == null:
			prepared = false
			break
		parent.add_child(control)
		control.position = Vector2(node.x, node.y)
		control.size = Vector2(node.width, node.height)
		control.visible = node.visible
		controls[str(node.handle)] = control
	var acknowledgment := _call(3, {"sequence": str(result.sequence), "accepted": prepared})
	if not prepared or acknowledgment.get("error"):
		candidate.queue_free()
		if result.candidate:
			_source_images.erase(str(result.revision))
		_error(str(acknowledgment.get("error", "Godot presentation rejected.")))
		_preparing = false
		return
	if is_instance_valid(_active):
		_active.get_parent().remove_child(_active)
		_active.queue_free()
	_active = candidate
	_active.visible = true
	generation = str(result.generation)
	rendered_nodes = result.nodes
	if result.candidate:
		_active_images = images
		_candidate_images = {}
		_source_images = {str(result.revision): _source_images.get(str(result.revision), {})}
		active_revision = str(result.revision)
		_candidate_revision = ""
		_call(5, {"width": int(get_viewport_rect().size.x), "height": int(get_viewport_rect().size.y)})
		publication_activated.emit(active_revision)
	else:
		_active_images = images
	_error("")
	_preparing = false

func _activate(handle: String, owner_generation: String) -> void:
	_call(4, {"handle": handle, "generation": owner_generation})

static func _color(channels: Array) -> Color:
	return Color(float(channels[0]) / 255, float(channels[1]) / 255, float(channels[2]) / 255, float(channels[3]) / 255)

func _error(message: String) -> void:
	if message != last_error:
		last_error = message
		diagnostic_changed.emit(message)

func _exit_tree() -> void:
	if _http:
		_http.cancel_request()
	if _runtime:
		_runtime.close()
		_runtime = null
