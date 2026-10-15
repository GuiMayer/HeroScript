extends RefCounted
## REST contract adapter. No UI, localization, preferences or animation dependencies.

var _transport
var _campaign_request: Dictionary = {}

func _init(transport) -> void:
	_transport = transport

func connect_engine() -> Dictionary:
	var health: Dictionary = await _transport.request(HTTPClient.METHOD_GET, "/api/v1/health/ready")
	if not health.ok:
		return health
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/content/settings")

func create_campaign(seed: int, setting: Dictionary) -> Dictionary:
	var launch: Dictionary = setting.get("launch", {})
	var setting_id := str(setting.get("settingId", ""))
	var body := {
		"settingId": setting_id,
		"runDefinitionId": str(launch.get("runDefinitionId", "")),
		"playerEntityId": str(launch.get("playerEntityId", "player")),
		"modeId": str(launch.get("modeId", "")),
		"contentRevision": str(setting.get("currentRevision", "")), "seed": seed
	}
	# Retain identical inputs after an uncertain response; success ends this attempt's retry window.
	var previous := _campaign_request.duplicate(true)
	previous.erase("commandId")
	if previous != body:
		_campaign_request = body.duplicate(true)
		_campaign_request["commandId"] = _uuid()
	var response: Dictionary = await _transport.request(HTTPClient.METHOD_POST, "/api/v1/runs", _campaign_request.duplicate(true))
	if response.ok or (int(response.get("status", 0)) >= 400 and int(response.get("status", 0)) < 500):
		_campaign_request = {}
	return response

func create_sandbox(mode_id: String, scenario: Dictionary, seed: int, revision: String, setting_id: String) -> Dictionary:
	var body := scenario.duplicate(true)
	body.merge({"schemaVersion": 2, "modeId": mode_id, "contentRevision": revision,
		"seed": seed, "attemptKey": "godot-%s-%s" % [mode_id, _uuid()]}, true)
	return await _transport.request(HTTPClient.METHOD_POST,
		"/api/v1/sandbox/runs?configName=%s" % setting_id.uri_encode(), body)

func read_run(run_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s" % run_id.uri_encode())

func read_loadout(run_id: String, actor_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s/loadout?actorId=%s" % [run_id.uri_encode(), actor_id.uri_encode()])

func preview_loadout(snapshot: Dictionary, assignments: Dictionary) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST, "/api/v1/runs/%s/loadout/preview" % str(snapshot.runId).uri_encode(), {
		"actorId": snapshot.actorId, "assignments": assignments.duplicate(true),
		"expectedSequence": snapshot.runSequence, "expectedStep": snapshot.expectedStep})

func read_session(run_id: String, expected_sequence := -1) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s/session%s" % [run_id.uri_encode(),
		"?expectedSequence=%s" % expected_sequence if expected_sequence >= 0 else ""])

func read_card_presentations(run_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s/cards/presentations" % run_id.uri_encode())

func run_history(player_id: String, setting_id: String) -> Dictionary:
	var response: Dictionary = await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/profiles/%s?settingId=%s" % [player_id.uri_encode(), setting_id.uri_encode()])
	if response.ok and (str(response.data.get("settingId", "")) != setting_id or str(response.data.get("playerId", "")) != player_id):
		return {"ok": false, "errorKey": "Restart the engine to enable setting-specific profiles."}
	if response.ok:
		response.data = {
			"playerId": response.data.get("playerId", player_id),
			"settingId": response.data.get("settingId", setting_id),
			"revision": response.data.get("revision", ""),
			"totalRuns": response.data.get("totalRuns", 0),
			"completedRuns": response.data.get("completedRuns", 0),
			"activeRuns": response.data.get("activeRuns", 0),
			"unlocks": response.data.get("unlocks", []).duplicate(true),
			"progressSequence": response.data.get("progressSequence", 0),
			"progressRevision": response.data.get("progressRevision", ""),
			"unlockProofs": response.data.get("unlockProofs", []).duplicate(true),
			"availability": response.data.get("availability", {}).duplicate(true) if response.data.get("availability") is Dictionary else {},
			"achievements": response.data.get("achievements", []).duplicate(true),
			"items": response.data.get("runs", []).duplicate(true)
		}
	return response

func run_timeline(run_id: String, after_sequence := 0, limit := 25, through_sequence := 0) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET,
		_timeline_path(run_id, after_sequence, limit, through_sequence))

func _timeline_path(run_id: String, after_sequence: int, limit: int, through_sequence: int) -> String:
	return "/api/v1/runs/%s/timeline?afterSequence=%s&limit=%s%s" % [run_id.uri_encode(), after_sequence, limit,
		"&throughSequence=%s" % through_sequence if through_sequence > 0 else ""]
func replay_context() -> Array: return _transport.endpoint_context()
func begin_run_timeline(run_id: String, after_sequence: int, limit: int, through_sequence: int):
	return _transport.read(_timeline_path(run_id, after_sequence, limit, through_sequence))
func begin_run_presentation(run_id: String, sequence: int):
	return _transport.read("/api/v1/runs/%s/commits/%s/presentation" % [run_id.uri_encode(), sequence])
