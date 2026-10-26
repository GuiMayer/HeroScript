extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var curves = preload("res://scripts/presentation/motion_curves.gd")
	for name in ["linear", "swift", "smooth"]:
		var previous := -1.0; var valid := true
		for i in 101:
			var p: float = curves.sample(name, i / 100.0)
			valid = valid and is_finite(p) and p >= previous and p >= 0 and p <= 1; previous = p
		check(valid and curves.sample(name, 0) == 0 and curves.sample(name, 1) == 1, name + " curve is bounded, monotonic and has exact endpoints")
	check(curves.sample("swift", .5) > .9, "reference easing decelerates strongly without confusing spatial and timing curves")
	check(curves.settle(0, .03) == 1 and curves.settle(1, .03) == 1 and curves.settle(.91, .03) < 1, "settling stays inside the active cycle and ends at unit scale")
	var policy = preload("res://scripts/presentation/motion_policy.gd").capture("default", {})
	var copy: Dictionary = policy.shape("card_flight"); copy.timing = "linear"
	check(policy.shape("card_flight").timing == "swift", "shape settings return detached values")
	check(policy.duration("card_flight") + policy.duration("staged_discard") == 1 and policy.duration("exile_burn") == .5, "new curves preserve configured cycle durations")
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
