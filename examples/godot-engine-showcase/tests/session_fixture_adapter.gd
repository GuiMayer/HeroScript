extends RefCounted
class TransportFixture extends RefCounted:
	var transport
	func _init(value) -> void: transport = value
	func read_run(id: String) -> Dictionary: return await transport.request(HTTPClient.METHOD_GET, "/api/v1/runs/" + id)
	func read_projections(id: String, combat: String, include: bool) -> Dictionary:
		var paths := {"commands": "/api/v1/runs/%s/available-commands" % id, "capabilities": "/api/v1/runs/%s/capabilities" % id,
			"cardZones": "/api/v1/runs/%s/card-zones" % id, "attributes": "/api/v1/runs/%s/character-attributes" % id,
			"cardPresentations": "/api/v1/runs/%s/cards/presentations" % id, "modifierItems": "/api/v1/runs/%s/card-modifier-items" % id}
		if include: paths["combat"] = "/api/v1/combats/" + combat
		return await transport.request_many(paths)
	func read_legal_actions(combat: String, actor: String) -> Dictionary:
		return await transport.request(HTTPClient.METHOD_GET, "/api/v1/combats/%s/legal-actions?actorId=%s" % [combat, actor])
	func inspect_hand(combat: String, actor: String, _targets: Array) -> Dictionary:
		return await transport.request(HTTPClient.METHOD_GET, "/api/v1/combats/%s/cards/evaluations?actorId=%s" % [combat, actor])
static func from_transport(transport, path: String) -> Dictionary:
	var id: String = path.split("/runs/")[1].split("/")[0]
	var expected := int(path.split("expectedSequence=")[1]) if "expectedSequence=" in path else -1
	return await read(TransportFixture.new(transport), id, expected)
## Test-only bridge for delayed legacy fixtures. Production uses one REST session read.
static func read(fixture, run_id: String, expected_sequence: int) -> Dictionary:
	var response: Dictionary = await fixture.read_run(run_id)
	if not response.ok: return response
	var run: Dictionary = response.data.duplicate(true)
	if expected_sequence >= 0 and int(run.get("sequence", -1)) != expected_sequence:
		return {"ok": false, "status": 409, "error": "Session revision conflict"}
	var encounter := str(run.get("activeEncounterId", "")) if run.get("activeEncounterId") != null else ""
	var parts: Dictionary = await fixture.read_projections(run_id, encounter, not encounter.is_empty())
	for part in parts.values():
		if not part.ok: return part
	var combat: Dictionary = parts.get("combat", {}).get("data", {})
	var actions: Dictionary = {"candidates": []}
	var evaluations: Dictionary = {"cards": []}
	if not combat.is_empty() and str(combat.get("status", "ACTIVE")) == "ACTIVE":
		var actor: String = preload("res://scripts/application/game_session.gd")._input_actor_id(combat)
		var options: Dictionary = await fixture.read_legal_actions(encounter, actor)
		if not options.ok: return options
		actions = options.data if options.data is Dictionary else {"candidates": options.data}
		var targets: Array = []
		for candidate in actions.get("candidates", []):
			var selected: Array = candidate.get("command", {}).get("targetIds", [])
			if not selected.is_empty(): targets = [selected[0]]; break
		var inspected: Dictionary = await fixture.inspect_hand(encounter, actor, targets)
		if not inspected.ok: return inspected
		evaluations = inspected.data
	var data := {"run": run, "combat": combat, "actions": actions, "evaluations": evaluations}
	for key in ["commands", "capabilities", "cardZones", "attributes", "cardPresentations", "modifierItems"]:
		data[key] = parts.get(key, {}).get("data", {})
	run["stateHash"] = run.get("stateHash", "immutable-fixture")
	run["engineVersion"] = run.get("engineVersion", "fixture")
	data["version"] = {"runId": run.get("runId", ""), "runSequence": run.get("sequence", 0),
		"runStep": run.get("step", 0), "engineVersion": run.engineVersion,
		"settingId": run.get("settingId", run.get("configName", "")), "contentRevision": run.get("contentRevision", ""),
		"combatId": combat.get("combatId", ""), "combatStep": combat.get("step", 0), "stateFingerprint": run.stateHash}
	return {"ok": true, "data": data}
