extends SceneTree

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var resources := ProjectSettings.globalize_path("res://addons/capsule/bin")
	var provider = load("res://addons/capsule/feature_provider.gd").new()
	provider.definition = JSON.parse_string(FileAccess.get_file_as_string("res://provider.json"))
	provider.grants = PackedStringArray(["energy.read", "energy.add"])
	provider.publish_state("State", PackedByteArray([0, 0, 0, 0]))
	var publication := FileAccess.get_file_as_string("res://theme-admission.json")
	for trusted in [false, true]:
		var runtime = ClassDB.instantiate("CapsuleRuntime")
		if not runtime.open(resources.path_join("capsule_host.dll"), JSON.stringify({"resources": resources, "firstParty": trusted, "providers": [provider.descriptor()]})):
			_fail(runtime, "Native theme admission host unavailable")
			return
		var result = JSON.parse_string(runtime.exchange(1, publication))
		if not trusted:
			if not result.get("error"):
				_fail(runtime, "Community theme quota bypassed")
				return
		else:
			var deadline := Time.get_ticks_msec() + 10000
			while not result.has("nodes") and not result.get("error") and Time.get_ticks_msec() < deadline:
				await process_frame
				result = JSON.parse_string(runtime.exchange(2, "{}"))
			if result.get("error") or not result.has("nodes"):
				_fail(runtime, "Trusted extended theme did not produce a snapshot: " + str(result.get("error", "timeout")))
				return
		runtime.close()
	print("CAPSULE_THEME_ADMISSION_PASS: community refusal and trusted extended text catalog")
	quit(0)

func _fail(runtime: RefCounted, message: String) -> void:
	runtime.close()
	push_error(message)
	quit(1)
