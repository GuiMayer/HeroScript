extends "res://tests/visual_baseline.gd"
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	if output_dir.is_empty(): quit(1); return
	DirAccess.make_dir_recursive_absolute(output_dir)
	var prefs = root.get_node("Preferences")
	prefs.reduced_motion = true; prefs.auto_animations = false; prefs.text_scale = 1.; prefs.high_contrast = false
	session = root.get_node("GameSession"); gateway = FixtureGateway.new(); session.configure(gateway); session.synchronized = true
	router = load("res://main.tscn").instantiate(); root.add_child(router)
	router.apply_visual_profile(load("res://data/themes/ivory_run.tres"))
	router.visual_context.set_scenery_profile(load("res://data/themes/ivory_scenery.tres"))
	for resolution in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = resolution
		for language in ["en", "pt_BR"]:
			root.get_node("I18n").set_locale(language); await settle()
			load_combat(); router.show_combat(); await capture(language + "-ivory-combat")
			router.toggle_pause(); await capture(language + "-ivory-pause"); router.toggle_pause()
			session._card_zones.zones[0].cards = []; session._card_zones.zones[0].count = 0
			router.show_combat(); await capture(language + "-ivory-empty-hand")
			load_journey(false)
			session._run.map.nodes[0].nextNodeIds = ["reward", "alternative"]
			session._run.map.nodes.append({"nodeId": "alternative", "activity": {"type": "Shop"}, "metadata": {"displayName": "Alternative route"}, "nextNodeIds": ["boss"]})
			router.show_map(); await capture(language + "-ivory-map")
			load_journey(true); router.show_activity(); await capture(language + "-ivory-reward")
			load_journey(false); session._run.map.nodes[0].activity.type = "Dialogue"
			session._run["dialogues"] = [{"dialogueInstanceId": "d", "nodeId": "speech", "activityNodeId": "encounter",
				"title": {"text": "The next step", "translations": {"pt_BR": "O próximo passo"}}, "speaker": {"text": "Keeper", "translations": {"pt_BR": "Guardião"}},
				"text": {"text": "The path branches ahead. Read the signs, weigh your choices, and decide where to travel. Your journey is yours to shape.", "translations": {"pt_BR": "O caminho se divide adiante. Leia os sinais, considere suas escolhas e decida para onde viajar. Sua jornada será moldada por você."}},
				"choices": [{"choiceId": "listen", "text": {"text": "Ask about the road", "translations": {"pt_BR": "Perguntar sobre a estrada"}}, "available": true}]}]
			session._available_commands = [{"type": "CHOOSE_DIALOGUE_OPTION", "validPayload": {"dialogueInstanceId": "d", "nodeId": "speech", "choiceIds": ["listen"]}}]
			router.show_dialogue(); await capture(language + "-ivory-dialogue")
	check(root.get_node("GameServices").diagnostics().is_empty(), "ivory captures use no real transport")
	router.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
