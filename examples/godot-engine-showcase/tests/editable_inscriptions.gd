extends SceneTree
var failures := 0
func _init(): call_deferred("run")
func check(ok: bool, label: String):
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run():
	var lab = load("res://scenes/unified_loadout_lab.tscn").instantiate(); root.add_child(lab)
	await process_frame
	var view = lab.inscriptions_view
	var tile = view._inventory.values()[0]
	var title = tile.find_child("TileTitle", true, false)
	view.inventory_grid.add_theme_constant_override("h_separation", 21)
	view.present(view._model)
	check(view._inventory.values()[0] == tile and tile.find_child("TileTitle", true, false) == title, "model refresh retains authored item content")
	check(view.inventory_grid.get_theme_constant("h_separation") == 21, "authored inventory spacing survives refresh")
	check(view.scene_file_path == "res://scenes/ui/components/loadout/inscriptions_editorial.tscn", "production and lab share the editorial scene")
	check(tile.scene_file_path == "res://scenes/ui/components/loadout/inscription_tile_editorial.tscn", "variable inventory instantiates canonical tile templates")
	check(tile.pressed.get_connections().size() == 1 and tile.inscription_dropped.get_connections().size() == 1, "refresh retains exactly one selection and drop intent")
	var slot = view._positions.values()[0]
	var payload: Dictionary = tile.drag_payload()
	var foreign = payload.duplicate(true); foreign.scope += 1
	check(not slot._can_drop_data(Vector2.ZERO, foreign), "foreign-view drag remains rejected")
	check(view.attributes_view == null, "shared attributes do not reserve a second character column")
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
