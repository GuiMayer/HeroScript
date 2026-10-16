extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var camera = load("res://scripts/ui/map_camera.gd").new()
	var act := Rect2(24, 20, 800, 3400)
	for viewport in [Vector2(1000,420), Vector2(1800,700), Vector2(2400,700), Vector2(2400,1000)]:
		var zoom: float = camera.fit_scale(act, viewport)
		check(act.size.x * zoom <= viewport.x - 47.99 and act.size.y * zoom <= viewport.y - 47.99, "whole-act fit includes every node at %s" % viewport)
		var offset: Vector2 = camera.offset_for(Vector2(500,1800), viewport, act.size)
		check(offset.x >= 0 and offset.y >= 0 and offset.y <= maxf(0, act.size.y - viewport.y), "camera clamps to valid content range")
	check(camera.fit_scale(Rect2(0,0,800,240),Vector2(1000,420), .6) == 1, "six-choice decision retains full-size hitboxes")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
