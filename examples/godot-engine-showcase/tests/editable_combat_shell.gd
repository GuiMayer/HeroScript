extends "res://tests/visual_baseline.gd"
func _run() -> void:
	session = root.get_node("GameSession"); gateway = FixtureGateway.new(); session.configure(gateway); session.available = false
	load_combat()
	router = load("res://main.tscn").instantiate(); root.add_child(router); await process_frame
	router.show_combat(); await settle()
	var screen = router.host.get_child(0)
	var view = screen.view
	check(screen.scene_file_path == "res://scenes/ui/screens/combat_screen.tscn", "router uses canonical combat scene")
	check(view.scene_file_path == "res://scenes/ui/components/combat_view.tscn", "application adapter binds authored combat view")
	check(view.hand.scene_file_path == "res://scenes/ui/components/card_hand.tscn", "combat uses shared editable hand")
	var turn = view.turn_view
	var preview = view.detail_label
	var hand_card = view.hand.card_for("baseline-0")
	turn.add_theme_constant_override("separation", 17)
	for index in 3:
		view._turn_key = []; view._build_footer()
	check(view.turn_view == turn and view.detail_label == preview and view.hand.card_for("baseline-0") == hand_card, "footer rebind retains dock preview and cards")
	check(turn.get_theme_constant("separation") == 17, "authored turn spacing survives model updates")
	check(turn.get_node("EndTurnButton").pressed.get_connections().size() == 1, "footer refresh keeps a single end-turn intent")
	check(turn.get_child(turn.get_child_count()-1).name == "EndTurnButton", "end turn stays after the right pile controls")
	var before = JSON.stringify(session.run)
	view.hand.move_visual_card("baseline-0", 2)
	check(JSON.stringify(session.run) == before, "hand presentation never changes the authoritative run")
	check(root.get_node("GameServices").diagnostics().is_empty(), "scene binding makes no real engine requests")
	router.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
