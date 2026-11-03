extends Node

const ActivityChoices = preload("res://scripts/application/activity_choices.gd")
const SnapshotStore = preload("res://scripts/application/session_snapshot_store.gd")
const InspectionService = preload("res://scripts/application/session_inspection_service.gd")
const ReplayService = preload("res://scripts/application/session_replay_service.gd")
const CommandDispatcher = preload("res://scripts/application/session_command_dispatcher.gd")

signal operation_changed(state: String)
signal changed
signal command_started(label: String)
signal receipt_received(receipt: Dictionary)
signal presentation_ready(envelope)
signal presentation_discarded
var _accepted_presentation: Dictionary = {}
var _command_started_ms := 0
signal run_opened(run_id: String)
signal availability_changed(value: bool)
signal command_finished
signal failed(error: Dictionary)

var _run: Dictionary = {}
var _combat: Dictionary = {}
var _available_commands: Array = []
var _legal_actions: Array = []
var _tool_capabilities: Dictionary = {}
var _card_zones: Dictionary = {}
var _character_attributes: Dictionary = {}
var _card_presentations: Dictionary = {}
var _collection_presentations: Dictionary = {}
var _modifier_items: Dictionary = {}
var _card_evaluations: Array = []
var _card_evaluation_context: Array = []
var _settings: Array = []
var tool_profile := "normal"
var selected_setting_id := "default"
var run: Dictionary:
	get: return _run.duplicate(true)
var combat: Dictionary:
	get: return _combat.duplicate(true)
var available_commands: Array:
	get: return _available_commands.duplicate(true)
var legal_actions: Array:
	get: return _legal_actions.duplicate(true)
var tool_capabilities: Array:
	get: return _tool_capabilities.keys().duplicate()
var card_zones: Dictionary:
	get: return _card_zones.duplicate(true)
var character_attributes: Dictionary:
	get: return _character_attributes.duplicate(true) if str(_character_attributes.get("runId", "")) == str(_run.get("runId", "")) and int(_character_attributes.get("runSequence", -1)) == int(_run.get("sequence", 0)) else {}
var card_evaluations_ready: bool:
	get: return _card_evaluation_context == _evaluation_context(_run, _combat)
var card_presentations: Array:
	get: return _card_presentations.get("cards", []).duplicate(true) if str(_card_presentations.get("runId", "")) == str(_run.get("runId", "")) and int(_card_presentations.get("runSequence", -1)) == int(_run.get("sequence", 0)) and str(_card_presentations.get("contentRevision", "")) == str(_run.get("contentRevision", "")) else []
var card_evaluations: Array:
	get: return _card_evaluations.duplicate(true) if card_evaluations_ready else []
var settings: Array:
	get: return _settings.duplicate(true)
var modifier_items: Dictionary:
	get: return _modifier_items.duplicate(true) if str(_modifier_items.get("runId", "")) == str(_run.get("runId", "")) and int(_modifier_items.get("runSequence", -1)) == int(_run.get("sequence", 0)) else {}
var selected_setting: Dictionary:
	get:
		for item in _settings:
			if str(item.get("settingId", "")) == selected_setting_id:
				return item.duplicate(true)
		return {}
var _gateway
var available := false
var content_revision := ""
var _operation := "idle"
var last_view_capture_usec := 0

func _belongs_to_current_run(snapshot: Dictionary) -> bool:
	return SnapshotStore.belongs_to_run(snapshot, _run)

func capture_view_snapshot() -> Dictionary:
	# One private copy per consumer/revision. Never expose the mutable backing maps.
	var started := Time.get_ticks_usec()
	var result := SnapshotStore.capture_view_snapshot(
		_run, _combat, _legal_actions, _card_zones, _card_evaluations,
		card_evaluations_ready, _card_presentations, _character_attributes)
	last_view_capture_usec = Time.get_ticks_usec() - started
	return result
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

func connect_engine(preferred_setting_id := "default") -> bool:
	var ticket := _begin("connecting")
	if ticket < 0:
		return false
	var ok := await _connect(ticket, preferred_setting_id)
	_finish(ticket)
	return ok

