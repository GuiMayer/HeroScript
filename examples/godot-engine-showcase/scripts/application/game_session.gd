extends Node

const ActivityChoices = preload("res://scripts/application/activity_choices.gd")

signal changed
signal command_started(label: String)
signal receipt_received(receipt: Dictionary)
signal run_opened(run_id: String)
signal availability_changed(value: bool)
signal command_finished
signal failed(error: Dictionary)

var _run: Dictionary = {}
var _combat: Dictionary = {}
var _available_commands: Array = []
var _legal_actions: Array = []
var run: Dictionary:
	get: return _run.duplicate(true)
var combat: Dictionary:
	get: return _combat.duplicate(true)
var available_commands: Array:
	get: return _available_commands.duplicate(true)
var legal_actions: Array:
	get: return _legal_actions.duplicate(true)
var _gateway
var available := false
var content_revision := ""
var busy := false
var synchronized := true

func configure(gateway) -> void:
	_gateway = gateway

func set_available(value: bool) -> void:
	available = value
	availability_changed.emit(value)

func connect_engine() -> bool:
	var response: Dictionary = await _gateway.connect_engine()
	if not response.ok:
		failed.emit(response)
		return false
	content_revision = str(response.data.get("currentRevision", ""))
	return not content_revision.is_empty()

func start_campaign(seed: int) -> bool:
	if content_revision.is_empty() and not await connect_engine():
		return false
	var response: Dictionary = await _gateway.create_campaign(seed, content_revision)
	if not response.ok:
		failed.emit(response)
		return false
	_run = response.data.duplicate(true)
	run_opened.emit(str(_run.get("runId", "")))
	return await refresh()

func start_sandbox(mode_id: String, scenario: Dictionary, seed: int) -> bool:
	if content_revision.is_empty() and not await connect_engine():
		return false
	var response: Dictionary = await _gateway.create_sandbox(mode_id, scenario, seed, content_revision)
	if not response.ok:
		failed.emit(response)
		return false
	_run = response.data.get("run", response.data).duplicate(true)
	_combat = response.data.get("combat", {}).duplicate(true)
	run_opened.emit(str(_run.get("runId", "")))
	return await refresh()

func continue_run(run_id: String) -> bool:
	if run_id.is_empty():
		return false
	var response: Dictionary = await _gateway.read_run(run_id)
	if not response.ok:
		failed.emit(response)
		return false
	_run = response.data.duplicate(true)
	run_opened.emit(run_id)
	return await refresh()

func refresh(committed_combat: Dictionary = {}, receipt_sequence := -1) -> bool:
	if _run.is_empty():
		return false
	var run_id := str(_run.get("runId", ""))
	var response: Dictionary = await _gateway.read_run(run_id)
	if not response.ok:
		return _refresh_failed(response)
	var next_run: Dictionary = response.data
	var next_combat: Dictionary = committed_combat if receipt_sequence == int(next_run.get("sequence", 0)) else {}
	var encounter_id := str(next_run.get("activeEncounterId", "")) if next_run.get("activeEncounterId") != null else ""
	var include_combat := not encounter_id.is_empty() and str(next_combat.get("combatId", "")) != encounter_id
	if encounter_id.is_empty():
		next_combat = {}
	var responses: Dictionary = await _gateway.read_projections(run_id, encounter_id, include_combat)
	for result in responses.values():
		if not result.ok:
			return _refresh_failed(result)
	if responses.has("combat"):
		next_combat = responses.combat.data
	var next_actions: Array = []
	if not next_combat.is_empty() and str(next_combat.get("status", "ACTIVE")) == "ACTIVE":
		var actor_id := _input_actor_id(next_combat)
		if not actor_id.is_empty():
			var actions_response: Dictionary = await _gateway.read_legal_actions(str(next_combat.get("combatId", "")), actor_id)
			if not actions_response.ok:
				return _refresh_failed(actions_response)
			next_actions = actions_response.data if actions_response.data is Array else actions_response.data.get("candidates", [])
	_run = next_run.duplicate(true)
	_combat = next_combat.duplicate(true)
	_available_commands = responses.commands.data.get("commands", []).duplicate(true)
	_legal_actions = next_actions.duplicate(true)
	synchronized = true
	changed.emit()
	return true

func _refresh_failed(error: Dictionary) -> bool:
	synchronized = false
	_legal_actions = []
	_available_commands = []
	failed.emit(error)
	changed.emit()
	return false

func execute_run_command(type: String, payload: Dictionary, label := "") -> bool:
	var advertised := command(type)
	var expected_step := int(advertised.get("expectedStep", _run.get("step", 0)))
	return await _execute("runs", str(_run.get("runId", "")), type, payload, label, expected_step)

