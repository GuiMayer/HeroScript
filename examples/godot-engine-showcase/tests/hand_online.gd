extends SceneTree
## Real candidate submission and replay through the production screens in two settings.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	await create_timer(.3).timeout
func _run() -> void:
	var session = root.get_node("GameSession")
	var prefs = root.get_node("Preferences")
	var i18n = root.get_node("I18n")
	var prior := {"last": prefs.last_run_id, "setting": prefs.selected_setting_id, "motion": prefs.reduced_motion, "auto": prefs.auto_animations, "scale": prefs.text_scale,
		"resolution": prefs.window_resolution, "fullscreen": prefs.fullscreen, "locale": i18n.locale}
	prefs.reduced_motion = true
	prefs.auto_animations = false
	prefs.fullscreen = false
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await settle()
	var connected: bool = await session.connect_engine("default")
	check(connected, "connects to a real engine with published settings")
	if not connected: _finish(prior, router); return
	for setting in ["default", "ascendant"]:
		check(session.select_setting(setting), "select setting: " + setting)
		var opened: bool = await session.start_campaign(int(Time.get_unix_time_from_system()) % 80000000 + randi_range(450000000, 451000000))
		check(opened, "start authored journey: " + setting)
		if not opened: continue
		var entry: Dictionary = session.command("START_ENCOUNTER")
		if entry.is_empty(): check(false, "published encounter action"); continue
		check(await session.execute_run_command("START_ENCOUNTER", entry.get("validPayload", {})), "enter encounter: " + setting)
		router.open_game()
		await settle()
		if router.host.get_child_count() == 0:
			check(false, "combat screen instantiated")
			_finish(prior, router)
			return
		var screen = router.host.get_child(0)
		var hand = screen.hand
		var id: String = hand.order[0]
		var card = hand.card_for(id)
		card.grab_focus()
		screen.refresh_state(screen.presentation)
		await settle()
		check(screen.hand == hand and hand.card_for(id) == card and card.has_focus(), "identical combat refresh preserves hand, instance and focus: " + setting)
		for resolution in [Vector2i(1280, 720), Vector2i(1280, 800), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
			prefs.set_resolution(resolution, false)
			for locale in ["en", "pt_BR"]:
				i18n.set_locale(locale)
				for scale in [1.0, 1.2]:
					prefs.text_scale = scale
					router.theme = load("res://scripts/ui/app_theme.gd").build(prefs.high_contrast)
					screen.refresh_state(screen.presentation)
					await settle()
					var turn: Rect2 = screen.find_child("EndTurnButton", true, false).get_global_rect()
					check(turn.end.y <= router.get_global_rect().end.y - 23 and turn.end.x <= screen.gameplay_column.get_global_rect().end.x + 1,
						"turn control fits: %s / %s / %s / %s" % [setting, resolution, locale, scale])
					check(preload("res://tests/layout_inspector.gd").vertical_text_issues(screen).is_empty(), "combat text never collapses vertically")
					check(screen.find_child("CombatBody", true, false).get_global_rect().end.x <= router.get_global_rect().end.x - 47, "translated hand toolbar does not widen the screen")
					var field: Rect2 = screen.find_child("Battlefield", true, false).get_global_rect()
					var actors_fit: bool = screen.find_children("ActorView_*", "PanelContainer", true, false).all(func(actor):
						return actor.get_global_rect().end.y <= field.end.y - 5)
					check(actors_fit, "authored actor information remains vertically visible")
					if not actors_fit:
						print("ACTOR_BOUNDS field=", field, " actors=", screen.find_children("ActorView_*", "PanelContainer", true, false).map(func(actor): return actor.get_global_rect()))
						if resolution == Vector2i(1280, 720) and locale == "en" and scale == 1.2:
							var info = screen.find_children("ActorView_*", "PanelContainer", true, false)[0].get_child(0).get_child(1)
							print("INFO_BOUNDS ", info.get_children().map(func(part): return [part.get_class(), part.get_combined_minimum_size(), part.size]))
					if setting == "default" and resolution == Vector2i(2560, 1080) and locale == "en" and scale == 1.0:
						for argument in OS.get_cmdline_user_args():
							if argument.begins_with("--hand-screenshot=") and DisplayServer.get_name() != "headless":
								await RenderingServer.frame_post_draw
								check(root.get_texture().get_image().save_png(argument.trim_prefix("--hand-screenshot=")) == OK, "capture real authored combat")
						screen._toggle_character_sidebar()
						await settle()
						check(screen.hand == hand and screen.find_child("EndTurnButton", true, false).get_global_rect().end.y <= router.get_global_rect().end.y - 23,
							"closing sidebar preserves hand and visible turn controls")
						screen._toggle_character_sidebar()
						await settle()
		var candidate: Dictionary = session.legal_actions.filter(func(item): return str(item.get("source", "")) == "Card")[0]
		var ids: Array = hand.order.duplicate()
		var survivors: Dictionary = hand.cards()
		await screen._play_candidate(candidate)
		await settle()
		check(router.host.get_child(0) == screen and screen.hand == hand, "accepted command preserves the actual hand container: " + setting)
		for survivor in hand.order:
			if survivor in ids: check(hand.card_for(survivor) == survivors[survivor], "surviving instance keeps its control after a real play")
		while root.get_node("Playback").has_frames(): screen._next_frame()
		var verified: Dictionary = await session.verify()
		check(verified.ok and verified.data.get("isValid", false), "replay remains deterministic after hand integration: " + setting)
	_finish(prior, router)

func _finish(prior: Dictionary, router) -> void:
	var prefs = root.get_node("Preferences")
	prefs.last_run_id = prior.last
	prefs.selected_setting_id = prior.setting
	prefs.reduced_motion = prior.motion
	prefs.auto_animations = prior.auto
	prefs.text_scale = prior.scale
	prefs.window_resolution = prior.resolution
	prefs.fullscreen = prior.fullscreen
	prefs.apply_window()
	root.get_node("I18n").set_locale(prior.locale)
	prefs.save()
	router.queue_free()
	root.get_node("GameAudio").shutdown()
	await process_frame
	print("SHOWCASE_HAND_ONLINE failures=", failures)
	quit(0 if failures == 0 else 1)
