extends SceneTree
## Complete normal campaign and replay, without privileged scenario-author tools.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var session = root.get_node("GameSession")
	var connected: bool = await session.connect_engine("default")
	check(connected, "normal QA host connects")
	if connected:
		var driver = load("res://tests/smoke.gd").new()
		check(await driver.drive_complete_campaign(int(Time.get_unix_time_from_system()) + 570000), "published normal campaign completes through canonical commands")
		driver.free()
		var replay: Dictionary = await session.verify()
		check(replay.ok and replay.data.get("isValid", false), "complete normal campaign replays deterministically")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
