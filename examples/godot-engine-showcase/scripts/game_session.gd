extends Node

signal changed
signal command_started(label: String)
signal frame_presented(frame: Dictionary, index: int, total: int)
signal command_finished
signal failed(message: String)

var run: Dictionary = {}
var combat: Dictionary = {}
var available_commands: Array = []
var legal_actions: Array = []
var content_revision := ""
var busy := false
var presentation_queue: Array = []
var presentation_index := 0
var auto_present := true

func connect_engine() -> bool:
	var health := await HeroAPI.health()
	if not health.ok:
		failed.emit(health.error)
		return false
	var revisions := await HeroAPI.request(HTTPClient.METHOD_GET, "/api/v1/content/revisions?configName=default")
	if not revisions.ok:
		failed.emit(revisions.error)
		return false
	content_revision = str(revisions.data.get("currentRevision", ""))
	return not content_revision.is_empty()

func start_campaign(seed: int) -> bool:
	if content_revision.is_empty() and not await connect_engine():
		return false
	var response := await HeroAPI.request(HTTPClient.METHOD_POST, "/api/v1/runs", {
		"schemaVersion": 1,
		"settingId": "default",
		"configName": "default",
		"runDefinitionId": "spire_showcase_run",
		"playerEntityId": "player",
		"modeId": "spire_showcase",
		"contentRevision": content_revision,
		"seed": seed
	})
	if not response.ok:
		failed.emit(response.error)
		return false
	run = response.data
	Preferences.last_run_id = str(run.get("runId", ""))
	Preferences.save()
	await refresh()
	return true

func start_sandbox(mode_id: String, scenario: Dictionary, seed: int) -> bool:
	if content_revision.is_empty() and not await connect_engine():
		return false
	var body := scenario.duplicate(true)
	body["schemaVersion"] = 2
	body["modeId"] = mode_id
	body["contentRevision"] = content_revision
	body["seed"] = seed
	body["attemptKey"] = "godot-%s-%s" % [mode_id, _uuid()]
	var response := await HeroAPI.request(HTTPClient.METHOD_POST, "/api/v1/sandbox/runs", body)
	if not response.ok:
		failed.emit(response.error)
		return false
	run = response.data.get("run", response.data)
	combat = response.data.get("combat", {})
	Preferences.last_run_id = str(run.get("runId", ""))
	Preferences.save()
	await refresh()
	return true

func continue_run(run_id := Preferences.last_run_id) -> bool:
	if run_id.is_empty():
		return false
	var response := await HeroAPI.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s" % run_id)
	if not response.ok:
		failed.emit(response.error)
		return false
	run = response.data
	await refresh()
	return true

func refresh() -> bool:
	if run.is_empty():
		return false
	var run_id := str(run.get("runId", ""))
	var response := await HeroAPI.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s" % run_id)
	if not response.ok:
		failed.emit(response.error)
		return false
	run = response.data
	var encounter_id = run.get("activeEncounterId")
	if encounter_id != null and not str(encounter_id).is_empty():
		var combat_response := await HeroAPI.request(HTTPClient.METHOD_GET,
			"/api/v1/combats/%s" % str(encounter_id))
		combat = combat_response.data if combat_response.ok else {}
	else:
		combat = {}
	var commands_response := await HeroAPI.request(HTTPClient.METHOD_GET,
		"/api/v1/runs/%s/available-commands" % run_id)
	available_commands = commands_response.data.get("commands", []) if commands_response.ok else []
	legal_actions = []
	if not combat.is_empty() and str(combat.get("status", "ACTIVE")) == "ACTIVE":
		var actor_id := input_actor_id()
		if not actor_id.is_empty():
			var actions_response := await HeroAPI.request(HTTPClient.METHOD_GET,
				"/api/v1/combats/%s/legal-actions?actorId=%s" % [str(combat.get("combatId", "")), actor_id.uri_encode()])
			if actions_response.ok:
				legal_actions = actions_response.data.get("candidates", actions_response.data if actions_response.data is Array else [])
	changed.emit()
	return true

func execute_run_command(type: String, payload: Dictionary, label := "") -> bool:
	var advertised := command(type)
	var expected_step := int(advertised.get("expectedStep", run.get("step", 0)))
	return await _execute("/api/v1/runs/%s/commands" % str(run.get("runId", "")), type, payload, label, expected_step)

