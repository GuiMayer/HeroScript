extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var prefs = root.get_node("Preferences")
	var positions := [Vector2i.ZERO, Vector2i(1920, 0), Vector2i(-1280, -200)]
	check(prefs.resolve_monitor(1, null, positions, 0) == 1, "restores saved secondary monitor")
	check(prefs.resolve_monitor(1, Vector2i(1920, 0), [positions[1], positions[0]], 1) == 0, "desktop origin resolves reordered monitor indices")
	check(prefs.resolve_monitor(2, positions[2], positions, 0) == 2, "negative desktop coordinates are valid")
	check(prefs.resolve_monitor(2, positions[2], [Vector2i.ZERO], 0) == 0, "disconnected monitor falls back to an available display")
	check(prefs.resolve_monitor(-1, null, positions, 1) == 1, "first launch keeps the host-selected display")
	check(prefs.resolve_monitor(99, null, positions, -1) == 0, "invalid monitor and fallback never produce an out-of-range index")
	check(prefs.resolve_monitor(1, null, [], -1) == 0, "empty display inventory is handled safely")
	var monitor: int = prefs.window_monitor
	var position = prefs.window_monitor_position
	prefs.window_monitor = 1
	prefs.window_monitor_position = Vector2i(1920, 0)
	prefs.save(false)
	prefs.window_monitor = -1
	prefs.window_monitor_position = null
	prefs.load_settings()
	check(prefs.window_monitor == 1 and prefs.window_monitor_position == Vector2i(1920, 0), "monitor identity round-trips through the isolated preference file")
	prefs.window_monitor = monitor
	prefs.window_monitor_position = position
	prefs.save(false)
	root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
