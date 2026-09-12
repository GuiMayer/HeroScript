extends RefCounted
## REST contract adapter. No UI, localization, preferences or animation dependencies.

var _transport

func _init(transport) -> void:
	_transport = transport

func connect_engine() -> Dictionary:
	var health: Dictionary = await _transport.request(HTTPClient.METHOD_GET, "/api/v1/health/ready")
	if not health.ok:
		return health
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/content/revisions?configName=default")

func create_campaign(seed: int, revision: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST, "/api/v1/runs", {
		"schemaVersion": 1, "settingId": "default", "configName": "default",
		"runDefinitionId": "spire_showcase_run", "playerEntityId": "player",
		"modeId": "spire_showcase", "contentRevision": revision, "seed": seed
	})

func create_sandbox(mode_id: String, scenario: Dictionary, seed: int, revision: String) -> Dictionary:
	var body := scenario.duplicate(true)
	body.merge({"schemaVersion": 2, "modeId": mode_id, "contentRevision": revision,
		"seed": seed, "attemptKey": "godot-%s-%s" % [mode_id, _uuid()]}, true)
	return await _transport.request(HTTPClient.METHOD_POST, "/api/v1/sandbox/runs", body)

func read_run(run_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s" % run_id.uri_encode())

func read_projections(run_id: String, combat_id: String, include_combat: bool) -> Dictionary:
	var paths := {"commands": "/api/v1/runs/%s/available-commands" % run_id.uri_encode()}
	if include_combat:
		paths["combat"] = "/api/v1/combats/%s" % combat_id.uri_encode()
	return await _transport.request_many(paths)

func read_legal_actions(combat_id: String, actor_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/combats/%s/legal-actions?actorId=%s" % [combat_id.uri_encode(), actor_id.uri_encode()])

func prepare_command(scope: String, id: String, type: String, payload: Dictionary, sequence: int, step: int) -> Dictionary:
	assert(scope in ["runs", "combats"])
	return {"scope": scope, "id": id, "envelope": {
		"commandId": _uuid(), "expectedSequence": sequence, "expectedStep": step,
		"type": type, "payload": payload.duplicate(true)}}

func send_prepared(command: Dictionary) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST,
		"/api/v1/%s/%s/commands" % [command.scope, str(command.id).uri_encode()], command.envelope.duplicate(true))

func timeline(combat_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/combats/%s/timeline?afterSequence=0&limit=200" % combat_id.uri_encode())

func branch_tree(run_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/%s/branch-tree" % run_id.uri_encode())

func create_branch(combat_id: String, sequence: int, key: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST,
		"/api/v1/combats/%s/timeline/%s/branches" % [combat_id.uri_encode(), sequence], {"branchKey": key})

func verify(run_id: String) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST, "/api/v1/runs/%s/verify" % run_id.uri_encode())

func simulate(run_id: String, sequence: int, commands: Array) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_POST, "/api/v1/simulations", {
		"sourceRunId": run_id, "sourceSequence": sequence, "commands": commands.duplicate(true)
	})

func content(kind: String, revision: String, limit: int) -> Dictionary:
	return await _transport.request(HTTPClient.METHOD_GET,
		"/api/v1/content/%s?revision=%s&configName=default&limit=%s" % [kind.uri_encode(), revision.uri_encode(), limit])

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