func _connect(ticket: int, preferred_setting_id := "default") -> bool:
	var response: Dictionary = await _gateway.connect_engine()
	if not _current(ticket):
		return false
	if not response.ok:
		failed.emit(response)
		return false
	_settings = response.data.get("items", []).duplicate(true)
	if _settings.is_empty():
		failed.emit({"errorKey": "The engine did not publish any playable settings."})
		return false
	var requested := preferred_setting_id
	if not _has_setting(requested):
		requested = "default" if _has_setting("default") else str(_settings[0].get("settingId", ""))
	return _apply_setting(requested, false)

func select_setting(setting_id: String) -> bool:
	if busy or has_pending_command or not _has_setting(setting_id):
		return false
	return _apply_setting(setting_id, true)

func _has_setting(setting_id: String) -> bool:
	return _settings.any(func(item): return str(item.get("settingId", "")) == setting_id)

func _apply_setting(setting_id: String, clear_active_session: bool) -> bool:
	for item in _settings:
		if str(item.get("settingId", "")) != setting_id:
			continue
		selected_setting_id = setting_id
		content_revision = str(item.get("currentRevision", ""))
		if clear_active_session:
			_accepted_presentation = {}
			_run = {}
			_combat = {}
			_card_zones = {}
			_character_attributes = {}
			_card_evaluations = []; _card_evaluation_context = []
			_available_commands = []
			_legal_actions = []
			_tool_capabilities = {}
			synchronized = true
		changed.emit()
		return not content_revision.is_empty()
	return false

func start_campaign(seed: int) -> bool:
	return await _open_run("campaign", [seed])

func start_sandbox(mode_id: String, scenario: Dictionary, seed: int) -> bool:
	return await _open_run("sandbox", [mode_id, scenario.duplicate(true), seed])

func continue_run(run_id: String, expected_mode := "") -> bool:
	return false if run_id.is_empty() else await _open_run("continue", [run_id, expected_mode])

func _open_run(kind: String, args: Array) -> bool:
	if has_pending_command:
		return false
	var ticket := _begin("opening")
	if ticket < 0:
		return false
	if kind != "continue" and content_revision.is_empty() and not await _connect(ticket, selected_setting_id):
		_finish(ticket)
		return false
	var response: Dictionary
	match kind:
		"campaign": response = await _gateway.create_campaign(args[0], selected_setting)
		"sandbox": response = await _gateway.create_sandbox(args[0], args[1], args[2], content_revision, selected_setting_id)
		_: response = await _gateway.read_run(args[0])
	if not _current(ticket):
		return false
	if not response.ok:
		failed.emit(response)
		_finish(ticket)
		return false
	var opened: Dictionary = response.data.get("run", response.data)
	if kind == "continue":
		var launch: Dictionary = selected_setting.get("launch", {})
		var setting_id := str(opened.get("settingId", opened.get("configName", "")))
		var valid := str(opened.get("runId", "")) == str(args[0]) and setting_id == selected_setting_id and _has_setting(setting_id) \
			and str(opened.get("playerEntityId", "")) == str(launch.get("playerEntityId", "player")) \
			and str(opened.get("lifecycle", "")).to_lower() == "active" \
			and (str(args[1]).is_empty() or str(opened.get("modeId", "")) == str(args[1]))
		if not valid:
			failed.emit({"errorKey": "This journey does not match the selected setting, player or mode, or is no longer active."})
			_finish(ticket)
			return false
	_run = opened.duplicate(true)
	_accepted_presentation = {}
	var opened_setting_id := str(_run.get("settingId", selected_setting_id))
	if _has_setting(opened_setting_id):
		selected_setting_id = opened_setting_id
	content_revision = str(_run.get("contentRevision", content_revision))
	_combat = {}
	_card_zones = {}
	_character_attributes = {}
	_card_evaluations = []; _card_evaluation_context = []
	synchronized = false
	var ok := await _refresh(ticket)
	if ok: run_opened.emit(str(_run.get("runId", "")))
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

func preview_transformation(choice: Dictionary) -> Dictionary:
	if busy or not synchronized or _run.is_empty(): return {}
	return await InspectionService.preview_transformation(_gateway, func(): return _run, choice)

