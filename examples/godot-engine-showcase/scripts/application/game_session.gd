extends Node

const ActivityChoices = preload("res://scripts/application/activity_choices.gd")

signal operation_changed(state: String)
signal changed
signal command_started(label: String)
signal receipt_received(receipt: Dictionary)
signal presentation_ready(envelope)
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
	var captured_sequence := int(_run.get("sequence", -1))
	var captured_run := str(_run.get("runId", ""))
	var operation: String = {"UPGRADE_CARD": "Apply", "REMOVE_CARD_TRANSFORMATION": "Remove", "REPLACE_CARD_TRANSFORMATION": "Replace"}.get(str(choice.get("type", "")), "")
	if str(choice.get("type", "")) == "APPLY_CARD_MODIFIER_ITEM":
		var item_payload: Dictionary = choice.get("payload", {})
		var preview: Dictionary = await _gateway.modifier_item_preview(captured_run, str(item_payload.get("itemInstanceId", "")), str(item_payload.get("cardInstanceId", "")))
		if not preview.ok or captured_run != str(_run.get("runId", "")) or captured_sequence != int(_run.get("sequence", -1)) or int(preview.data.get("runSequence", -2)) != captured_sequence: return {}
		var data: Dictionary = preview.data.duplicate(true)
		data["isCompatible"] = true
		return data
	if operation.is_empty(): return {}
	var payload: Dictionary = choice.get("payload", {})
	var upgrade_id := str(payload.upgradeId) if payload.get("upgradeId") != null else ""
	var response: Dictionary = await _gateway.transformation_preview(captured_run, str(payload.get("cardInstanceId", "")), operation, upgrade_id, payload.get("transformationId"))
	if not response.ok or captured_run != str(_run.get("runId", "")) or captured_sequence != int(_run.get("sequence", -1)): return {}
	if int(response.data.get("expectedSequence", -2)) != captured_sequence: return {}
	return response.data.duplicate(true)

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
	if not response.ok:
		return _refresh_failed(response)
	var data: Dictionary = response.data
	var next_run: Dictionary = data.get("run", {})
	var version: Dictionary = data.get("version", {})
	var next_combat: Dictionary = data.combat if data.get("combat") is Dictionary else {}
	if str(next_run.get("runId", "")) != run_id or str(version.get("runId", "")) != run_id or \
		int(version.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or \
		str(version.get("settingId", "")) != str(next_run.get("settingId", next_run.get("configName", ""))) or \
		str(version.get("contentRevision", "")) != str(next_run.get("contentRevision", "")) or \
		int(version.get("runStep", -1)) != int(next_run.get("step", 0)) or \
		str(version.get("engineVersion", "")) != str(next_run.get("engineVersion", "")) or \
		str(version.get("stateFingerprint", "")) != str(next_run.get("stateHash", "")) or \
		str(version.get("stateFingerprint", "")).is_empty() or \
		(receipt_sequence >= 0 and int(next_run.get("sequence", -1)) != receipt_sequence):
		return _refresh_failed({"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."})
	if not next_combat.is_empty() and (str(version.get("combatId", "")) != str(next_combat.get("combatId", "")) or \
		int(version.get("combatStep", -1)) != int(next_combat.get("step", 0))):
		return _refresh_failed({"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."})
	if not committed_combat.is_empty() and receipt_sequence >= 0 and (str(committed_combat.get("combatId", "")) != str(next_combat.get("combatId", "")) or \
		int(committed_combat.get("step", -1)) != int(next_combat.get("step", 0))):
		return _refresh_failed({"ok": false, "errorKey": "Presentation was superseded by a newer game revision."})
	var responses: Dictionary = {}
	for section in ["commands", "capabilities", "cardZones", "attributes", "cardPresentations", "modifierItems"]:
		if not data.get(section) is Dictionary: return _refresh_failed({"ok": false, "errorKey": "The engine returned an invalid JSON response."})
		responses[section] = {"ok": true, "data": data[section]}
	var refresh_timings := {"session_loaded": Time.get_ticks_msec()}
	if not data.get("actions") is Dictionary or not data.get("evaluations") is Dictionary:
		return _refresh_failed({"ok": false, "errorKey": "The engine returned an invalid JSON response."})
	var next_actions: Array = data.get("actions", {}).get("candidates", [])
	var next_evaluations: Array = data.get("evaluations", {}).get("cards", [])
	for card in next_evaluations:
		var inspection_version: Dictionary = card.get("version", {})
		if int(inspection_version.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or \
			str(inspection_version.get("runId", "")) != run_id or str(inspection_version.get("combatId", "")) != str(next_combat.get("combatId", "")) or \
			int(inspection_version.get("combatStep", -1)) != int(next_combat.get("step", 0)) or str(inspection_version.get("contentRevision", "")) != str(next_run.get("contentRevision", "")):
			return _refresh_failed({"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."})
	var next_attributes: Dictionary = responses.attributes.data if responses.has("attributes") else {}
	if not next_attributes.is_empty():
		if str(next_attributes.get("runId", "")) != run_id or int(next_attributes.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or str(next_attributes.get("contentRevision", "")) != str(next_run.get("contentRevision", "")):
			return _refresh_failed({"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."})
		if not next_combat.is_empty() and (str(next_attributes.get("combatId", "")) != str(next_combat.get("combatId", "")) or int(next_attributes.get("combatStep", -1)) != int(next_combat.get("step", 0))):
			return _refresh_failed({"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."})
	var next_presentations: Dictionary = responses.cardPresentations.data if responses.has("cardPresentations") else {}
	if not next_presentations.is_empty() and (str(next_presentations.get("runId", "")) != run_id or int(next_presentations.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or str(next_presentations.get("contentRevision", "")) != str(next_run.get("contentRevision", ""))):
		return _refresh_failed({"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."})
	var next_items: Dictionary = responses.modifierItems.data if responses.has("modifierItems") else {}
	if not next_items.is_empty() and (str(next_items.get("runId", "")) != run_id or int(next_items.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or str(next_items.get("contentRevision", "")) != str(next_run.get("contentRevision", ""))):
		return _refresh_failed({"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."})
	_modifier_items = next_items.duplicate(true)
	_card_presentations = next_presentations.duplicate(true)
	_character_attributes = next_attributes.duplicate(true)
	_run = next_run.duplicate(true)
	_combat = next_combat.duplicate(true)
	_card_zones = responses.cardZones.data.duplicate(true)
	_available_commands = responses.commands.data.get("commands", []).duplicate(true)
	_legal_actions = next_actions.duplicate(true)
	_card_evaluations = next_evaluations.duplicate(true)
	_card_evaluation_context = _evaluation_context(_run, _combat)
	_tool_capabilities = {}
	tool_profile = str(responses.capabilities.data.get("profile", "normal"))
	for capability in responses.capabilities.data.get("granted", []):
		_tool_capabilities[str(capability)] = true
	synchronized = true
	changed.emit()
	if not _accepted_presentation.is_empty():
		var accepted: Dictionary = _accepted_presentation
		_accepted_presentation = {}
		var envelope = preload("res://scripts/presentation/presentation_envelope.gd").build(accepted.receipt, accepted.before,
			{"run": run, "combat": combat, "zones": card_zones, "evaluations": card_evaluations}, accepted.endpoint)
		if envelope != null:
			for marker in refresh_timings: envelope.mark_at(marker, refresh_timings[marker])
			envelope.mark_at("submitted", int(accepted.get("submitted", 0)))
			envelope.mark_at("received", int(accepted.get("received", 0)))
			presentation_ready.emit(envelope)
		else: failed.emit({"errorKey": "Presentation was superseded by a newer game revision."})
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
	return [selected_setting_id, run_state.get("runId", ""), run_state.get("branchId", ""),
		run_state.get("sequence", -1), run_state.get("contentRevision", ""),
		combat_state.get("combatId", ""), combat_state.get("step", -1), _input_actor_id(combat_state)]

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
	_accepted_presentation = {"receipt": response.data.duplicate(true), "before": {"run": run, "combat": combat, "zones": card_zones},
		"endpoint": replay_context() if _gateway.has_method("replay_context") else ["fixture", _generation],
		"submitted": _command_started_ms, "received": Time.get_ticks_msec()}
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

func allows_tool(capability: String) -> bool:
	return _tool_capabilities.has(capability)

func _tool_denied(capability: String) -> Dictionary:
	return {
		"ok": false,
		"status": 403,
		"errorKey": "This tool is not available in the current mode.",
		"error": "Tool capability denied: %s" % capability
	}

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
	var player := str(launch.get("playerEntityId", "player"))
	var mode := str(launch.get("modeId", ""))
	var generation := _generation
	var ids: Array[String] = []
	var bookmarked: String = index.candidate(api, player, setting, mode)
	if bookmarked.is_empty() and index.has_scope(api, player, setting, mode):
		return {"ok": true, "runId": "", "legacyChecked": false}
	if not bookmarked.is_empty(): ids.append(bookmarked)
	if not legacy_id.is_empty() and legacy_id not in ids: ids.append(legacy_id)
	var legacy_checked := false
	for id in ids:
		var response: Dictionary = await _gateway.read_run(id)
		if generation != _generation or selected_setting_id != setting:
			return {"ok": false, "errorKey": "The selected setting changed. Try again."}
		if not response.ok:
			if int(response.get("status", 0)) != 404: return response
			index.forget(api, player, setting, mode, id)
			if index.has_scope(api, player, setting, mode): return {"ok": true, "runId": "", "legacyChecked": legacy_checked}
			continue
		var record: Dictionary = response.data
		if str(record.get("runId", "")) != id:
			return {"ok": false, "errorKey": "This journey does not match the selected setting, player or mode, or is no longer active."}
		if id == legacy_id: legacy_checked = true
		index.remember(api, record) # Import only verified metadata, never the menu's selection.
		if str(record.get("settingId", record.get("configName", ""))) == setting \
			and str(record.get("playerEntityId", "")) == player and str(record.get("modeId", "")) == mode \
			and str(record.get("lifecycle", "")).to_lower() == "active":
			return {"ok": true, "runId": id, "legacyChecked": legacy_checked}
		index.forget(api, player, setting, mode, id)
		if index.has_scope(api, player, setting, mode) and index.candidate(api, player, setting, mode).is_empty():
			return {"ok": true, "runId": "", "legacyChecked": legacy_checked}
	var profile: Dictionary = await _gateway.run_history(player, setting)
	if generation != _generation or selected_setting_id != setting:
		return {"ok": false, "errorKey": "The selected setting changed. Try again."}
	if not profile.ok: return profile
	# Old saves lack a recency bookmark. Prefer the most progressed active matching
	# journey (the profile's deterministic order); all others remain in history.
	for record in profile.data.get("items", []):
		if str(record.get("settingId", record.get("configName", ""))) != setting \
			or str(record.get("modeId", "")) != mode or str(record.get("lifecycle", "")).to_lower() != "active": continue
		var metadata: Dictionary = record.duplicate(true)
		metadata["playerEntityId"] = player
		index.remember(api, metadata)
		return {"ok": true, "runId": str(record.get("runId", "")), "legacyChecked": legacy_checked}
	return {"ok": true, "runId": "", "legacyChecked": legacy_checked}

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
