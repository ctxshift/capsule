extends "res://tests/development.gd"

var provider: RefCounted
var count := 0

func _configure_host() -> void:
	provider = load("res://addons/capsule/feature_provider.gd").new()
	provider.definition = JSON.parse_string(FileAccess.get_file_as_string("res://provider.json"))
	provider.grants = PackedStringArray(["energy.read", "energy.add"])
	provider.publish_state("State", PackedByteArray([0, 0, 0, 0]))
	provider.request_received.connect(_requested)
	assert(host.register_provider(provider))

func _request_count() -> int:
	return count

func _requested(token: String, operation: String, payload: PackedByteArray) -> void:
	var bytes := StreamPeerBuffer.new()
	bytes.data_array = payload
	bytes.big_endian = false
	if operation != "Add" or payload.size() != 4:
		provider.resolve_request(token, PackedByteArray(), true)
		return
	var amount := bytes.get_u32()
	if amount < 1 or amount > 10:
		provider.resolve_request(token, PackedByteArray(), true)
		return
	count += int(amount)
	var next := StreamPeerBuffer.new()
	next.big_endian = false
	next.put_u32(count)
	assert(provider.publish_state("State", next.data_array))
	assert(provider.resolve_request(token, PackedByteArray([1])))
