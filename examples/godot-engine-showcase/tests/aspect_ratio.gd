extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run() -> void:
	var prefs = root.get_node("Preferences")
	prefs.lock_aspect_16_9 = true
	prefs.window_resolution = Vector2i(2560, 1080)
	prefs.save()
	check(prefs.is_16_9(prefs.window_resolution), "enabling lock replaces an incompatible resolution")
	check(prefs.resolution_options().all(func(value): return prefs.is_16_9(value)), "locked selector contains only exact 16:9 resolutions")
	check(not prefs.set_resolution(Vector2i(2560, 1080), false), "locked mode rejects ultrawide resolutions")
	check(prefs.set_resolution(Vector2i(1920, 1080), false), "locked mode accepts 16:9 resolution")
	check(root.content_scale_aspect == Window.CONTENT_SCALE_ASPECT_KEEP and root.content_scale_size == Vector2i(1440, 810), "viewport preserves 16:9 during window resize and fullscreen")
	var config := ConfigFile.new()
	config.load(prefs.SAVE_PATH)
	check(config.get_value("video", "lock_aspect_16_9", false), "aspect preference persists automatically")
	prefs.lock_aspect_16_9 = false
	prefs.apply_window()
	check(root.content_scale_aspect == Window.CONTENT_SCALE_ASPECT_EXPAND, "disabling lock restores adaptive viewport")
	check(Vector2i(2560, 1080) in prefs.resolution_options(), "disabling lock restores ultrawide choices")
	quit(1 if failures else 0)
