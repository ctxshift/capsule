extends SceneTree

var host: Control
var proof_dir := ""
var last_command := 0
var status_key := ""
var completed_command := 0
var started := Time.get_ticks_msec()
var first_texts: Array[String] = []
var latency_only := false

func _initialize() -> void:
	_run()

func _run() -> void:
	var endpoint := ""
	for argument in OS.get_cmdline_user_args():
		if argument == "--latency-only":
			latency_only = true
		if argument.begins_with("--endpoint="):
			endpoint = argument.trim_prefix("--endpoint=")
		if argument.begins_with("--proof-dir="):
			proof_dir = argument.trim_prefix("--proof-dir=")
	if endpoint.is_empty() or proof_dir.is_empty():
		push_error("Expected endpoint and proof directory.")
		quit(1)
		return
	host = load("res://addons/capsule/development_host.gd").new()
	host.endpoint = endpoint
	_configure_host()
	host.publication_activated.connect(_publication)
	root.size = Vector2i(800, 480)
	root.add_child(host)
	print("CAPSULE_GODOT_PID " + str(OS.get_process_id()))

func _configure_host() -> void:
	host.enable_demo_provider = true

func _request_count() -> int:
	return host.demo_requests

func _publication(_revision: String) -> void:
	if first_texts.is_empty():
		for node in host.rendered_nodes:
			if node.kind == "Text":
				first_texts.append(str(node.text))

func _process(_delta: float) -> bool:
	if not is_instance_valid(host):
		return false
	if Time.get_ticks_msec() - started > 120000:
		push_error("Godot development proof timed out.")
		quit(1)
		return false
	var texts: Array[String] = []
	var text_color: Array = []
	for label in host.find_children("*", "Label", true, false):
		texts.append(label.text)
		if text_color.is_empty():
			var color: Color = label.get_theme_color("font_color")
			text_color = [color.r8, color.g8, color.b8, color.a8]
	var image_color: Array = []
	if not latency_only:
		for picture in host.find_children("*", "TextureRect", true, false):
			var color: Color = picture.texture.atlas.get_image().get_pixel(0, 0)
			image_color = [color.r8, color.g8, color.b8, color.a8]
	var status := {"pid": OS.get_process_id(), "revision": host.active_revision, "generation": host.generation,
		"error": host.last_error, "requests": _request_count(), "texts": texts,
		"nodes": host.rendered_nodes.size(), "command": completed_command, "imageColor": image_color, "textColor": text_color, "firstTexts": first_texts, "presentation": host.presentation_metrics,
		"elapsedMs": Time.get_ticks_msec() - started, "startedAtTicksMs": started}
	var key := JSON.stringify([status.revision, status.generation, status.error, status.requests, status.texts, status.command, status.imageColor, status.textColor, status.presentation])
	if key != status_key:
		status_key = key
		var output := FileAccess.open(proof_dir.path_join("status.json"), FileAccess.WRITE)
		output.store_string(JSON.stringify(status))
		output.close()
	var command_path := proof_dir.path_join("command.json")
	if FileAccess.file_exists(command_path):
		var parser := JSON.new()
		if parser.parse(FileAccess.get_file_as_string(command_path)) != OK:
			return false
		var command = parser.data
		if command is Dictionary and int(command.get("sequence", 0)) > last_command:
			last_command = int(command.sequence)
			_command(command)
	return false

func _command(command: Dictionary) -> void:
	match command.action:
		"clear-image-cache":
			host._content_images.clear()
		"click":
			var buttons := host.find_children("*", "Button", true, false)
			if buttons.is_empty():
				push_error("No native Godot button.")
				quit(1)
				return
			var button: Button = buttons[0]
			var position := button.get_global_rect().get_center()
			var motion := InputEventMouseMotion.new()
			motion.position = position
			motion.global_position = position
			Input.parse_input_event(motion)
			for pressed in [true, false]:
				var event := InputEventMouseButton.new()
				event.position = position
				event.global_position = position
				event.button_index = MOUSE_BUTTON_LEFT
				event.pressed = pressed
				Input.parse_input_event(event)
				await process_frame
		"capture":
			await RenderingServer.frame_post_draw
			var image := root.get_texture().get_image()
			if image.is_empty() or image.save_png(proof_dir.path_join("capture.png")) != OK:
				push_error("Godot render capture failed.")
				quit(1)
				return
		"create-image":
			var image := Image.create(3, 3, false, Image.FORMAT_RGBA8)
			image.fill(Color(1, 0, 0, 1))
			if image.save_png(proof_dir.path_join("changed-image.png")) != OK:
				quit(1)
				return
		"exit":
			quit(0)
	completed_command = int(command.sequence)
