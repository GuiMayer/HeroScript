extends SceneTree
var failures := 0
class Gateway extends RefCounted:
	var calls := 0
	var overrides: Dictionary = {}
	func read_session(_id: String, _expected := -1) -> Dictionary:
		calls += 1
		var version := {"runId": "run", "settingId": "setting", "runSequence": 1, "runStep": 7,
			"engineVersion": "31", "contentRevision": "revision", "stateFingerprint": "hash"}
		version.merge(overrides, true)
		return {"ok": true, "data": {"version": version,
			"run": {"runId": "run", "settingId": "setting", "sequence": 1, "step": 7, "engineVersion": "31", "contentRevision": "revision", "stateHash": "hash"},
			"combat": null, "commands": {"commands": []}, "capabilities": {"granted": []},
			"cardZones": {"zones": []}, "attributes": {}, "cardPresentations": {}, "modifierItems": {},
			"actions": {"candidates": []}, "evaluations": {"cards": []}}}
func _init() -> void: call_deferred("run_checks")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run_checks() -> void:
	var gateway := Gateway.new()
	var session = preload("res://scripts/application/game_session.gd").new(); root.add_child(session)
	session.configure(gateway); session._run = {"runId": "run"}
	check(await session.refresh() and gateway.calls == 1, "refresh performs one compact read, with no legacy fan-out methods")
	var copy: Dictionary = session.run; copy.sequence = 999
	check(session.run.sequence == 1, "public session data remains defensively isolated")
	for change in [{"runId": "other"}, {"settingId": "other"}, {"runSequence": 2}, {"runStep": 8}, {"engineVersion": "other"}, {"contentRevision": "other"}, {"stateFingerprint": "other"}]:
		gateway.overrides = change
		check(not await session.refresh() and session.run.sequence == 1 and not session.synchronized, "inconsistent bundle cannot publish: %s" % change.keys()[0])
	gateway.overrides = {}
	check(await session.refresh() and session.synchronized, "recovery publishes a complete coherent revision")
	session.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
