extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 12: await process_frame
func _run() -> void:
	var output := ""
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output = arg.trim_prefix("--lab-output=")
	var translator = root.get_node("I18n"); var prefs = root.get_node("Preferences")
	var locale: String = translator.locale; var scale: float = prefs.text_scale
	var document = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/inscription_loadout_lab.json"))
	var draft = load("res://scripts/labs/inscription_loadout_draft.gd").new(); draft.configure(document)
	var fixture: Dictionary = draft.snapshot()
	var untouched := JSON.stringify(fixture)
	for resolution in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = resolution; root.content_scale_size = resolution
		for language in ["en", "pt_BR"]:
			translator.set_locale(language); prefs.text_scale = 1.2 if language == "pt_BR" else 1.0
			var session = load("res://tests/run_loadout.gd").Session.new(); session.fail = false
			session.run.playerEntityId = "Matrix Wanderer"; session.visual_fixture = fixture; session.assignments = fixture.assignments.duplicate()
			var screen = load("res://scenes/run_loadout.tscn").instantiate(); screen.session = session; root.add_child(screen); await settle()
			var view = screen.inscriptions_view
			check(view.editorial_layout and not view.columns.vertical and view._inventory.size() == fixture.items.size() and view._positions.size() == fixture.slots.size(), "three-column editorial view reuses real loadout components: %s / %s" % [resolution, language])
			check(view.inventory_grid.columns == 3 and view.slot_grid.columns == 3, "inventory and equipped slots use a readable three-column grid")
			check(view._inventory.values().all(func(tile): return tile.find_child("InscriptionSigil", true, false).texture != null), "every inscription has a catalog sigil, including unknown-content fallback")
			check(preload("res://tests/layout_inspector.gd").vertical_text_issues(screen).is_empty(), "no compressed vertical text")
			check(screen.get_minimum_size().x <= resolution.x and view.columns.get_global_rect().end.x <= resolution.x, "editorial layout fits the viewport width")
			var tile = view._inventory.sample_focus
			tile.pressed.emit(); await settle()
			check(session.commits.is_empty() and screen.shell.attributes_view._rows.critical_chance.marker.visible, "inspection highlights relevant character attributes without issuing a gameplay command")
			var assignments_before := JSON.stringify(session.assignments)
			view._filter_buttons.Defense.pressed.emit(); await settle()
			check(view._inventory.values().filter(func(item): return item.visible).size() < fixture.items.size() and JSON.stringify(session.assignments) == assignments_before, "category filters only affect visibility, not ownership or equipment")
			view._filter_buttons.All.pressed.emit(); await settle()
			check(view._inventory.sample_focus == tile, "filter changes retain tile identity and drag components")
			check(tile.drag_payload().item == "sample_focus" and tile.drag_payload().scope == view._positions.trace_6.drag_scope, "editorial sigils preserve the scoped drag/drop payload")
			for button in view._inventory.values() + view._positions.values():
				var label: Label = button.find_child("TileTitle", true, false)
				check(label.get_global_rect().end.y <= button.get_global_rect().end.y + 1, "tile name remains inside its own interactive bounds")
			check(not view.find_child("LoadoutRestoreButton", true, false).visible and view.find_child("LoadoutClearButton", true, false).visible, "only the clear action remains in the equipment toolbar")
			if not output.is_empty():
				await process_frame; await RenderingServer.frame_post_draw
				check(root.get_texture().get_image().save_png(output.path_join("loadout-editorial-%s-%sx%s.png" % [language, resolution.x, resolution.y])) == OK, "saved rendered editorial screenshot")
			screen.queue_free(); await settle()
	check(JSON.stringify(fixture) == untouched, "presentation never mutates the source fixture")
	translator.set_locale(locale); prefs.text_scale = scale
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
