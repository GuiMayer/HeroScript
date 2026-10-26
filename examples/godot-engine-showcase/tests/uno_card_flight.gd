extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var motion = preload("res://scripts/ui/card_use_motion.gd")
	var policy = preload("res://scripts/presentation/motion_policy.gd").capture("default", {})
	var start := {"center": Vector2(100, 500), "rotation": -.15, "scale": Vector2.ONE, "size": Vector2(180, 270)}
	var target := {"center": Vector2(600, 200), "rotation": .08, "scale": Vector2(.82, .82)}
	var unchanged := start.duplicate(true)
	for key in ["card_flight", "retain"]:
		var first: Dictionary = motion.sample_pose(start, target, 0, policy, key)
		var last: Dictionary = motion.sample_pose(start, target, 1, policy, key)
		check(first.center.distance_to(start.center) < .001 and first.scale == start.scale and absf(first.rotation-start.rotation) < .001, key + " starts exactly at the borrowed/released pose")
		check(last.center.distance_to(target.center) < .001 and last.scale.distance_to(target.scale) < .001 and absf(last.rotation-target.rotation) < .001, key + " settles at the full destination pose")
		var raw := .25; var p: float = policy.progress(key, raw)
		check(motion.sample_pose(start, target, raw, policy, key).center.y < start.center.lerp(target.center, p).y, key + " travels on an elevated arc within the configured time")
	check(start == unchanged, "sampling never mutates the input pose")
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