func execute_combat_command(type: String, payload: Dictionary, label := "") -> bool:
	return await _execute("/api/v1/combats/%s/commands" % str(combat.get("combatId", "")), type, payload, label)

func _execute(path: String, type: String, payload: Dictionary, label: String, advertised_step := -1) -> bool:
	if busy or run.is_empty():
		return false
	busy = true
	command_started.emit(label if not label.is_empty() else type)
	var expected_step = advertised_step if advertised_step >= 0 else (
		combat.get("step", run.get("step", 0)) if path.contains("/combats/") else run.get("step", 0))
	var envelope := {
		"commandId": _uuid(),
		"expectedSequence": int(run.get("sequence", 0)),
		"expectedStep": int(expected_step),
		"type": type,
		"payload": payload
	}
	var response := await HeroAPI.request(HTTPClient.METHOD_POST, path, envelope)
	if not response.ok:
		busy = false
		failed.emit(response.error)
		command_finished.emit()
		return false
	_collect_frames(response.data)
	await refresh()
	busy = false
	command_finished.emit()
	return true

func _collect_frames(receipt: Dictionary) -> void:
	var resolution = receipt.get("resolution", {})
	presentation_queue = resolution.get("frames", []) if resolution is Dictionary else []
	presentation_index = 0
	if auto_present:
		while presentation_index < presentation_queue.size():
			present_next_frame()

func present_next_frame() -> bool:
	if presentation_index >= presentation_queue.size():
		return false
	var frame = presentation_queue[presentation_index]
	frame_presented.emit(frame, presentation_index, presentation_queue.size())
	presentation_index += 1
	return true

func input_actor_id() -> String:
	if combat.is_empty():
		return ""
	var priority = combat.get("priorityWindow")
	if priority is Dictionary and not priority.is_empty():
		return str(priority.get("holderActorId", ""))
	var activation = combat.get("activation", {})
	if activation is Dictionary and bool(activation.get("waitingForInput", false)):
		return str(activation.get("activeActorId", combat.get("activeActorId", "")))
	return str(combat.get("activeActorId", ""))

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
	if combat.is_empty():
		return {"ok": false, "error": "Nenhum combate ativo."}
	return await HeroAPI.request(HTTPClient.METHOD_GET,
		"/api/v1/combats/%s/timeline?afterSequence=0&limit=200" % str(combat.get("combatId", "")))

func branch_tree() -> Dictionary:
	return await HeroAPI.request(HTTPClient.METHOD_GET,
		"/api/v1/runs/%s/branch-tree" % str(run.get("runId", "")))

func create_branch(sequence: int, key: String) -> Dictionary:
	if combat.is_empty():
		return {"ok": false, "error": "Nenhum combate ativo."}
	return await HeroAPI.request(HTTPClient.METHOD_POST,
		"/api/v1/combats/%s/timeline/%s/branches" % [str(combat.get("combatId", "")), sequence],
		{"branchKey": key})

func verify() -> Dictionary:
	return await HeroAPI.request(HTTPClient.METHOD_POST,
		"/api/v1/runs/%s/verify" % str(run.get("runId", "")))

func simulate(commands: Array) -> Dictionary:
	return await HeroAPI.request(HTTPClient.METHOD_POST, "/api/v1/simulations", {
		"sourceRunId": run.get("runId"),
		"sourceSequence": int(run.get("sequence", 0)),
		"commands": commands
	})

func content(kind: String, limit := 100) -> Dictionary:
	return await HeroAPI.request(HTTPClient.METHOD_GET,
		"/api/v1/content/%s?revision=%s&configName=default&limit=%s" % [kind, content_revision, limit])

func _uuid() -> String:
	var bytes := Crypto.new().generate_random_bytes(16)
	bytes[6] = (bytes[6] & 0x0f) | 0x40
	bytes[8] = (bytes[8] & 0x3f) | 0x80
	var hex := bytes.hex_encode()
	return "%s-%s-%s-%s-%s" % [hex.substr(0, 8), hex.substr(8, 4), hex.substr(12, 4), hex.substr(16, 4), hex.substr(20, 12)]