func _refresh(ticket: int, committed_combat: Dictionary = {}, receipt_sequence := -1) -> bool:
	if _run.is_empty():
		return false
	var run_id := str(_run.get("runId", ""))
	var response: Dictionary = await _gateway.read_session(run_id, receipt_sequence)
	# Another authorized client may have advanced the run after our durable
	# receipt. Read the new head atomically; never relabel it as the old receipt.
	if _current(ticket) and not response.ok and int(response.get("status", 0)) == 409 and receipt_sequence >= 0:
		response = await _gateway.read_session(run_id, -1)
		receipt_sequence = -1
		committed_combat = {}
	if not _current(ticket):
		return false
	var refresh_timings := {"session_loaded": Time.get_ticks_msec()}
	var validated := SnapshotStore.validate_session_bundle(response, run_id, committed_combat, receipt_sequence)
	if not validated.get("ok", false):
		return _refresh_failed(validated.get("error", {"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."}))
	_modifier_items = validated.modifier_items
	_card_presentations = validated.card_presentations
	_character_attributes = validated.character_attributes
	_run = validated.run
	_combat = validated.combat
	_card_zones = validated.card_zones
	_available_commands = validated.available_commands
	_legal_actions = validated.legal_actions
	_card_evaluations = validated.card_evaluations
	_card_evaluation_context = _evaluation_context(_run, _combat)
	_tool_capabilities = validated.tool_capabilities
	tool_profile = validated.tool_profile
	synchronized = true
	changed.emit()
	if not _accepted_presentation.is_empty():
		var accepted: Dictionary = _accepted_presentation
		_accepted_presentation = {}
		var envelope = preload("res://scripts/presentation/presentation_envelope.gd").build(accepted.receipt, accepted.before,
			capture_view_snapshot(), accepted.endpoint)
		if envelope != null:
			for marker in refresh_timings: envelope.mark_at(marker, refresh_timings[marker])
			envelope.mark_at("submitted", int(accepted.get("submitted", 0)))
			envelope.mark_at("received", int(accepted.get("received", 0)))
			presentation_ready.emit(envelope)
		else:
			presentation_discarded.emit()
			failed.emit({"errorKey": "Presentation was superseded by a newer game revision."})
	return true

func _refresh_failed(error: Dictionary) -> bool:
	synchronized = false
	_legal_actions = []
	_available_commands = []
	_tool_capabilities = {}
	# Retain the last published presentation while disconnected. Commands are
	# disabled; a failed read must not replace cards with fallback text/empty zones.
	tool_profile = "normal"
	failed.emit(error)
	changed.emit()
	return false

func _evaluation_context(run_state: Dictionary, combat_state: Dictionary) -> Array:
	return SnapshotStore.evaluation_context(selected_setting_id, run_state, combat_state)

func execute_run_command(type: String, payload: Dictionary, label := "") -> bool:
	var advertised := command(type)
	var expected_step := int(advertised.get("expectedStep", _run.get("step", 0)))
	return await _execute("runs", str(_run.get("runId", "")), type, payload, label, expected_step)

func read_support_loadout() -> Dictionary:
	if busy or has_pending_command or _run.is_empty(): return {"ok": false, "error": "Recover pending commands before editing."}
	var ticket := _generation
	var response: Dictionary = await _gateway.read_support_loadout(str(_run.runId))
	if ticket != _generation: return {"ok": false, "status": 409, "error": "Session changed while reading equipment."}
	if response.get("ok", false):
		if not response.get("data") is Dictionary or not loadout_context_matches(response.data) or str(response.data.get("actorId", "")) != str(_run.get("playerEntityId", "")):
			return {"ok": false, "status": 409, "error": "Equipment context changed. Reopen the loadout."}
		response.data["_clientContext"] = replay_context()
	return response

func preview_support_loadout(snapshot: Dictionary, equipment: Dictionary) -> Dictionary:
	if busy or has_pending_command or not loadout_context_matches(snapshot): return {"ok": false, "status": 409, "error": "Equipment context changed."}
	var response: Dictionary = await _gateway.preview_support_loadout(snapshot, equipment)
	if not loadout_context_matches(snapshot): return {"ok": false, "status": 409, "error": "Session changed while previewing."}
	return response

