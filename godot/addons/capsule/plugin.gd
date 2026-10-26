@tool
extends EditorPlugin

const Host = preload("res://addons/capsule/development_host.gd")
var dialog: ConfirmationDialog
var endpoint: LineEdit

func _enter_tree() -> void:
	add_custom_type("CapsuleDevelopmentHost", "Control", Host, null)
	add_tool_menu_item("Capsule: Connect React UI", _open_setup)
	dialog = ConfirmationDialog.new()
	dialog.title = "Capsule React UI"
	dialog.ok_button_text = "Save connection"
	endpoint = LineEdit.new()
	endpoint.placeholder_text = "http://127.0.0.1:5173"
	endpoint.custom_minimum_size = Vector2(420, 36)
	dialog.add_child(endpoint)
	dialog.confirmed.connect(_save_connection)
	get_editor_interface().get_base_control().add_child(dialog)

func _open_setup() -> void:
	endpoint.text = ProjectSettings.get_setting("capsule/development/endpoint", "http://127.0.0.1:5173")
	dialog.popup_centered()

func _save_connection() -> void:
	if not Host.valid_endpoint(endpoint.text):
		push_error("Capsule requires an explicit loopback HTTP endpoint.")
		return
	ProjectSettings.set_setting("capsule/development/endpoint", endpoint.text)
	ProjectSettings.save()

func _exit_tree() -> void:
	remove_tool_menu_item("Capsule: Connect React UI")
	remove_custom_type("CapsuleDevelopmentHost")
	if is_instance_valid(dialog):
		dialog.queue_free()
