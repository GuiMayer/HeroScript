extends SceneTree
const Planner = preload("res://scripts/presentation/presentation_planner.gd")
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var frame := {"frameId": "frame", "stateAfter": {"step": 2}, "effectSteps": [
		{"identity": {"procId": "root"}, "condensation": {}}, {"identity": {"procId": "child", "parentProcId": "root"}}],
		"applications": [{"identity": {"procId": "child"}, "id": "A1"}, {"identity": {"procId": "B"}, "id": "B"}, {"identity": {"procId": "child"}, "id": "A2"}]}
	var source := JSON.stringify(frame)
	var beats: Array = Planner.plan([frame])
	check(beats.map(func(beat): return beat.applications[0].id) == ["A1", "B", "A2"], "interleaved descendants remain in canonical order")
	check(beats[0].condensed and beats[2].condensed and not beats[1].condensed, "condensation ancestry is consumed without classifying unrelated applications")
	check(not beats[0].has("stateAfter") and beats[-1].stateAfter.step == 2, "whole-frame snapshot is applied only at its final beat")
	frame.applications.remove_at(1)
	check(Planner.plan([frame]).size() == 1, "contiguous outputs of one condensation stay one activation")
	check(JSON.stringify(beats[0].effectSteps) == JSON.stringify(frame.effectSteps) and source.contains("A1"), "trace references remain available without recomputation")
	var empty: Array = Planner.plan([{"transitionType": "phase"}])
	check(not empty[0].hasVisibleFacts, "empty lifecycle beat has no imposed visual delay")
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
