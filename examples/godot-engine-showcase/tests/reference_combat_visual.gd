extends "res://tests/visual_baseline.gd"
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	if output_dir.is_empty(): quit(1); return
	DirAccess.make_dir_recursive_absolute(output_dir)
	root.get_node("Preferences").reduced_motion = true
	session = root.get_node("GameSession"); gateway = FixtureGateway.new(); session.configure(gateway); session.synchronized = true
	router = load("res://main.tscn").instantiate(); root.add_child(router)
	root.size = Vector2i(1920, 1080)
	for count in [0, 1, 2, 5, 10, 20]:
		load_combat()
		var template: Dictionary = session._card_zones.zones[0].cards[0].duplicate(true)
		session._card_zones.zones[0].cards = []; session._card_zones.zones[0].count = count
		for index in count:
			var card := template.duplicate(true); card.cardInstanceId = "reference-%s" % index
			session._card_zones.zones[0].cards.append(card)
		router.show_combat(); await capture("hand-%02d" % count)
	check(root.get_node("GameServices").diagnostics().is_empty(), "reference fixtures issue no HTTP")
	router.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
