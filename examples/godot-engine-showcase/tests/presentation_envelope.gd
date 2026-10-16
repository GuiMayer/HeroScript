extends SceneTree
const Envelope = preload("res://scripts/presentation/presentation_envelope.gd")
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var before := {"run": {"runId": "run", "sequence": 1, "branchId": "branch"}}
	var after := {"run": {"runId": "run", "sequence": 2, "branchId": "branch", "settingId": "custom", "contentRevision": "revision"}, "combat": {"combatId": "combat"}}
	var receipt := {"sequence": 2, "state": {"combat": {"combatId": "combat"}, "resolution": {"combatId": "combat", "commandId": "command", "frames": []}}}
	var envelope = Envelope.build(receipt, before, after, ["endpoint", 2])
	check(envelope != null and envelope.scope().has("custom"), "complete accepted revision builds an explicitly scoped envelope")
	var copy: Dictionary = envelope.snapshot("after"); copy.run.sequence = 999
	check(envelope.snapshot("after").run.sequence == 2, "snapshots cannot be mutated through getters")
	for field in ["runId", "branchId"]:
		var other := after.duplicate(true); other.run[field] = "other"
		check(Envelope.build(receipt, before, other, []) == null, "foreign context rejected: " + field)
	var later := after.duplicate(true); later.run.sequence = 3
	check(Envelope.build(receipt, before, later, []) == null, "external sequence advance cannot pair old receipt with a newer result")
	var foreign := before.duplicate(true); foreign.run.settingId = "foreign"
	check(Envelope.build(receipt, foreign, after, []) == null, "foreign setting cannot reuse a run-scoped presentation")
	var optional := receipt.duplicate(true)
	optional.state.resolution = null
	var without_resolution = Envelope.build(optional, before, after, [])
	check(without_resolution != null and without_resolution.snapshot("combat").combatId == "combat", "explicit null resolution does not abort a synchronized combat envelope")
	optional.state.combat = null
	check(Envelope.build(optional, before, after, []).snapshot("combat").combatId == "combat", "null receipt combat falls back to the synchronized snapshot")
	optional.state = null
	check(Envelope.build(optional, before, after, []) != null, "null optional receipt state never raises a typed assignment error")
	var outside := after.duplicate(true); outside.combat = null
	check(Envelope.build(optional, before, outside, []).snapshot("combat").is_empty(), "non-combat commands do not manufacture a combat")
	check(Envelope.build(receipt, before, {"run": null}, []) == null, "invalid run identity is rejected without raising")
	var cursor = preload("res://scripts/presentation/playback.gd").new()
	for nullable in [optional, {"state": {"resolution": null}}]:
		cursor.load_receipt(nullable)
		check(not cursor.has_blocking_work(), "absent animation facts create no frames or blocking duration")
	cursor.free()
	var steps := [{"instanceIds": ["visible", "hidden"], "sourceOrderBefore": ["hidden", "visible"]}]
	var zones := {"zones": [{"contentsVisible": true, "cards": [{"cardInstanceId": "visible"}]}]}
	var projected: Array = Envelope.visible_zone_steps(steps, [zones])
	check(projected[0].instanceIds == ["visible"] and projected[0].instanceIdsHiddenCount == 1 and not projected[0].has("sourceOrderBefore"), "visual transfers do not expose hidden identities or shuffled order")
	check(steps[0].instanceIds.size() == 2 and steps[0].has("sourceOrderBefore"), "visibility projection never modifies recorded facts")
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
