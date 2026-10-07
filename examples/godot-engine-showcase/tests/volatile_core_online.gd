extends SceneTree
## Real setting/REST contracts rendered by the normal reusable Godot screens.
var failures := 0
var router

func _init() -> void: call_deferred("_run")

func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1

func _run() -> void:
	var session = root.get_node("GameSession")
	var i18n = root.get_node("I18n")
	var preferences = root.get_node("Preferences")
	var original := {"run": preferences.last_run_id, "setting": preferences.selected_setting_id, "locale": i18n.locale}
	router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await create_timer(.3).timeout
	while session.busy: await create_timer(.025).timeout
	var connected: bool = await session.connect_engine("volatile-core")
	check(connected and session.selected_setting_id == "volatile-core", "selects the published core setting and its revision")
	if connected:
		var opened: bool = await session.start_campaign(int(Time.get_unix_time_from_system()) + 923000)
		check(opened, "starts the canonical authored core journey")
		if opened:
			router.show_activity()
			await create_timer(.2).timeout
			check(session.run.map.nodes.size() == 9, "all nine build opportunities come from JSON")
			var entry: Dictionary = session.activity_choices().filter(func(choice): return str(choice.type) == "START_ENCOUNTER")[0]
			check(await session.submit_activity(entry), "starts the core encounter using only advertised input")
			router.open_game()
			await create_timer(1.0).timeout
			var screen = router.host.get_child(0)
			check(not session.legal_actions.is_empty(), "core cards publish playable actions in their own setting")
			for locale in ["en", "pt_BR"]:
				i18n.set_locale(locale)
				await create_timer(.2).timeout
				screen = router.host.get_child(0)
				check(preload("res://tests/layout_inspector.gd").vertical_text_issues(screen).is_empty(), "core combat text remains horizontal: " + locale)
			var evaluation: Dictionary = await session.inspect_hand(["enemy_0"])
			check(evaluation.ok and evaluation.data.cards.all(func(card): return card.has("previewScope") and card.has("procs")), "client receives canonical scope and proc projections")
	preferences.last_run_id = original.run
	preferences.selected_setting_id = original.setting
	i18n.set_locale(original.locale)
	preferences.save()
	router.queue_free()
	root.get_node("GameAudio").shutdown()
	await process_frame
	print("SHOWCASE_VOLATILE_CORE_ONLINE failures=", failures)
	quit(0 if failures == 0 else 1)
