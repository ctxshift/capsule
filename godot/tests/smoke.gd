extends SceneTree

func _initialize() -> void:
	var host_script = load("res://addons/capsule/development_host.gd")
	for endpoint in ["http://127.0.0.1:5173", "http://localhost:80", "http://[::1]:65535"]:
		assert(host_script.valid_endpoint(endpoint))
	for endpoint in ["https://localhost", "http://example.invalid", "http://localhost:0", "http://localhost:65536", "http://localhost/path", "http://user@localhost"]:
		assert(not host_script.valid_endpoint(endpoint))
	if not ClassDB.class_exists("CapsuleRuntime"):
		push_error("CapsuleRuntime was not registered.")
		quit(1)
		return
	var runtime = ClassDB.instantiate("CapsuleRuntime")
	var resources := ProjectSettings.globalize_path("res://addons/capsule/bin")
	if not runtime.open(resources.path_join("capsule_host.dll"), JSON.stringify({"resources": resources, "demo": true})):
		push_error(runtime.exchange(0, "{}"))
		quit(1)
		return
	print("CAPSULE_NATIVE_HOST_READY " + runtime.exchange(0, "{}"))
	runtime.close()
	quit(0)
