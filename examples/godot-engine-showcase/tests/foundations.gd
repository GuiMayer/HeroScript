extends SceneTree
## Offline transport contract. No real requests, runs or preference writes.
var failures := 0
class Transport extends RefCounted:
	var fail_next := true
	var calls: Array = []
	func request(_method, _path, body = null) -> Dictionary:
		calls.append(body.duplicate(true) if body is Dictionary else {})
		if fail_next:
			fail_next = false
			return {"ok": false, "status": 0}
		return {"ok": true, "status": 200, "data": {}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var transport := Transport.new()
	var gateway = preload("res://scripts/engine/engine_gateway.gd").new(transport)
	var setting := {"settingId": "ascendant", "currentRevision": "revision", "launch": {"modeId": "campaign", "runDefinitionId": "act", "playerEntityId": "player"}}
	await gateway.create_campaign(42, setting)
	await gateway.create_campaign(42, setting)
	check(transport.calls[0] == transport.calls[1], "uncertain creation retries identical inputs and command id")
	check(not transport.calls[0].has("schemaVersion") and transport.calls[0].has("commandId"), "canonical creation contains no rejected schemaVersion")
	await gateway.create_campaign(42, setting)
	check(transport.calls[1].commandId != transport.calls[2].commandId, "new attempt after success receives a new retry key")
	check(transport.calls[0].settingId == "ascendant" and transport.calls[0].modeId == "campaign", "transport preserves setting and mode")
	print("FOUNDATIONS: ", failures, " failure(s)")
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
