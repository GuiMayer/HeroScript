extends SceneTree
var failures := 0
class Gateway extends RefCounted:
	var calls := 0
	var overrides: Dictionary = {}
	func prepare_command(_scope, _id, _type, _payload, _sequence, _step) -> Dictionary: return {"commandId": "confirmed"}
	func send_prepared(_command: Dictionary) -> Dictionary:
		return {"ok": true, "data": {"sequence": 1, "state": {"resolution": null, "combat": null}}}
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
	var captured: Dictionary = session.capture_view_snapshot()
	captured.run.sequence = 888; captured.zones.zones.append({"zoneId": "injected"})
	check(session.run.sequence == 1 and session.card_zones.zones.is_empty(), "batch snapshot mutation cannot alter session or other readers")
	for change in [{"runId": "other"}, {"settingId": "other"}, {"runSequence": 2}, {"runStep": 8}, {"engineVersion": "other"}, {"contentRevision": "other"}, {"stateFingerprint": "other"}]:
		gateway.overrides = change
		check(not await session.refresh() and session.run.sequence == 1 and not session.synchronized, "inconsistent bundle cannot publish: %s" % change.keys()[0])
	gateway.overrides = {}
	check(await session.refresh() and session.synchronized, "recovery publishes a complete coherent revision")
	var finished: Array = []
	var presentations: Array = []
	session.command_finished.connect(func(): finished.append(true))
	session.presentation_ready.connect(func(value): presentations.append(value))
	check(await session.execute_run_command("TEST", {}), "accepted command with null optional presentation data completes")
	check(not session.busy and session.synchronized and not session.has_pending_command and finished.size() == 1, "null resolution releases session operation and pending-command gates without reopening the menu")
	check(presentations.size() == 1 and presentations[0].snapshot("combat").is_empty(), "no-animation receipt publishes a coherent envelope rather than hanging the caller")
	check(await session.refresh() and not session.busy, "next session operation is immediately usable")
	session.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
