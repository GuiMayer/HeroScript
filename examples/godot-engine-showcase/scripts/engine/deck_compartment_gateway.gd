extends RefCounted
## REST adapter, independent from Godot views. Uncertain commits retain their command identity.
const Numbers = preload("res://scripts/engine/request_numbers.gd")
var _transport
var _commands
var run_id := ""
var pending: Dictionary = {}

func _init(transport, commands, id := "") -> void:
	_transport = transport; _commands = commands; run_id = id

func read() -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s/deck-compartments" % run_id.uri_encode())

func preview(snapshot: Dictionary, allocations: Array) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST, "/api/v1/runs/%s/deck-compartments/preview" % run_id.uri_encode(), {
		"expectedSequence": Numbers.integer_token(snapshot.runSequence), "expectedStep": Numbers.integer_token(snapshot.expectedStep), "allocations": allocations.duplicate(true)})

func apply(snapshot: Dictionary, allocations: Array) -> Dictionary:
	if pending.is_empty():
		pending = _commands.prepare_command("runs", run_id, "APPLY_DECK_COMPARTMENTS", {
			"actorId": snapshot.actorId, "allocations": allocations.duplicate(true)}, int(snapshot.runSequence), int(snapshot.expectedStep))
	var result: Dictionary = await _commands.send_prepared(pending)
	if result.get("ok", false) and (not result.get("data") is Dictionary or not typeof(result.data.get("sequence")) in [TYPE_INT, TYPE_FLOAT]):
		return {"ok": false, "status": 0, "error": "Invalid command receipt. Retry the same command to recover."}
	if result.get("ok", false) or int(result.get("status", 0)) in range(400, 500): pending = {}
	return result
