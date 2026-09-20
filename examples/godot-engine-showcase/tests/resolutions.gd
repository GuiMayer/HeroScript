extends SceneTree
## Offline actual-window/layout tests. Run with -- --layout-smoke.
var failures := 0

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1

func settle() -> void:
	for _frame in 4: await process_frame

func _run() -> void:
	await create_timer(.15).timeout
	var prefs = root.get_node("Preferences")
	var i18n = root.get_node("I18n")
	var original_size: Vector2i = prefs.window_resolution
	var original_fullscreen: bool = prefs.fullscreen
	var original_locale: String = i18n.locale
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await settle()
	check(preload("res://tests/layout_inspector.gd").vertical_text_issues(router.host).is_empty(), "main menu has no vertical text")
	prefs.fullscreen = false
	check(prefs.resolution_options().has(Vector2i(2560, 1080)), "ultrawide preset is available")
	check(not prefs.set_resolution(Vector2i(-1, 0), false), "invalid resolution is rejected")
	check(prefs.fit_window(Vector2i(2560, 1080), Vector2i(1280, 720)) == Vector2i(1280, 540), "desktop fitting preserves ultrawide aspect")
	for resolution in [Vector2i(1280, 720), Vector2i(1440, 900), Vector2i(1920, 1080), Vector2i(2560, 1080), Vector2i(2560, 1440), Vector2i(3440, 1440), Vector2i(3840, 2160)]:
		prefs.set_resolution(resolution, false)
		await settle()
		if DisplayServer.get_name() == "headless": check(root.size == resolution, "window dimensions applied: " + str(resolution))
		var actual_aspect: float = float(root.size.x) / root.size.y
		check(absf(router.size.x / router.size.y - actual_aspect) < .003, "canvas expands without distortion: " + str(resolution))
		check(router.size.x >= 1439 and router.size.y >= 899, "logical layout stays above its design minimum")
		for locale in ["en", "pt_BR"]:
			i18n.set_locale(locale)
			router.show_settings()
			await settle()
			var selector: OptionButton = router.host.find_child("ResolutionSelector", true, false)
			check(selector.get_item_metadata(selector.selected) == resolution, "settings reflects selected dimensions: " + locale)
			check(selector.get_global_rect().end.x <= router.size.x, "resolution control fits the layout: " + locale)
			check(preload("res://tests/layout_inspector.gd").vertical_text_issues(router.host).is_empty(), "settings has no vertical text: " + locale)
	var selector: OptionButton = router.host.find_child("ResolutionSelector", true, false)
	for index in selector.item_count:
		if selector.get_item_metadata(index) == Vector2i(2560, 1080):
			selector.select(index)
			selector.item_selected.emit(index)
	await settle()
	var saved := ConfigFile.new()
	check(saved.load(prefs.SAVE_PATH) == OK and saved.get_value("video", "width") == 2560 and saved.get_value("video", "height") == 1080, "menu selection persists 2560x1080")
	var toggle: CheckButton = router.host.find_child("FullscreenToggle", true, false)
	toggle.button_pressed = true
	check(selector.disabled, "fullscreen makes native resolution explicit")
	toggle.button_pressed = false
	check(not selector.disabled and prefs.window_resolution == Vector2i(2560, 1080), "leaving fullscreen retains windowed selection")
	prefs.window_resolution = original_size
	prefs.fullscreen = original_fullscreen
	prefs.save()
	i18n.set_locale(original_locale)
	router.queue_free()
	root.get_node("GameAudio").shutdown()
	print("SHOWCASE_RESOLUTION_TESTS failures=", failures)
	create_timer(.15).timeout.connect(func(): process_frame.connect(
		func(): quit(0 if failures == 0 else 1), CONNECT_ONE_SHOT), CONNECT_ONE_SHOT)
