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
	var original_scale: float = prefs.text_scale
	var original_locale: String = i18n.locale
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await settle()
	check(preload("res://tests/layout_inspector.gd").vertical_text_issues(router.host).is_empty(), "main menu has no vertical text")
	var empty_hand_host := HBoxContainer.new()
	empty_hand_host.size = Vector2(1280, 284)
	var combat_screen = load("res://scripts/ui/combat_screen.gd").new()
	var empty_hand_state: Control = combat_screen._empty_hand_state()
	combat_screen.free()
	empty_hand_host.add_child(empty_hand_state)
	root.add_child(empty_hand_host)
	await settle()
	var empty_hand_message: Label = empty_hand_state.find_child("EmptyHandMessage", true, false)
	check(empty_hand_state.get_combined_minimum_size().x >= 320,
		"empty hand keeps a readable horizontal minimum")
	check(empty_hand_message.get_line_count() == 1 and empty_hand_message.size.x >= 280,
		"empty hand message never collapses into vertical text")
	check(preload("res://tests/layout_inspector.gd").vertical_text_issues(empty_hand_host).is_empty(),
		"empty hand passes the vertical-text regression check")
	empty_hand_host.free()
	prefs.text_scale = 1.2
	var card = load("res://scripts/ui/card_view.gd").new()
	card.animate_entry = false
	var card_model := {"definitionId": "font-regression", "tone": "skill", "name": "Stable type",
		"cardType": "SKILL", "rarity": "", "effects": ["Stable effect"], "costs": [],
		"availability": "SELECT TO PLAY", "changeBadges": [], "artPlaceholder": true}
	card.configure(card_model)
	root.add_child(card)
	await settle()
	var initial_font_size: int = card.find_child("CardName", true, false).get_theme_font_size("font_size")
	card_model["rarity"] = "RARE"
	card.configure(card_model)
	await settle()
	var enriched_font_size: int = card.find_child("CardName", true, false).get_theme_font_size("font_size")
	check(initial_font_size == roundi(17 * prefs.text_scale) and enriched_font_size == initial_font_size,
		"asynchronous card enrichment preserves scaled font sizes")
	card.free()
	prefs.text_scale = original_scale
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
			var display_category: Button = router.host.find_child("SettingsCategory_Display", true, false)
			check(is_instance_valid(display_category), "settings exposes categorized display options: " + locale)
			display_category.pressed.emit()
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
	prefs.text_scale = original_scale
	prefs.save()
	i18n.set_locale(original_locale)
	router.queue_free()
	root.get_node("GameAudio").shutdown()
	print("SHOWCASE_RESOLUTION_TESTS failures=", failures)
	create_timer(.15).timeout.connect(func(): process_frame.connect(
		func(): quit(0 if failures == 0 else 1), CONNECT_ONE_SHOT), CONNECT_ONE_SHOT)
