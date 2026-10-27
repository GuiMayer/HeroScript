extends SceneTree
var failures := 0
class Gateway extends RefCounted:
	var info := {"ok": true, "data": {"relics": {"runId": "run", "sequence": 2, "relics": []}, "attributes": {"runId": "run", "actorId": "hero", "runSequence": 2, "contentRevision": "revision", "attributes": []}}}
	func replay_context() -> Array: return ["fixture"]
	func read_loadout_info(_run: String) -> Dictionary: return info.duplicate(true)
	func read_loadout(_run: String, _actor: String) -> Dictionary: return {"ok": true, "data": null}
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func _run() -> void:
	var session = load("res://scripts/application/game_session.gd").new(); var gateway := Gateway.new(); session.configure(gateway)
	session._run = {"runId": "run", "playerEntityId": "hero", "sequence": 2, "step": 0, "contentRevision": "revision"}
	check((await session.read_loadout_info()).get("ok", false), "consistent canonical read is accepted")
	gateway.info.data.attributes.runId = "another-run"
	check(not (await session.read_loadout_info()).get("ok", false), "same actor and sequence cannot admit another run's attributes")
	gateway.info.data.attributes.runId = "run"; gateway.info.data.attributes.actorId = "another-actor"
	check(not (await session.read_loadout_info()).get("ok", false), "foreign actor attributes are rejected")
	gateway.info.data.attributes.actorId = "hero"; gateway.info.data.relics.sequence = 1
	check(not (await session.read_loadout_info()).get("ok", false), "mixed sequence projections are rejected")
	gateway.info.data.attributes = null
	check(not (await session.read_loadout_info()).get("ok", false), "null attribute payload is handled as a failed read")
	check(not (await session.read_loadout("hero")).get("ok", false), "null loadout response cannot lock the screen with a script exception")
	session.free(); quit(1 if failures else 0)
