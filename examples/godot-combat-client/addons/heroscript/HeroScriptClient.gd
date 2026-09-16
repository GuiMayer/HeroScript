extends Node

signal snapshot_changed(snapshot: Dictionary)
signal receipt_started(receipt: Dictionary)
signal frame_ready(frame: Dictionary, frame_index: int, frame_count: int)
signal receipt_finished(receipt: Dictionary)
signal input_changed(can_input: bool, reason: String)
signal request_failed(code: int, message: String)

const DEFAULT_BASE_URL := "http://127.0.0.1:5260"

var base_url := DEFAULT_BASE_URL
var snapshot: Dictionary = {}
var last_receipt: Dictionary = {}
var timeline: Dictionary = {}
var playing_receipt := false
var pause_between_frames := false
var _frame_acknowledged := false

func _ready() -> void:
	var configured := OS.get_environment("HEROSCRIPT_BASE_URL").strip_edges()
	if not configured.is_empty():
		base_url = configured.trim_suffix("/")

func request_json(path: String, method: int = HTTPClient.METHOD_GET, payload: Dictionary = {}) -> Dictionary:
	var request := HTTPRequest.new()
	add_child(request)
	var body := "" if payload.is_empty() else JSON.stringify(payload)
	var start_error := request.request(base_url + path, ["Content-Type: application/json"], method, body)
	if start_error != OK:
		request.queue_free()
		return _failure(0, "Falha ao iniciar a requisição: %s" % start_error)

	var response: Array = await request.request_completed
	request.queue_free()
	var code: int = response[1]
	var parsed = JSON.parse_string(response[3].get_string_from_utf8())
	if code < 200 or code >= 300:
		var message := str(parsed)
		if parsed is Dictionary:
			message = str(parsed.get("detail", parsed.get("error", parsed)))
		return _failure(code, message, parsed if parsed is Dictionary else {})
	return parsed if parsed is Dictionary else {}

func launch_sandbox(seed: int = 20260909, attempt_key: String = "") -> bool:
	var revisions := await request_json("/api/v1/content/revisions?configName=default")
	if is_failure(revisions):
		return false
	if attempt_key.is_empty():
		attempt_key = "godot-combat-%d" % seed
	var result := await request_json("/api/v1/sandbox/runs", HTTPClient.METHOD_POST, {
		"schemaVersion": 2,
		"modeId": "combat_sandbox",
		"contentRevision": str(revisions["currentRevision"]),
		"seed": seed,
		"attemptKey": attempt_key,
		"participants": [
			{
				"instanceId": "player",
				"entityDefinitionId": "player_warrior",
				"sideId": "player",
				"controllerBinding": {"kind": "Player"}
			},
			{
				"instanceId": "opponent",
				"entityDefinitionId": "enemy_goblin",
				"sideId": "opposition",
				"controllerBinding": {"kind": "AI", "policyId": "gambit"}
			}
		],
		"startingCards": [
			{"definitionId": "basic_attack"},
			{"definitionId": "basic_attack"},
			{"definitionId": "defend"},
			{"definitionId": "fireball"},
			{"definitionId": "heal"}
		],
		"initialState": {"resourcesByActor": {"player": {"energy": 3}}}
	})
	if is_failure(result):
		return false
	var run: Dictionary = result.get("run", {})
	return await load_snapshot(str(run.get("runId", "")))

func load_snapshot(run_id: String = "") -> bool:
	if run_id.is_empty():
		run_id = current_run_id()
	if run_id.is_empty():
		return false
	var result := await request_json("/api/v1/sandbox/runs/%s/snapshot" % run_id)
	if is_failure(result):
		return false
	snapshot = result
	snapshot_changed.emit(snapshot)
	_emit_input_state()
	return true

func legal_actions(actor_id: String = "") -> Dictionary:
	if actor_id.is_empty():
		actor_id = player_actor_id()
	return await request_json(
		"/api/v1/combats/%s/legal-actions?actorId=%s" % [current_combat_id(), actor_id.uri_encode()])

func play_card(card_instance_id: String, target_ids: Array[String]) -> bool:
	return await execute_combat_command("PLAY_CARD", {
		"actorId": player_actor_id(),
		"cardInstanceId": card_instance_id,
		"targetIds": target_ids
	})

func end_turn() -> bool:
	return await execute_combat_command("END_TURN", {"actorId": player_actor_id()})

func pass_priority() -> bool:
	return await execute_combat_command("PASS_PRIORITY", {"actorId": player_actor_id()})

func execute_combat_command(command_type: String, payload: Dictionary) -> bool:
	if playing_receipt or not can_player_input():
		return false
	var run: Dictionary = snapshot.get("run", {})
	var combat: Dictionary = snapshot.get("combat", {})
	var result := await request_json(
		"/api/v1/combats/%s/commands" % current_combat_id(),
		HTTPClient.METHOD_POST,
		{
			"commandId": new_command_id(),
			"expectedSequence": int(run.get("sequence", 0)),
			"expectedStep": int(combat.get("step", 0)),
			"type": command_type,
			"payload": payload
		})
	if is_failure(result):
		if int(result.get("_http_status", 0)) == 409:
			await load_snapshot()
		return false
	await play_receipt(result)
	return await load_snapshot()

