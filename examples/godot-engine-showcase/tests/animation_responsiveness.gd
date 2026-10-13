extends SceneTree
var failures := 0
class Options extends RefCounted:
	var auto_animations := true
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var cursor = preload("res://scripts/presentation/playback.gd").new()
	var options := Options.new()
	var director = preload("res://scripts/ui/controllers/presentation_director.gd").new(cursor, options, func(): return false)
	var frames: Array = []
	for index in 100: frames.append({"transitionType": "effect", "applications": [{"previousValue": 1, "currentValue": 1}]})
	cursor.load_receipt({"state": {"resolution": {"frames": frames}}})
	var ticks := 0
	var start := Time.get_ticks_usec()
	while cursor.has_blocking_work() and ticks < 300:
		director._process(.016); ticks += 1
	print("NO_OP_BASELINE ticks=", ticks, " cpu_us=", Time.get_ticks_usec() - start)
	var baseline := "--baseline" in OS.get_cmdline_user_args()
	check(ticks == 1 or baseline, "100 facts without executors drain without a frame per fact")
	options.auto_animations = false
	cursor.load_receipt({"state": {"resolution": {"frames": frames}}}); director._process(.016)
	check(not cursor.has_blocking_work() or baseline, "manual mode requires no confirmation for absent animations")
	director.dispose(); director.free(); cursor.free()
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
