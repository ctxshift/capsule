extends SceneTree

func _initialize() -> void:
	var provider = load("res://addons/capsule/feature_provider.gd").new()
	provider.definition = JSON.parse_string(FileAccess.get_file_as_string("res://provider.json"))
	provider.grants = PackedStringArray(["energy.read", "energy.add"])
	provider.publish_state("State", PackedByteArray([1, 0, 0, 0]))
	var descriptor: Dictionary = provider.descriptor()
	if descriptor.states.State.has("schema") or descriptor.requests.Add.has("input") or not provider.definition.states.State.has("schema") or descriptor.states.State.initial != "AQAAAA==" or descriptor.fingerprint != provider.definition.fingerprint:
		push_error("Native provider descriptor must retain identity/state bytes without copying game schemas.")
		quit(1)
		return
	descriptor.grants.clear()
	if provider.grants.size() != 2:
		push_error("Descriptor mutation affected provider grants.")
		quit(1)
		return
	print("CAPSULE_PROVIDER_DESCRIPTOR_PASS: compact native metadata and independent game schemas")
	quit(0)
