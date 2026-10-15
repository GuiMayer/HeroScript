extends SceneTree
## Exercise real settings callbacks in the runner's isolated user directory.
var failures := 0
var locale_events := 0
const SLIDERS := {
	"general": [["AnimationSpeedSlider", "animation_speed", "game", "animation_speed"],
		["HandEmphasisSlider", "hand_emphasis", "game", "hand_emphasis"]],
	"audio": [["MasterVolumeSlider", "master_volume", "audio", "master"],
		["MusicVolumeSlider", "music_volume", "audio", "music"],
		["SfxVolumeSlider", "sfx_volume", "audio", "sfx"]],
	"accessibility": [["TextScaleSlider", "text_scale", "video", "text_scale"]]
}

func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 8: await process_frame
func _locale_changed() -> void: locale_events += 1

func _run() -> void:
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-"):
		check(false, "interaction test requires an isolated preference directory")
		quit(1)
		return
	var prefs = root.get_node("Preferences")
	var i18n = root.get_node("I18n")
	var session = root.get_node("GameSession")
	var saved := {"locale": prefs.locale, "available": session.available}
	for sliders in SLIDERS.values():
		for spec in sliders: saved[spec[1]] = prefs.get(spec[1])
	session.available = false
	var requests: int = root.get_node("GameServices").diagnostics().size()
	root.size = Vector2i(1440, 900)
	root.content_scale_size = Vector2i(1440, 900)
	prefs.locale = "en"
	i18n.set_locale("en")
	i18n.locale_changed.connect(_locale_changed)
	# Equality is checked against the normalized locale, including unsupported languages.
	TranslationServer.set_locale("pt_BR")
	i18n.set_locale("en")
	i18n.set_locale("unsupported")
	check(locale_events == 0 and TranslationServer.get_locale() == "en", "same/fallback locale emits no change; native translator still initializes")
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await settle()
	for language in ["en", "pt_BR"]:
		prefs.locale = language
		i18n.set_locale(language)
		await settle()
		for from_gameplay in [false, true]:
			router.show_settings(from_gameplay)
			await settle()
			var screen = router.host.get_child(0)
			check(screen.find_child("HandDragToggle", true, false) == null, "drag-to-play has no settings toggle")
			for category in SLIDERS:
				screen.category_buttons[category].pressed.emit()
				await settle()
				for spec in SLIDERS[category]:
					var slider: HSlider = screen.find_child(spec[0], true, false)
					slider.grab_focus()
					var events_before := locale_events
					# Send a real slider click, then consecutive changes as during a drag.
					var before := slider.value
					var point := slider.get_global_rect().position + Vector2(slider.size.x * (.8 if before < (slider.min_value + slider.max_value) * .5 else .2), slider.size.y * .5)
					for pressed in [true, false]:
						var click := InputEventMouseButton.new()
						click.button_index = MOUSE_BUTTON_LEFT
						click.pressed = pressed
						click.position = point
						root.push_input(click)
					await settle()
					check(is_instance_valid(slider) and not is_equal_approx(slider.value, before), "%s / %s / game=%s: mouse click changes value" % [language, spec[0], from_gameplay])
					if not is_instance_valid(slider): continue
					for fraction in [.25, .65, .85]:
						slider.value = lerpf(slider.min_value, slider.max_value, fraction)
						await settle()
					check(is_instance_valid(screen) and router.host.get_child(0) == screen and screen.selected_category == category and screen.find_child(spec[0], true, false) == slider and root.gui_get_focus_owner() == slider and locale_events == events_before,
						"%s / %s / game=%s: repeated edits keep page, slider and focus; no false locale events" % [language, spec[0], from_gameplay])
					var config := ConfigFile.new()
					check(config.load(prefs.SAVE_PATH) == OK and is_equal_approx(float(config.get_value(spec[2], spec[3], -1)), slider.value) and is_equal_approx(float(prefs.get(spec[1])), slider.value), "slider changes applied and persisted: " + spec[0])
					if spec[1] == "master_volume":
						check(is_equal_approx(AudioServer.get_bus_volume_db(AudioServer.get_bus_index("Master")), linear_to_db(slider.value)), "master volume applied immediately")
			# A genuine language selection must still translate the settings screen.
			screen._show_category("general")
			await settle()
			var events_before := locale_events
			var language_selector: OptionButton = screen.find_child("LanguageSelector", true, false)
			language_selector.item_selected.emit(1 if language == "en" else 0)
			await settle()
			check(locale_events == events_before + 1 and router.host.get_child(0) != screen and router.host.get_child(0).page_title.text == i18n.text("GENERAL"), "actual language change still refreshes translated settings")
			prefs.locale = language
			i18n.set_locale(language)
			await settle()
	check(requests == root.get_node("GameServices").diagnostics().size(), "local settings interactions make no engine requests")
	router.queue_free()
	await settle()
	i18n.locale_changed.disconnect(_locale_changed)
	for key in saved:
		if key != "available": prefs.set(key, saved[key])
	session.available = saved.available
	prefs.save(false)
	i18n.set_locale(prefs.locale)
	root.get_node("GameAudio").shutdown()
	await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
