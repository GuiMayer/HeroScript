extends SceneTree
var failures := 0
class Options extends RefCounted:
	var auto_animations := true
	var animation_speed := 1.0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var cursor = preload("res://scripts/presentation/playback.gd").new()
	var pause := {"value": false}; var options := Options.new()
	var director = preload("res://scripts/ui/controllers/presentation_director.gd").new(cursor, options, func(): return pause.value)
	var handles: Array = []; var outcomes: Array = []
	cursor.executor = func(_frame, handle): handles.append(handle)
	cursor.settled.connect(func(outcome, _generation): outcomes.append(outcome))
	cursor.load_receipt({"state": {"resolution": {"frames": [{}, {}]}}})
	director._process(.01)
	check(cursor.index == 1 and cursor.has_blocking_work() and not director.advance(), "active operation prevents overlapping steps")
	pause.value = true; director._process(20.)
	check(handles[0].elapsed == 0 and cursor.index == 1, "pause freezes watchdog and execution")
	pause.value = false; handles[0].hit(); handles[0].finish(); director._process(.01)
	check(cursor.index == 2 and cursor.has_blocking_work() and not cursor.has_frames(), "final dispatch remains blocking")
	handles[1].finish(); handles[1].finish("cancelled")
	check(not cursor.has_blocking_work() and outcomes == ["completed"], "completion is exactly once")
	cursor.load_receipt({"state": {"resolution": {"frames": [{}]}}}); director._process(.01)
	director.skip()
	check(outcomes[-1] == "skipped" and handles[-1].done, "skip settles visual work without an engine command")
	cursor.load_receipt({"state": {"resolution": {"frames": [{}]}}}); director._process(.01)
	var obsolete = handles[-1]; cursor.clear(); obsolete.finish()
	check(not cursor.has_blocking_work() and obsolete.result == "cancelled", "obsolete completion cannot release a new context")
	options.auto_animations = false; cursor.load_receipt({"state": {"resolution": {"frames": [{}]}}}); director._process(50.)
	check(cursor.index == 0, "manual wait is not a watchdog failure")
	director.advance(); director._process(20.)
	check(not cursor.has_blocking_work() and outcomes[-1] == "fallback", "broken executor has a bounded safe fallback")
	cursor.load_receipt({"state": {"resolution": {"frames": [{"transitionType": "activation-start"}]}}}); director._process(.01)
	check(cursor.index == 1, "empty lifecycle facts do not request a manual confirmation")
	director.dispose(); director.free(); cursor.free()
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
