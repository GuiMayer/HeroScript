extends Node

const ActivityChoices = preload("res://scripts/application/activity_choices.gd")

signal operation_changed(state: String)
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
var _operation := "idle"
var _generation := 0
var _pending: Dictionary = {}
var busy: bool:
	get: return _operation != "idle"
var operation: String:
	get: return _operation
var has_pending_command: bool:
	get: return not _pending.is_empty()
var synchronized := true

func configure(gateway) -> void:
	_gateway = gateway

func set_available(value: bool) -> void:
	available = value
	availability_changed.emit(value)

func connect_engine() -> bool:
	var ticket := _begin("connecting")
	if ticket < 0:
		return false
	var ok := await _connect(ticket)
	_finish(ticket)
	return ok

func _connect(ticket: int) -> bool:
	var response: Dictionary = await _gateway.connect_engine()
	if not _current(ticket):
		return false
	if not response.ok:
		failed.emit(response)
		return false
	content_revision = str(response.data.get("currentRevision", ""))
	return not content_revision.is_empty()

func start_campaign(seed: int) -> bool:
	return await _open_run("campaign", [seed])

func start_sandbox(mode_id: String, scenario: Dictionary, seed: int) -> bool:
	return await _open_run("sandbox", [mode_id, scenario.duplicate(true), seed])

func continue_run(run_id: String) -> bool:
	return false if run_id.is_empty() else await _open_run("continue", [run_id])

func _open_run(kind: String, args: Array) -> bool:
	if has_pending_command:
		return false
	var ticket := _begin("opening")
	if ticket < 0:
		return false
	if kind != "continue" and content_revision.is_empty() and not await _connect(ticket):
		_finish(ticket)
		return false
	var response: Dictionary
	match kind:
		"campaign": response = await _gateway.create_campaign(args[0], content_revision)
		"sandbox": response = await _gateway.create_sandbox(args[0], args[1], args[2], content_revision)
		_: response = await _gateway.read_run(args[0])
	if not _current(ticket):
		return false
	if not response.ok:
		failed.emit(response)
		_finish(ticket)
		return false
	_run = response.data.get("run", response.data).duplicate(true)
	_combat = {}
	synchronized = false
	run_opened.emit(str(_run.get("runId", "")))
	var ok := await _refresh(ticket)
	_finish(ticket)
	return ok

func refresh() -> bool:
	if has_pending_command:
		return await recover_pending()
	var ticket := _begin("refreshing")
	if ticket < 0:
		return false
	var ok := await _refresh(ticket)
	_finish(ticket)
	return ok

func _refresh(ticket: int, committed_combat: Dictionary = {}, receipt_sequence := -1) -> bool:
	if _run.is_empty():
		return false
	var run_id := str(_run.get("runId", ""))
	var response: Dictionary = await _gateway.read_run(run_id)
	if not _current(ticket):
		return false
	if not response.ok:
		return _refresh_failed(response)
	var next_run: Dictionary = response.data
	var next_combat: Dictionary = committed_combat if receipt_sequence == int(next_run.get("sequence", 0)) else {}
	var encounter_id := str(next_run.get("activeEncounterId", "")) if next_run.get("activeEncounterId") != null else ""
	var include_combat := not encounter_id.is_empty() and str(next_combat.get("combatId", "")) != encounter_id
	if encounter_id.is_empty():
		next_combat = {}
	var responses: Dictionary = await _gateway.read_projections(run_id, encounter_id, include_combat)
	if not _current(ticket):
		return false
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
			if not _current(ticket):
				return false
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
	if not synchronized or _run.is_empty() or has_pending_command:
		return false
	var ticket := _begin("submitting")
	if ticket < 0:
		return false
	command_started.emit(label if not label.is_empty() else type)
	_pending = _gateway.prepare_command(scope, id, type, payload, int(_run.get("sequence", 0)), expected_step)
	return await _send_pending(ticket)

func recover_pending() -> bool:
	if not has_pending_command:
		return await refresh()
	var ticket := _begin("recovering")
	if ticket < 0:
		return false
	return await _send_pending(ticket)

