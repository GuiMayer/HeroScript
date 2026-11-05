extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run() -> void:
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-"): quit(1); return
	var prefs = root.get_node("Preferences")
	prefs.fullscreen = false; prefs.lock_aspect_16_9 = false
	prefs.window_resolution = Vector2i(1280, 800); prefs.save()
	var before: Dictionary = prefs.video_snapshot()
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	for _frame in 10: await process_frame
	router.show_settings(false)
	for _frame in 10: await process_frame
	var screen = router.host.get_child(0)
	check(screen.find_child("MonitorSelector", true, false).item_count == maxi(1, DisplayServer.get_screen_count()), "monitor selector contains only available displays, without scene placeholders")
	var mode: OptionButton = screen.find_child("DisplayModeSelector", true, false)
	check(mode.item_count == 2, "mode offers windowed and borderless fullscreen")
	mode.item_selected.emit(1)
	check(prefs.fullscreen and screen.resolution_selector.disabled, "borderless uses automatic monitor resolution")
	check(screen.get_node("VideoConfirmation").visible and prefs.video_deadline > 0, "video changes open the confirmation countdown")
	prefs.save(false)
	var config := ConfigFile.new(); config.load(prefs.SAVE_PATH)
	check(not config.get_value("video", "fullscreen"), "unrelated saves cannot persist unconfirmed video")
	screen.get_node("VideoConfirmation").canceled.emit()
	check(prefs.video_snapshot() == before and not screen.get_node("VideoConfirmation").visible, "cancel restores the complete video state and menu")
	screen.find_child("AspectRatioSelector", true, false).item_selected.emit(1)
	check(prefs.lock_aspect_16_9 and prefs.resolution_options().all(func(value): return prefs.is_16_9(value)), "aspect selection filters window resolutions")
	screen.get_node("VideoConfirmation").confirmed.emit()
	config.load(prefs.SAVE_PATH)
	check(config.get_value("video", "lock_aspect_16_9") and prefs.video_deadline == 0, "confirmation saves the video choice")
	var confirmed: Dictionary = prefs.video_snapshot()
	prefs.preview_video({"lock_aspect_16_9": false})
	paused = true
	await create_timer(15.2, true, false, true).timeout
	paused = false
	check(prefs.video_snapshot() == confirmed and prefs.video_deadline == 0, "15-second timeout restores confirmed settings even while paused")
	prefs.preview_video({"lock_aspect_16_9": false})
	screen.queue_free()
	await process_frame
	check(prefs.video_snapshot() == confirmed, "closing settings reverts pending video changes")
	quit(1 if failures else 0)
