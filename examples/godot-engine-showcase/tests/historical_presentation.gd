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
	var context = preload("res://scripts/ui/visual_context.gd").new(); context.setting_id = "default"
	commit.stateAfter.settingId = "ascendant"; before = JSON.stringify(commit)
	var view = preload("res://scripts/ui/historical_beat_view.gd").new(context); root.add_child(view)
	view.load_commit(commit, {})
	check(context.setting_id == "default" and view.visual_context.setting_id == "ascendant", "historical motion uses the saved setting without changing the live context")
	view.director._process(.01)
	check(not view.blocking(), "historical facts without an actor or animation settle immediately")
	view.director.skip()
	check(not view.blocking() and JSON.stringify(commit) == before, "historical skip changes neither recorded facts nor live session")
	view.history.apply_frame_snapshot({"combatId": "finished", "actors": [{"instanceId": "transient", "definitionId": "spire_adept", "resources": {}}]})
	view.history.apply_frame_snapshot({"combatId": "finished", "actors": []})
	check(view.history.actor_panels.is_empty(), "published absence removes historical actors without inferring death rules")
	var raw := {"actors": {"hero": {"instanceId": "hero", "definitionId": "spire_adept", "components": {"resources": {"type": "resources", "state": {"resources": {"custom": {"current": 7, "maximum": 10}}}}}}}, "actorOrder": ["hero"], "activationState": null, "phaseState": null}
	var raw_before := JSON.stringify(raw)
	view.history.apply_frame_snapshot(raw)
	check(view.history.actor_panels.has("hero") and view.history.presenter.actors()[0].resources.custom.current == 7 and JSON.stringify(raw) == raw_before, "aggregate snapshots adapt once without mutating components or inventing rules")
	view.queue_free(); for _frame in 8: await process_frame
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