func apply_support_loadout(snapshot: Dictionary, equipment: Dictionary) -> bool:
	if not loadout_context_matches(snapshot) or str(equipment.get("actorId", "")) != str(_run.get("playerEntityId", "")): return false
	return await _execute("runs", str(snapshot.runId), "EQUIP_SUPPORT", equipment.duplicate(true), "Change support", int(snapshot.expectedStep))

func read_deck_loadout() -> Dictionary:
	if busy or has_pending_command or _run.is_empty(): return {"ok": false, "error": "Recover pending commands before editing."}
	var ticket := _generation
	var response: Dictionary = await _gateway.read_deck_loadout(str(_run.runId))
	if ticket != _generation: return {"ok": false, "status": 409, "error": "Session changed while reading the deck."}
	if response.get("ok", false):
		if not response.get("data") is Dictionary or not loadout_context_matches(response.data) or str(response.data.get("actorId", "")) != str(_run.get("playerEntityId", "")):
			return {"ok": false, "status": 409, "error": "Deck context changed. Reopen the loadout."}
		response.data["_clientContext"] = replay_context()
	return response

func preview_deck_loadout(snapshot: Dictionary, edit: Dictionary) -> Dictionary:
	if busy or has_pending_command or not loadout_context_matches(snapshot): return {"ok": false, "status": 409, "error": "Deck context changed."}
	var response: Dictionary = await _gateway.preview_deck_loadout(snapshot, edit)
	if not loadout_context_matches(snapshot): return {"ok": false, "status": 409, "error": "Session changed while previewing."}
	return response

func apply_deck_loadout(snapshot: Dictionary, edit: Dictionary) -> bool:
	if not loadout_context_matches(snapshot) or str(edit.get("actorId", "")) != str(_run.get("playerEntityId", "")): return false
	return await _execute("runs", str(snapshot.runId), "EDIT_DECK_LOADOUT", edit.duplicate(true), "Change deck", int(snapshot.expectedStep))

func read_deck_compartments() -> Dictionary:
	if busy or has_pending_command or _run.is_empty(): return {"ok": false, "error": "Open a run and recover pending commands first."}
	var ticket := _generation
	var response: Dictionary = await _gateway.read_deck_compartments(str(_run.runId))
	if ticket != _generation: return {"ok": false, "error": "Session changed while reading compartments."}
	if response.get("ok", false) and not response.get("data") is Dictionary: return {"ok": false, "error": "Invalid compartment response."}
	if response.get("ok", false):
		if not loadout_context_matches(response.data) or str(response.data.get("actorId", "")) != str(_run.get("playerEntityId", "")):
			return {"ok": false, "status": 409, "error": "Compartment context changed. Reopen the editor."}
		response.data["_clientContext"] = replay_context()
	return response

func read_loadout_info() -> Dictionary:
	if busy or has_pending_command or _run.is_empty(): return {"ok": false, "error": "Open a run and recover pending commands first."}
	var captured := [_run.get("runId"), _run.get("sequence"), _run.get("step"), _run.get("contentRevision"), _run.get("playerEntityId"), replay_context()]
	var response: Dictionary = await _gateway.read_loadout_info(str(_run.runId))
	if captured != [_run.get("runId"), _run.get("sequence"), _run.get("step"), _run.get("contentRevision"), _run.get("playerEntityId"), replay_context()]:
		return {"ok": false, "status": 409, "error": "Equipment changed. Synchronizing…"}
	return InspectionService.validate_loadout_info(response, _run)

func preview_deck_compartments(snapshot: Dictionary, allocations: Array) -> Dictionary:
	if busy or has_pending_command or not loadout_context_matches(snapshot): return {"ok": false, "status": 409, "error": "Compartment context changed. Reopen the editor."}
	var response: Dictionary = await _gateway.preview_deck_compartments(snapshot, allocations)
	if not loadout_context_matches(snapshot): return {"ok": false, "status": 409, "error": "Session changed while previewing."}
	return response