func play_receipt(receipt: Dictionary) -> void:
	playing_receipt = true
	last_receipt = receipt
	input_changed.emit(false, "reproduzindo receipt")
	receipt_started.emit(receipt)
	var state: Dictionary = receipt.get("state", {})
	var resolution: Dictionary = state.get("resolution", {})
	var frames: Array = resolution.get("frames", [])
	frames.sort_custom(func(left: Dictionary, right: Dictionary) -> bool:
		return int(left.get("index", 0)) < int(right.get("index", 0)))
	for index in frames.size():
		_frame_acknowledged = not pause_between_frames
		frame_ready.emit(frames[index], index, frames.size())
		while not _frame_acknowledged:
			await get_tree().process_frame
	playing_receipt = false
	receipt_finished.emit(receipt)

func acknowledge_frame() -> void:
	_frame_acknowledged = true

func load_timeline(after_sequence: int = 0, limit: int = 200) -> Dictionary:
	timeline = await request_json(
		"/api/v1/combats/%s/timeline?afterSequence=%d&limit=%d" % [current_combat_id(), after_sequence, limit])
	return timeline

func load_historical_state(run_sequence: int) -> Dictionary:
	return await request_json(
		"/api/v1/combats/%s/timeline/%d/state" % [current_combat_id(), run_sequence])

func select_timeline_frame(run_sequence: int, frame_index: int) -> Dictionary:
	for item in timeline.get("items", []):
		if int(item.get("runSequence", -1)) != run_sequence:
			continue
		for frame in item.get("frames", []):
			if int(frame.get("frameIndex", -1)) == frame_index:
				return {"coordinate": {"runSequence": run_sequence, "frameIndex": frame_index}, "frame": frame}
	return {"coordinate": {"runSequence": run_sequence, "frameIndex": frame_index}, "state": await load_historical_state(run_sequence)}

func create_and_switch_branch(run_sequence: int, branch_key: String) -> bool:
	var result := await request_json(
		"/api/v1/combats/%s/timeline/%d/branches" % [current_combat_id(), run_sequence],
		HTTPClient.METHOD_POST,
		{"branchKey": branch_key})
	if is_failure(result):
		return false
	return await load_snapshot(str(result.get("runId", "")))

func load_branch_tree() -> Dictionary:
	return await request_json("/api/v1/runs/%s/branch-tree" % current_run_id())

func can_player_input() -> bool:
	if playing_receipt or snapshot.is_empty():
		return false
	var combat: Dictionary = snapshot.get("combat", {})
	if str(combat.get("status", "")) != "ACTIVE":
		return false
	var priority = combat.get("priorityWindow")
	if priority is Dictionary:
		return _is_player_actor(str(priority.get("holderActorId", "")))
	var activation = combat.get("activation")
	return activation is Dictionary \
		and bool(activation.get("waitingForInput", false)) \
		and _is_player_actor(str(activation.get("activeActorId", "")))

func input_reason() -> String:
	if playing_receipt:
		return "reproduzindo receipt"
	var combat: Dictionary = snapshot.get("combat", {})
	var priority = combat.get("priorityWindow")
	if priority is Dictionary:
		return "prioridade: %s" % priority.get("holderActorId", "")
	var activation = combat.get("activation")
	if activation is Dictionary:
		return "ativação: %s" % activation.get("activeActorId", "")
	return "sem janela de input"

func player_actor_id() -> String:
	for actor in snapshot.get("combat", {}).get("actors", []):
		if str(actor.get("controllerBinding", {}).get("kind", "")) == "Player":
			return str(actor.get("instanceId", ""))
	return ""

func opponent_actor_ids() -> Array[String]:
	var ids: Array[String] = []
	for actor in snapshot.get("combat", {}).get("actors", []):
		if str(actor.get("instanceId", "")) != player_actor_id() and bool(actor.get("isAlive", false)):
			ids.append(str(actor.get("instanceId", "")))
	return ids

func current_run_id() -> String:
	return str(snapshot.get("run", {}).get("runId", ""))

func current_combat_id() -> String:
	return str(snapshot.get("combat", {}).get("combatId", ""))

func is_failure(result: Dictionary) -> bool:
	return result.has("_http_status")

func _is_player_actor(actor_id: String) -> bool:
	for actor in snapshot.get("combat", {}).get("actors", []):
		if str(actor.get("instanceId", "")) == actor_id:
			return str(actor.get("controllerBinding", {}).get("kind", "")) == "Player"
	return false

func _emit_input_state() -> void:
	input_changed.emit(can_player_input(), input_reason())

func _failure(code: int, message: String, details: Dictionary = {}) -> Dictionary:
	request_failed.emit(code, message)
	return {"_http_status": code, "_error_message": message, "_details": details}

func new_command_id() -> String:
	var bytes := Crypto.new().generate_random_bytes(16)
	bytes[6] = (bytes[6] & 0x0f) | 0x40
	bytes[8] = (bytes[8] & 0x3f) | 0x80
	return "%02x%02x%02x%02x-%02x%02x-%02x%02x-%02x%02x-%02x%02x%02x%02x%02x%02x" % [
		bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
		bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]
	]
