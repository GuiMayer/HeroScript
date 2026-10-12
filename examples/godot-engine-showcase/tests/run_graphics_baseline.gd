extends "res://tests/visual_baseline.gd"
## Comparable fixture capture; original baseline artifacts remain untouched.
var capture_phase := "before-refactor"
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
		if argument.begins_with("--capture-phase="): capture_phase = argument.trim_prefix("--capture-phase=")
	if output_dir.is_empty(): quit(1); return
	DirAccess.make_dir_recursive_absolute(output_dir)
	var prefs = root.get_node("Preferences")
	var saved_before := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	prefs.text_scale = 1.; prefs.high_contrast = false; prefs.reduced_motion = true
	prefs.fullscreen = false; prefs.auto_animations = false; prefs.hand_layout_mode = "adaptive"
	session = root.get_node("GameSession")
	gateway = FixtureGateway.new(); session.configure(gateway); session.available = false; session.synchronized = true
	router = load("res://main.tscn").instantiate(); root.add_child(router)
	for resolution in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		prefs.set_resolution(resolution, false); root.size = resolution
		for language in ["en", "pt_BR"]:
			root.get_node("I18n").set_locale(language); await settle()
			load_combat(); router.show_combat()
			var combat = router.host.get_child(0)
			await capture(language + "-combat-hand")
			combat._choose_card("baseline-0"); combat.view._show_target_preview("wisp")
			await capture(language + "-combat-preview")
			combat._cancel_selection(); combat._toggle_character_sidebar()
			await capture(language + "-combat-sidebar")
			router.toggle_pause(); await capture(language + "-pause"); router.toggle_pause()
			combat._toggle_character_sidebar()
			session._card_zones.zones[0].cards = []; session._card_zones.zones[0].count = 0
			combat.refresh_state(router.presentation); await capture(language + "-combat-empty")
			load_combat()
			var guest := actor("guest", "unknown-actor", "neutral")
			guest.resources["custom_resource"] = {"current": -5}
			session._combat.actors.append(guest)
			session._combat.relationships["rules"] = [{"fromSideId": "player", "toSideId": "neutral", "relationship": "Neutral"}]
			combat.refresh_state(router.presentation); await capture(language + "-combat-third-team")
			load_combat(); load_journey(false); router.show_activity(); await capture(language + "-journey")
			load_journey(true); router.show_activity(); await capture(language + "-reward")
			load_journey(false)
			session._run.map.nodes[0].activity.type = "Shop"
			session._run["shops"] = [{"shopInstanceId": "shop", "items": [{"itemId": "item", "cardId": "fireball", "costs": [{"resourceId": "gold", "amount": 25}]}]}]
			session._available_commands = [{"type": "BUY_SHOP_ITEM", "validPayload": {"shopInstanceId": "shop", "itemIds": ["item"]}}]
			router.show_activity(); await capture(language + "-shop")
			session._run.map.nodes[0].activity.type = "Preparation"
			session._run["preparations"] = [{"preparationInstanceId": "rest", "options": [{"optionId": "recover", "costs": []}]}]
			session._available_commands = [{"type": "APPLY_PREPARATION_OPTION", "validPayload": {"preparationInstanceId": "rest", "optionIds": ["recover"]}}]
			router.show_activity(); await capture(language + "-preparation")
			session._run.map.nodes[0].activity.type = "Dialogue"
			session._run["dialogues"] = [{"activityNodeId": "encounter", "title": {"text": "A conversation"}, "speaker": {"text": "Keeper"}, "text": {"text": "A fixture with choices, not rules."}, "choices": [{"choiceId": "listen", "text": {"text": "Listen"}, "available": true}]}]
			session._available_commands = [{"type": "CHOOSE_DIALOGUE_OPTION", "validPayload": {"dialogueInstanceId": "dialogue", "nodeId": "encounter", "choiceIds": ["listen"]}}]
			router.show_activity(); await capture(language + "-dialogue")
			session._run.lifecycle = "Completed"; router.show_activity(); await capture(language + "-ending")
	check(root.get_node("GameServices").diagnostics().is_empty(), "no real HTTP requests or gameplay commands")
	check((FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else "") == saved_before, "captures never write preferences")
	var report := FileAccess.open(output_dir.path_join("captures.json"), FileAccess.WRITE)
	if report:
		report.store_string(JSON.stringify({"fixture": true, "phase": capture_phase, "captures": captures, "failures": failures, "gateway_reads": gateway.calls}, "\t")); report.close()
	router.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