func begin_run_commit(run_id: String, sequence: int):
	return _transport.read("/api/v1/runs/%s/commits/%s" % [run_id.uri_encode(), sequence])

func run_commit(run_id: String, sequence: int) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/runs/%s/commits/%s" % [run_id.uri_encode(), sequence])

func read_legal_actions(combat_id: String, actor_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/combats/%s/legal-actions?actorId=%s" % [combat_id.uri_encode(), actor_id.uri_encode()])

func modifier_item_preview(run_id: String, item_id: String, card_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s/card-modifier-items/%s/preview/%s" % [run_id.uri_encode(), item_id.uri_encode(), card_id.uri_encode()])

func prepare_command(scope: String, id: String, type: String, payload: Dictionary, sequence: int, step: int) -> Dictionary:
	assert(scope in ["runs", "combats"])
	return {"scope": scope, "id": id, "envelope": {
		"commandId": _uuid(), "expectedSequence": sequence, "expectedStep": step,
		"type": type, "payload": payload.duplicate(true)}}

func send_prepared(command: Dictionary) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST,
		"/api/v1/%s/%s/commands" % [command.scope, str(command.id).uri_encode()], command.envelope.duplicate(true))

func timeline(combat_id: String, after_sequence := 0, limit := 50) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/combats/%s/timeline?afterSequence=%s&limit=%s" % [combat_id.uri_encode(), after_sequence, limit])

func branch_tree(run_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s/branch-tree" % run_id.uri_encode())

func create_branch(combat_id: String, sequence: int, key: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST,
		"/api/v1/combats/%s/timeline/%s/branches" % [combat_id.uri_encode(), sequence], {"branchKey": key})

func verify(run_id: String) -> Dictionary:
	# Re-executing a whole journey is not a normal interactive command.
	return await _transport.request(HTTPClient.METHOD_POST, "/api/v1/runs/%s/verify" % run_id.uri_encode(), null, 120.0)

func simulate(run_id: String, sequence: int, commands: Array) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST, "/api/v1/simulations", {
		"sourceRunId": run_id, "sourceSequence": sequence, "commands": commands.duplicate(true)
	})

func content(kind: String, revision: String, setting_id: String, limit: int) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/content/%s?revision=%s&configName=%s&limit=%s" % [
			kind.uri_encode(), revision.uri_encode(), setting_id.uri_encode(), limit])

static func candidate_payload(command: Dictionary) -> Dictionary:
	var payload := {}
	for key in ["actorId", "actionType", "powerId", "targetId", "targetIds", "costOptionId", "cardInstanceId"]:
		if command.has(key) and command[key] != null:
			payload[key] = command[key]
	return payload.duplicate(true)

func _uuid() -> String:
	var bytes := Crypto.new().generate_random_bytes(16)
	bytes[6] = (bytes[6] & 0x0f) | 0x40
	bytes[8] = (bytes[8] & 0x3f) | 0x80
	var hex := bytes.hex_encode()
	return "%s-%s-%s-%s-%s" % [hex.substr(0, 8), hex.substr(8, 4), hex.substr(12, 4), hex.substr(16, 4), hex.substr(20, 12)]

func inspect_hand(combat_id: String, actor_id: String, target_ids := []) -> Dictionary:
	var path := "/api/v1/combats/%s/cards/evaluations?actorId=%s" % [combat_id.uri_encode(), actor_id.uri_encode()]
	for target_id in target_ids:
		path += "&targetIds=%s" % str(target_id).uri_encode()
	return await _transport.request(HTTPClient.METHOD_GET, path)
func inspect_card(combat_id: String, actor_id: String, instance_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/combats/%s/cards/%s/evaluation?actorId=%s" % [combat_id.uri_encode(), instance_id.uri_encode(), actor_id.uri_encode()])

func transformation_preview(run_id: String, card_id: String, operation: String, upgrade_id := "", transformation_id = null) -> Dictionary:
	var path := "/api/v1/runs/%s/cards/%s/transformation-preview?operation=%s" % [run_id.uri_encode(), card_id.uri_encode(), operation.uri_encode()]
	if not upgrade_id.is_empty(): path += "&upgradeId=" + upgrade_id.uri_encode()
	if transformation_id != null: path += "&transformationId=" + str(transformation_id).uri_encode()
	return await _transport.request(HTTPClient.METHOD_GET, path)

func historical_state(combat_id: String, sequence: int) -> Dictionary:
	var response: Dictionary = await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/combats/%s/timeline/%s/state" % [combat_id.uri_encode(), sequence])
	if response.ok: response.data = normalize_history(response.data)
	return response

func resolution(combat_id: String, command_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/combats/%s/resolutions/%s" % [combat_id.uri_encode(), command_id.uri_encode()])

static func normalize_history(data: Dictionary) -> Dictionary:
	# Historical endpoints return the aggregate, while live endpoints return DTOs.
	# Adapt their shapes here; never synthesize or execute historical transitions.
	var result := data.duplicate(true)
	result["combat"] = preload("res://scripts/contracts/snapshot_projection.gd").combat(result.get("combat", {}))
	result["run"] = preload("res://scripts/contracts/snapshot_projection.gd").run(result.get("run", {}))
	return result
