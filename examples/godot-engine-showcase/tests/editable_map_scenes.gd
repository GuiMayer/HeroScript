extends "res://tests/visual_baseline.gd"
func _run() -> void:
	session = root.get_node("GameSession"); gateway = FixtureGateway.new(); session.configure(gateway); session.available = false; session.synchronized = false
	load_combat(); load_journey(false)
	var untouched := JSON.stringify(session.run)
	router = load("res://main.tscn").instantiate(); root.add_child(router); await process_frame
	router.show_map(); await settle()
	var screen = router.host.get_child(0); var view = screen.view
	var marker = view.route.buttons.encounter
	var details = view._inspector.get_node("Information/Title")
	view._inspector.get_node("Information").add_theme_constant_override("separation",21)
	var viewport = view._scroll.get_global_rect()
	view._inspect("reward"); view._set_inspector_open(true); await settle()
	screen.refresh_state(router.presentation); await settle()
	check(screen.scene_file_path == "res://scenes/ui/screens/map_screen.tscn" and view.scene_file_path == "res://scenes/ui/components/map_view.tscn", "router composes canonical map scenes")
	check(view.route.buttons.encounter == marker and marker.scene_file_path == "res://scenes/ui/components/map_node.tscn", "route refresh retains canonical node template instances")
	check(view._inspector.get_node("Information/Title") == details, "node selection fills the same authored inspector labels")
	check(view._inspector.get_node("Information").get_theme_constant("separation") == 21, "inspector data does not overwrite authored spacing")
	check(view._scroll.get_global_rect().is_equal_approx(viewport), "floating inspector does not resize the map viewport")
	check(view._inspector_panel.scene_file_path == "res://scenes/ui/components/map_inspector.tscn", "inspector uses its own editable scene")
	check(JSON.stringify(session.run) == untouched, "inspection and rendering preserve authoritative progress")
	check(root.get_node("GameServices").diagnostics().is_empty(), "map presentation performs no real HTTP requests")
	router.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
