extends "res://tests/visual_baseline.gd"
func _run() -> void:
	session = root.get_node("GameSession"); gateway = FixtureGateway.new(); session.configure(gateway); session.available = false
	load_combat(); load_journey(false)
	router = load("res://main.tscn").instantiate(); root.add_child(router); await process_frame
	router.show_activity(); await settle()
	var screen = router.host.get_child(0); var view = screen.view
	var title = view.get_node("ActivityHeader/Row/Titles/RunTitle")
	var summary = view.get_node("ActivityColumns/ActivityWorkspace/ActivitySummary")
	var button = view.choice_buttons[0]
	view.action_panel.get_node("ActionPanel").add_theme_constant_override("separation",23)
	var next = session.run.duplicate(true); next.resources = {"gold":{"current":25}}; session._run = next
	var untouched := JSON.stringify(session.run)
	screen.refresh_state(router.presentation); await settle()
	check(screen.scene_file_path == "res://scenes/ui/screens/activity_screen.tscn" and view.scene_file_path == "res://scenes/ui/components/journey_view.tscn", "router uses canonical activity scenes")
	check(view.get_node("ActivityHeader/Row/Titles/RunTitle") == title and view.get_node("ActivityColumns/ActivityWorkspace/ActivitySummary") == summary, "resource refresh retains authored header and summary")
	check(view.choice_buttons[0] == button and button.pressed.get_connections().size() == 1, "offer reconciliation retains one live choice intent")
	check(view.action_panel.get_node("ActionPanel").get_theme_constant("separation") == 23, "published offers do not overwrite authored spacing")
	var comparison = load("res://scenes/ui/overlays/transformation_confirmation.tscn").instantiate()
	for side in ["Before","After"]:
		check(comparison.get_node("Content/Scroll/Comparison/"+side+"/Card").scene_file_path == "res://scenes/ui/components/card_view.tscn", "transformation uses current canonical card: "+side)
	comparison.free()
	check(JSON.stringify(session.run) == untouched and root.get_node("GameServices").diagnostics().is_empty(), "presentation leaves gameplay intact and issues no real HTTP request")
	router.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
