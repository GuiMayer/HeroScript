extends SceneTree
var failures := 0
func _init(): call_deferred("_run")
func check(ok: bool, message: String):
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func _run():
	root.gui_embed_subwindows = true
	var prefs = root.get_node("Preferences")
	var saved := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	var session = root.get_node("GameSession")
	var before := JSON.stringify([session.run,session.combat])
	var requests: int = root.get_node("GameServices").diagnostics().size()
	var catalog = load("res://scenes/ui/workbenches/screen_catalog_workbench.tscn").instantiate(); root.add_child(catalog)
	for index in catalog.ENTRIES.size():
		for state in [0,1]:
			catalog.get_node("%StateSelector").select(state); catalog.show_preview(index)
			await process_frame; await process_frame
			check(is_instance_valid(catalog.preview) and catalog.preview.scene_file_path == catalog.ENTRIES[index][1], "canonical preview: %s / %s" % [catalog.ENTRIES[index][0],catalog.STATES[state]])
	for state in [2,3,4,5]:
		catalog.get_node("%StateSelector").select(state); catalog.show_preview(7)
		await process_frame
		check(catalog.preview.panel.visible == (state not in [3,4]), "dialogue preview state: " + catalog.STATES[state])
	catalog.queue_free(); await process_frame; await process_frame
	check(JSON.stringify([session.run,session.combat]) == before, "catalog preserves live authoritative state")
	check(root.get_node("GameServices").diagnostics().size() == requests, "all catalog screens and states issue zero HTTP requests")
	check((FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else "") == saved, "catalog writes no preferences")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
