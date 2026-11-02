extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print(("[PASS] " if ok else "[FAIL] ") + message)
	if not ok: failures += 1
func settle() -> void:
	for frame in 6: await process_frame
func run() -> void:
	var prefs = root.get_node("Preferences")
	var before := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	var session = root.get_node("GameSession")
	var snapshot := JSON.stringify(session.run)
	var catalog = load("res://scenes/ui/workbenches/screen_catalog_workbench.tscn").instantiate()
	root.add_child(catalog)
	await settle()
	check(catalog.preview.scene_file_path.ends_with("ui_catalog_workbench.tscn"), "Catalog opens the shared component scene")
	catalog.show_preview(1)
	await settle()
	check(catalog.preview.scene_file_path == "res://scenes/unified_loadout_lab.tscn", "Catalog and production registry instantiate the same route scene")
	check(catalog.get_node("%ContentHost").get_child_count() == 1, "Changing preview replaces only the hosted instance")
	catalog.show_preview(0)
	await settle()
	check(catalog.preview.scene_file_path.ends_with("ui_catalog_workbench.tscn"), "Returning to a preview mounts a fresh valid scene")
	check(JSON.stringify(session.run) == snapshot, "Preview never changes the run snapshot")
	check(root.get_node("GameServices").diagnostics().is_empty(), "Preview sends no HTTP requests")
	check((FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else "") == before, "Preview never writes preferences")
	catalog.free()
	quit(1 if failures else 0)