func _send_pending(ticket: int) -> bool:
	var response: Dictionary = await _gateway.send_prepared(_pending)
	if not _current(ticket):
		return false
	if not response.ok:
		var status := int(response.get("status", 0))
		if status > 0 and status < 500:
			_pending = {}
		synchronized = false
		_legal_actions = []
		_available_commands = []
		failed.emit(response)
		_finish(ticket)
		command_finished.emit()
		changed.emit()
		return false
	_pending = {}
	receipt_received.emit(response.data.duplicate(true))
	var state: Dictionary = response.data.get("state", {})
	var updated := await _refresh(ticket, state.get("combat", {}) if state.get("combat") is Dictionary else {}, int(response.data.get("sequence", -1)))
	_finish(ticket)
	command_finished.emit()
	if not updated and _current(ticket):
		failed.emit({"errorKey": "Could not refresh the game. Reconnect before playing again."})
	return true

func _begin(state: String) -> int:
	if busy:
		return -1
	_generation += 1
	_operation = state
	operation_changed.emit(state)
	return _generation

func _current(ticket: int) -> bool:
	return ticket == _generation

func _finish(ticket: int) -> void:
	if _current(ticket):
		_operation = "idle"
		operation_changed.emit(_operation)

func invalidate() -> void:
	# Explicit session invalidation discards late reads, never an uncertain write.
	if has_pending_command:
		return
	_generation += 1
	_operation = "idle"
	_run = {}
	_combat = {}
	_legal_actions = []
	_available_commands = []
	content_revision = ""
	synchronized = false
	operation_changed.emit(_operation)
	changed.emit()

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


func command(type: String) -> Dictionary:
	for candidate in available_commands:
		if str(candidate.get("type", "")) == type:
			return candidate
	return {}

func timeline(after_sequence := 0) -> Dictionary:
	if _combat.is_empty():
		return {"ok": false, "errorKey": "No active combat.", "error": "No active combat."}
	var limit := mini(50, int(_run.get("resolvedMode", {}).get("timelinePolicy", {}).get("maxItemsPerPage", 50)))
	return await _gateway.timeline(str(_combat.get("combatId", "")), after_sequence, limit)

func branch_tree() -> Dictionary:
	return await _gateway.branch_tree(str(_run.get("runId", "")))

func create_branch(sequence: int, key: String) -> Dictionary:
	if _combat.is_empty():
		return {"ok": false, "errorKey": "No active combat.", "error": "No active combat."}
	if has_pending_command: return {"ok": false, "errorKey": "Recover the pending command first."}
	var ticket := _begin("branching")
	if ticket < 0: return {"ok": false, "errorKey": "An operation is already in progress."}
	var result: Dictionary = await _gateway.create_branch(str(_combat.get("combatId", "")), sequence, key)
	_finish(ticket)
	return result

func verify() -> Dictionary:
	return await verify_run(str(_run.get("runId", "")))

func verify_run(run_id: String) -> Dictionary:
	if run_id.is_empty():
		return {"ok": false, "errorKey": "No journey was selected.", "error": "No journey was selected."}
	return await _gateway.verify(run_id)

func run_history() -> Dictionary:
	return await _gateway.run_history("player")

func replay_timeline(run_id: String, after_sequence := 0, limit := 200) -> Dictionary:
	if run_id.is_empty():
		return {"ok": false, "errorKey": "No journey was selected.", "error": "No journey was selected."}
	return await _gateway.run_timeline(run_id, after_sequence, limit)

func replay_commit(run_id: String, sequence: int) -> Dictionary:
	if run_id.is_empty() or sequence < 1:
		return {"ok": false, "errorKey": "No replay command was selected.", "error": "No replay command was selected."}
	return await _gateway.run_commit(run_id, sequence)

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

func inspect_hand(target_ids := []) -> Dictionary:
	return await _gateway.inspect_hand(str(_combat.get("combatId", "")), input_actor_id(), target_ids)

func historical_state(sequence: int) -> Dictionary:
	return await _gateway.historical_state(str(_combat.get("combatId", "")), sequence)

func resolution(command_id: String) -> Dictionary:
	return await _gateway.resolution(str(_combat.get("combatId", "")), command_id)
