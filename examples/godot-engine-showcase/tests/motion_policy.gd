extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var Policy = preload("res://scripts/presentation/motion_policy.gd")
	var normal = Policy.capture("unknown", {"animation_speed": 1.})
	var fast = Policy.capture("unknown", {"animation_preset": "fast"})
	var instant = Policy.capture("unknown", {"animation_preset": "instant"})
	var reduced = Policy.capture("unknown", {"reduced_motion": true})
	check(normal.duration("card_flight") > 0 and fast.duration("card_flight") == normal.duration("card_flight") / 2., "fast preset changes presentation duration only")
	check(instant.duration("card_flight", true) == 0 and instant.duration("impact") == 0, "instant mode removes all blocking motion duration")
	check(reduced.duration("card_flight", true) == 0 and reduced.duration("impact") > 0, "reduced movement retains reading feedback without spatial movement")
	check(Policy.capture("ascendant", {}).random_labels().has("critical"), "setting-specific published random outcomes have configured labels")
	check(Policy.capture("unknown", {}, "res://absent.json").duration("card_flight") > 0, "missing manifest falls back to bounded defaults")
	var copy: Dictionary = normal.cue("impact"); copy.sound = "altered"
	check(normal.cue("impact").sound == "impact", "captured cue policy is defensively copied")
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
