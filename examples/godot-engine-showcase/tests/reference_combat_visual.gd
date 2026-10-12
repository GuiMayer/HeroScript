extends "res://tests/visual_baseline.gd"
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	if output_dir.is_empty(): quit(1); return
	DirAccess.make_dir_recursive_absolute(output_dir)
	root.get_node("Preferences").reduced_motion = true
	session = root.get_node("GameSession"); gateway = FixtureGateway.new(); session.configure(gateway); session.synchronized = true
	router = load("res://main.tscn").instantiate(); root.add_child(router)
	for resolution in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = resolution
		for language in ["en", "pt_BR"]:
			root.get_node("I18n").set_locale(language); await settle()
			for count in [0, 1, 2, 5, 10, 20]:
				load_combat()
				var templates: Array = session._card_zones.zones[0].cards.duplicate(true)
				session._card_zones.zones[0].cards = []; session._card_zones.zones[0].count = count
				session._legal_actions = []
				for index in count:
					var card: Dictionary = templates[index % templates.size()].duplicate(true); card.cardInstanceId = "reference-%s" % index
					session._card_zones.zones[0].cards.append(card)
					session._legal_actions.append({"source": "Card", "command": {"cardInstanceId": card.cardInstanceId, "actionType": "PLAY_CARD", "targetIds": ["wisp"]}, "costs": [{"resourceId": "energy", "amount": 1}]})
				router.show_combat(); await capture(language + "-hand-%02d" % count)
			load_combat()
			session._card_zones.zones[1].merge({"contentsVisible": true, "orderVisible": false, "ownerId": "hero", "scopeId": "baseline-combat", "cards": [{"cardInstanceId": "collection-a", "definitionId": "fireball", "upgrades": []}, {"cardInstanceId": "collection-b", "definitionId": "defend", "upgrades": []}], "count": 2}, true)
			router.show_combat(); await settle()
			router.host.get_child(0)._open_collection(load("res://scripts/presentation/card_collection_presenter.gd").address(session._card_zones.zones[1]))
			await capture(language + "-visual-pile")
			router.host.get_child(0)._collection_dialog.queue_free(); await settle()
	check(root.get_node("GameServices").diagnostics().is_empty(), "reference fixtures issue no HTTP")
	router.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