func execute_combat_command(type: String, payload: Dictionary, label := "") -> bool:
	return await _execute("combats", str(_combat.get("combatId", "")), type, payload, label, int(_combat.get("step", _run.get("step", 0))))

func submit_candidate(candidate: Dictionary) -> bool:
	var command_data: Dictionary = candidate.get("command", {})
	var type := "PLAY_CARD" if str(candidate.get("source", "")) == "Card" else (
		"END_TURN" if str(command_data.get("actionType", "")) == "END_TURN" else "EXECUTE_ACTION")
	return await execute_combat_command(type, _gateway.candidate_payload(command_data))

func _execute(scope: String, id: String, type: String, payload: Dictionary, label: String, expected_step: int) -> bool:
	if busy or not synchronized or _run.is_empty():
		return false
	busy = true
	command_started.emit(label if not label.is_empty() else type)
	var response: Dictionary = await _gateway.send_command(scope, id, type, payload, int(_run.get("sequence", 0)), expected_step)
	if not response.ok:
		busy = false
		if int(response.get("status", 0)) in [0, 409]:
			synchronized = false
		failed.emit(response)
		command_finished.emit()
		return false
	receipt_received.emit(response.data.duplicate(true))
	var state: Dictionary = response.data.get("state", {})
	var updated := await refresh(state.get("combat", {}) if state.get("combat") is Dictionary else {}, int(response.data.get("sequence", -1)))
	busy = false
	command_finished.emit()
	# The POST committed even if a later read failed. Never silently resend.
	if not updated:
		failed.emit({"errorKey": "Could not refresh the game. Reconnect before playing again.", "error": "Could not refresh the game. Reconnect before playing again."})
	return true

func input_actor_id() -> String:
	return _input_actor_id(_combat)

static func _input_actor_id(snapshot: Dictionary) -> String:
	if snapshot.is_empty():
		return ""
	var priority = snapshot.get("priorityWindow")
	if priority is Dictionary and not priority.is_empty():
		return str(priority.get("holderActorId", ""))
	var activation = snapshot.get("activation", {})
	if activation is Dictionary and bool(activation.get("waitingForInput", false)):
		return str(activation.get("activeActorId", snapshot.get("activeActorId", "")))
	return str(snapshot.get("activeActorId", ""))

func player_actor() -> Dictionary:
	for actor in combat.get("actors", []):
		var binding = actor.get("controllerBinding", {})
		if str(binding.get("kind", "")) == "Player":
			return actor
	return {}

func opponents() -> Array:
	var player := player_actor()
	var player_side := str(player.get("sideId", "player"))
	return combat.get("actors", []).filter(func(actor): return str(actor.get("sideId", "")) != player_side)

func command(type: String) -> Dictionary:
	for candidate in available_commands:
		if str(candidate.get("type", "")) == type:
			return candidate
	return {}

func timeline() -> Dictionary:
	if _combat.is_empty():
		return {"ok": false, "errorKey": "No active combat.", "error": "No active combat."}
	return await _gateway.timeline(str(_combat.get("combatId", "")))

func branch_tree() -> Dictionary:
	return await _gateway.branch_tree(str(_run.get("runId", "")))

func create_branch(sequence: int, key: String) -> Dictionary:
	if _combat.is_empty():
		return {"ok": false, "errorKey": "No active combat.", "error": "No active combat."}
	return await _gateway.create_branch(str(_combat.get("combatId", "")), sequence, key)

func verify() -> Dictionary:
	return await _gateway.verify(str(_run.get("runId", "")))

func simulate(commands: Array) -> Dictionary:
	return await _gateway.simulate(str(_run.get("runId", "")), int(_run.get("sequence", 0)), commands)

func simulate_end_turn() -> Dictionary:
	for candidate in _legal_actions:
		if str(candidate.get("command", {}).get("actionType", "")) == "END_TURN":
			return await simulate([{"type": "END_TURN", "payload": _gateway.candidate_payload(candidate.command)}])
	return {"ok": false, "errorKey": "No actor is waiting for input to simulate.", "error": "No actor is waiting for input to simulate."}

func content(kind: String, limit := 100) -> Dictionary:
	return await _gateway.content(kind, content_revision, limit)


func activity_choices() -> Array:
	return ActivityChoices.build(_run, _available_commands)

func submit_activity(choice: Dictionary) -> bool:
	return await execute_run_command(str(choice.get("type", "")), choice.get("payload", {}))