func apply_deck_compartments(snapshot: Dictionary, allocations: Array) -> bool:
	if not loadout_context_matches(snapshot) or str(snapshot.get("actorId", "")) != str(_run.get("playerEntityId", "")): return false
	return await _execute("runs", str(snapshot.runId), "APPLY_DECK_COMPARTMENTS", {
		"actorId": snapshot.actorId, "allocations": allocations.duplicate(true)}, "Apply deck compartments", int(snapshot.expectedStep))

func loadout_context_matches(snapshot: Dictionary) -> bool:
	return InspectionService.loadout_context_matches(synchronized, _run, snapshot, replay_context())

func read_loadout(actor_id: String) -> Dictionary:
	if busy or has_pending_command or _run.is_empty(): return {"ok": false, "error": "Open a run and recover pending commands first."}
	var ticket := _generation
	var response: Dictionary = await _gateway.read_loadout(str(_run.runId), actor_id)
	if ticket != _generation: return {"ok": false, "error": "Session changed while reading loadout."}
	if response.get("ok", false) and not response.get("data") is Dictionary: return {"ok": false, "error": "Invalid loadout information."}
	if response.ok and (not loadout_context_matches(response.data) or str(response.data.get("actorId", "")) != actor_id):
		return {"ok": false, "error": "Loadout changed. Refresh before editing."}
	if response.ok: response.data["_clientContext"] = replay_context()
	return response

func preview_loadout(snapshot: Dictionary, assignments: Dictionary) -> Dictionary:
	if busy or has_pending_command or not loadout_context_matches(snapshot): return {"ok": false, "error": "Loadout changed. Refresh before editing."}
	var response: Dictionary = await _gateway.preview_loadout(snapshot, assignments)
	if not loadout_context_matches(snapshot): return {"ok": false, "error": "Loadout changed while previewing."}
	return response

func apply_loadout(snapshot: Dictionary, assignments: Dictionary) -> bool:
	if not loadout_context_matches(snapshot): return false
	return await _execute("runs", str(snapshot.runId), "APPLY_LOADOUT", {
		"actorId": snapshot.actorId, "assignments": assignments.duplicate(true)}, "Apply inscriptions", int(snapshot.expectedStep))

func execute_combat_command(type: String, payload: Dictionary, label := "") -> bool:
	return await _execute("combats", str(_combat.get("combatId", "")), type, payload, label, int(_combat.get("step", _run.get("step", 0))))

func submit_candidate(candidate: Dictionary) -> bool:
	var command_data: Dictionary = candidate.get("command", {})
	var type := CommandDispatcher.candidate_command_type(candidate)
	return await execute_combat_command(type, _gateway.candidate_payload(command_data))

func _execute(scope: String, id: String, type: String, payload: Dictionary, label: String, expected_step: int) -> bool:
	if not synchronized or _run.is_empty() or has_pending_command:
		return false
	var ticket := _begin("submitting")
	if ticket < 0:
		return false
	command_started.emit(label if not label.is_empty() else type)
	_command_started_ms = Time.get_ticks_msec()
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
	response = CommandDispatcher.normalize_receipt_response(response)
	if not response.ok:
		if CommandDispatcher.should_clear_pending_on_failure(response):
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
	_accepted_presentation = {"receipt": response.data.duplicate(true), "before": {"run": run, "combat": combat, "zones": card_zones},
		"endpoint": replay_context() if _gateway.has_method("replay_context") else ["fixture", _generation],
		"submitted": _command_started_ms, "received": Time.get_ticks_msec()}
	receipt_received.emit(response.data.duplicate(true))
	var state := preload("res://scripts/presentation/presentation_envelope.gd").dictionary(response.data.get("state"))
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
	_accepted_presentation = {}
	_combat = {}
	_card_zones = {}
	_character_attributes = {}
	_card_evaluations = []; _card_evaluation_context = []
	_legal_actions = []
	_available_commands = []
	_tool_capabilities = {}
	tool_profile = "normal"
	content_revision = ""
	_settings = []
	synchronized = false
	set_available(false)
	operation_changed.emit(_operation)
	changed.emit()

func input_actor_id() -> String:
	return _input_actor_id(_combat)

static func _input_actor_id(snapshot: Dictionary) -> String:
	return SnapshotStore.input_actor_id(snapshot)


