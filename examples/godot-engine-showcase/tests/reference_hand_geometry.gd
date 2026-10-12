extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var layout = load("res://scripts/ui/hand_layout.gd")
	var style = load("res://data/default_hand_visual_style.tres")
	for count in [0, 1, 2, 3, 5, 10, 20]:
		var model: Dictionary = layout.calculate(count, 1800., Vector2(248, 360), style, "adaptive")
		check(model.rotations.size() == count and model.hitRects.size() == count, "one geometry per instance: %s" % count)
		if count >= 2: check(model.rotations[0] < 0 and model.rotations[-1] > 0 and is_equal_approx(-model.rotations[0], model.rotations[-1]), "fan remains symmetric even with ample space: %s" % count)
		if count == 1: check(model.rotations[0] == 0, "single card is upright")
		var reading: Dictionary = layout.calculate(count, 1000., Vector2(248, 360), style, "reading")
		check(reading.rotations.all(func(angle): return angle == 0), "accessible reading remains flat: %s" % count)
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout; quit(0 if failures == 0 else 1)
