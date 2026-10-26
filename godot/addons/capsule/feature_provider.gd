extends RefCounted
## Game-owned feature implementation. Descriptor and grants come from trusted composition.

signal request_received(token: String, operation: String, payload: PackedByteArray)
signal request_cancelled(token: String)

var definition: Dictionary = {}
var grants: PackedStringArray = []
var _states: Dictionary = {}
var _host: WeakRef

func bind(host: Control) -> void:
	_host = weakref(host)

func descriptor() -> Dictionary:
	# Schemas stay with the game codec; the native registry consumes identity and permissions.
	var value := {"name": definition.get("name", ""), "version": definition.get("version", ""), "fingerprint": definition.get("fingerprint", ""), "dependencies": definition.get("dependencies", {}).duplicate(), "grants": Array(grants), "states": {}, "requests": {}}
	for member in definition.get("states", {}):
		var state: Dictionary = definition.states[member]
		value.states[member] = {"permission": state.get("permission", ""), "delivery": state.get("delivery", "latest"), "initial": Marshalls.raw_to_base64(_states.get(member, PackedByteArray()))}
	for member in definition.get("requests", {}):
		value.requests[member] = {"permission": definition.requests[member].get("permission", "")}
	return value

func publish_state(member: String, payload: PackedByteArray) -> bool:
	if not definition.get("states", {}).has(member) or payload.size() > 4194304:
		return false
	_states[member] = payload.duplicate()
	var host = _host.get_ref() if _host else null
	return host.publish_feature_state(str(definition.name), member, payload) if is_instance_valid(host) else true

func resolve_request(token: String, payload: PackedByteArray, rejected: bool = false) -> bool:
	var host = _host.get_ref() if _host else null
	return host.resolve_feature_request(token, payload, rejected) if is_instance_valid(host) else false