func command(type: String) -> Dictionary:
	for candidate in available_commands:
		if str(candidate.get("type", "")) == type:
			return candidate
	return {}

func allows_tool(capability: String) -> bool:
	return _tool_capabilities.has(capability)

func _tool_denied(capability: String) -> Dictionary:
	return ReplayService.tool_denied(capability)

func timeline(after_sequence := 0) -> Dictionary:
	if not allows_tool("timeline.read"):
		return _tool_denied("timeline.read")
	if _combat.is_empty():
		return {"ok": false, "errorKey": "No active combat.", "error": "No active combat."}
	var limit := mini(50, int(_run.get("resolvedMode", {}).get("timelinePolicy", {}).get("maxItemsPerPage", 50)))
	return await _gateway.timeline(str(_combat.get("combatId", "")), after_sequence, limit)

func branch_tree() -> Dictionary:
	if not allows_tool("timeline.branch.read"):
		return _tool_denied("timeline.branch.read")
	return await _gateway.branch_tree(str(_run.get("runId", "")))

func create_branch(sequence: int, key: String) -> Dictionary:
	if not allows_tool("timeline.branch.create"):
		return _tool_denied("timeline.branch.create")
	if _combat.is_empty():
		return {"ok": false, "errorKey": "No active combat.", "error": "No active combat."}
	if has_pending_command: return {"ok": false, "errorKey": "Recover the pending command first."}
	var ticket := _begin("branching")
	if ticket < 0: return {"ok": false, "errorKey": "An operation is already in progress."}
	var result: Dictionary = await _gateway.create_branch(str(_combat.get("combatId", "")), sequence, key)
	_finish(ticket)
	return result

func verify() -> Dictionary:
	if not allows_tool("replay.verify"):
		return _tool_denied("replay.verify")
	return await verify_run(str(_run.get("runId", "")))

func verify_run(run_id: String) -> Dictionary:
	if run_id.is_empty():
		return {"ok": false, "errorKey": "No journey was selected.", "error": "No journey was selected."}
	return await _gateway.verify(run_id)

func run_history() -> Dictionary:
	var setting := selected_setting_id
	var player := str(selected_setting.get("launch", {}).get("playerEntityId", "player"))
	var response: Dictionary = await _gateway.run_history(player, setting)
	if selected_setting_id != setting:
		return {"ok": false, "errorKey": "The selected setting changed. Try again."}
	return response

func find_resume(index, api: String, legacy_id := "") -> Dictionary:
	# Reading bookmarks/profiles must not activate a run or change settings.
	var setting := selected_setting_id
	var launch: Dictionary = selected_setting.get("launch", {})
	var generation := _generation
	return await ReplayService.find_resume(_gateway, index, api, legacy_id, setting, launch, generation, func(gen: int, expected_setting: String) -> bool:
		return gen == _generation and selected_setting_id == expected_setting)

func replay_timeline(run_id: String, after_sequence := 0, limit := 25, through_sequence := 0) -> Dictionary:
	if run_id.is_empty():
		return {"ok": false, "errorKey": "No journey was selected.", "error": "No journey was selected."}
	return await _gateway.run_timeline(run_id, after_sequence, limit, through_sequence)

func replay_context() -> Array: return _gateway.replay_context()
func begin_replay_page(run_id: String, after_sequence: int, limit: int, through_sequence: int):
	return _gateway.begin_run_timeline(run_id, after_sequence, limit, through_sequence)
func begin_replay_presentation(run_id: String, sequence: int):
	return _gateway.begin_run_presentation(run_id, sequence)
func begin_replay_inspection(run_id: String, sequence: int):
	return _gateway.begin_run_commit(run_id, sequence)

func replay_commit(run_id: String, sequence: int) -> Dictionary:
	if run_id.is_empty() or sequence < 1:
		return {"ok": false, "errorKey": "No replay command was selected.", "error": "No replay command was selected."}
	return await _gateway.run_commit(run_id, sequence)

func simulate(commands: Array) -> Dictionary:
	if not allows_tool("simulation.run"):
		return _tool_denied("simulation.run")
	return await _gateway.simulate(str(_run.get("runId", "")), int(_run.get("sequence", 0)), commands)

