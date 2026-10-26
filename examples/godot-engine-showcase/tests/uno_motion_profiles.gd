extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var profile = preload("res://scripts/presentation/card_animation_profile.gd")
	var existed: bool = FileAccess.file_exists(profile.PATH)
	var original: String = FileAccess.get_file_as_string(profile.PATH) if existed else ""
	var prefs = root.get_node("Preferences")
	prefs.animation_speed = 1.0; prefs.reduced_motion = false; prefs.animation_preset = "normal"
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 6: await process_frame
	for timing in ["swift", "smooth", "linear"]:
		lab.animation_picker.select_animation("use_" + timing)
		lab.animation_cycle.value = .2; lab.apply_animation_to_combat()
		var combat_policy = preload("res://scripts/presentation/motion_policy.gd").capture("default", {"animation_speed": 1.0})
		lab.play_selected_animation()
		check(["card_flight", "retain", "zone_transfer"].all(func(key): return combat_policy.shape(key).timing == timing and lab.visual_context.motion.shape(key).timing == timing), timing + " uses the same captured curves in lab and combat")
		lab.motion_lab.skip()
		check(is_equal_approx(combat_policy.duration("card_flight") + combat_policy.duration("staged_discard"), .2), "style does not change the configured active cycle")
	if existed:
		var file := FileAccess.open(profile.PATH, FileAccess.WRITE); file.store_string(original); file.close()
	else: DirAccess.remove_absolute(profile.PATH)
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
