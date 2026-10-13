extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var commit := {"runId": "archived", "sequence": 7, "combatResolution": {"combatId": "finished"}, "stateAfter": {"encounters": [{"combat": {"combatId": "finished", "actors": []}}]},
		"frames": [{"scope": "combat", "kind": "effect", "applications": [{"resourceId": "custom", "previousValue": 4, "currentValue": 2}]}]}
	var before := JSON.stringify(commit)
	var projection = preload("res://scripts/presentation/historical_presentation.gd")
	check(projection.state(commit).combat.combatId == "finished" and projection.frames(commit).size() == 1, "archived combat is read by canonical commit, independent of current encounter")
	var view = preload("res://scripts/ui/historical_beat_view.gd").new(); root.add_child(view)
	view.load_commit(commit, {})
	view.director._process(.01)
	check(view.blocking(), "historical reader uses the same blocking completion contract")
	view.director.skip()
	check(not view.blocking() and JSON.stringify(commit) == before, "historical skip changes neither recorded facts nor live session")
	view.queue_free(); for _frame in 8: await process_frame
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