func simulate_end_turn() -> Dictionary:
	for candidate in _legal_actions:
		if str(candidate.get("command", {}).get("actionType", "")) == "END_TURN":
			return await simulate([{"type": "END_TURN", "payload": _gateway.candidate_payload(candidate.command)}])
	return {"ok": false, "errorKey": "No actor is waiting for input to simulate.", "error": "No actor is waiting for input to simulate."}

func content(kind: String, limit := 100) -> Dictionary:
	return await _gateway.content(kind, content_revision, selected_setting_id, limit)


func activity_choices() -> Array:
	return ActivityChoices.build(_run, _available_commands)

func submit_activity(choice: Dictionary) -> bool:
	return await execute_run_command(str(choice.get("type", "")), choice.get("payload", {}))

func payment_context_matches(version: Dictionary) -> bool:
	return InspectionService.payment_context_matches(synchronized, _run, _combat, content_revision, version, replay_context())

func preview_card_payment(command: Dictionary) -> Dictionary:
	if busy or has_pending_command or not synchronized: return {"ok": false, "error": "Recover pending commands first."}
	var ticket := _generation; var context := replay_context()
	var response: Dictionary = await _gateway.preview_card_payment(str(_combat.get("combatId", "")), int(_run.get("sequence", 0)), int(_combat.get("step", 0)), command)
	if ticket != _generation or context != replay_context(): return {"ok": false, "status": 409, "error": "Session changed while selecting payment."}
	if response.get("ok", false):
		if not response.get("data") is Dictionary or not response.data.get("version") is Dictionary or not payment_context_matches(response.data.version): return {"ok": false, "status": 409, "error": "Payment version changed."}
		response.data.version["_clientContext"] = context
	return response

func submit_payment_preview(preview: Dictionary) -> bool:
	if not preview.get("isLegal", false) or not payment_context_matches(preview.get("version", {})) or not preview.get("command") is Dictionary: return false
	return await execute_combat_command("PLAY_CARD", _gateway.candidate_payload(preview.command), "Play card")

func inspect_hand(target_ids := []) -> Dictionary:
	return await _gateway.inspect_hand(str(_combat.get("combatId", "")), input_actor_id(), target_ids)
func inspect_card(instance_id: String) -> Dictionary:
	return await _gateway.inspect_card(str(_combat.get("combatId", "")), input_actor_id(), instance_id)

func load_collection_presentations() -> Dictionary:
	if not synchronized or busy or _run.is_empty(): return {"ok": false}
	var identity := [_run.get("runId"), _run.get("sequence"), _run.get("contentRevision"), _run.get("stateHash")]
	if _collection_presentations.get("identity", []) == identity:
		return {"ok": true, "data": _collection_presentations.data.duplicate(true)}
	var response: Dictionary = await _gateway.read_card_presentations(str(_run.runId))
	if identity != [_run.get("runId"), _run.get("sequence"), _run.get("contentRevision"), _run.get("stateHash")]: return {"ok": false, "cancelled": true}
	if not response.ok: return response
	var data: Dictionary = response.data
	if str(data.get("runId", "")) != str(_run.runId) or int(data.get("runSequence", -1)) != int(_run.sequence) or str(data.get("contentRevision", "")) != str(_run.get("contentRevision", "")):
		return {"ok": false, "cancelled": true}
	_collection_presentations = {"identity": identity, "data": data.duplicate(true)}
	return {"ok": true, "data": data.duplicate(true)}

func historical_state(sequence: int) -> Dictionary:
	if not allows_tool("timeline.inspect_state"):
		return _tool_denied("timeline.inspect_state")
	return await _gateway.historical_state(str(_combat.get("combatId", "")), sequence)

func resolution(command_id: String) -> Dictionary:
	return await _gateway.resolution(str(_combat.get("combatId", "")), command_id)

func gameplay_events(run_id: String, sequence: int) -> Dictionary:
	if not allows_tool("timeline.inspect_state") or not allows_tool("debug.card_inspection.full"):
		return _tool_denied("debug.card_inspection.full")
	return await _gateway.read_gameplay_events(run_id, sequence)
