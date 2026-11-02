extends SceneTree
var failures := 0
func _init(): call_deferred("run")
func check(ok: bool, label: String):
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run():
	root.get_node("GameSession").available = false
	var requests = root.get_node("GameServices").diagnostics().size()
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await process_frame
	router.show_settings()
	var view = router.host.get_child(0)
	var volume = view.find_child("MasterVolumeSlider", true, false)
	view._show_category("audio")
	view._show_category("controls")
	view._show_category("audio")
	check(view.find_child("MasterVolumeSlider", true, false) == volume, "category changes preserve authored controls")
	check(view.page_content.get_child_count() == 6, "six editable pages are mounted once")
	var visible_pages := 0
	for page in view.page_content.get_children():
		if page.visible: visible_pages += 1
	check(visible_pages == 1 and view.selected_category == "audio", "only selected category participates in layout")
	var page = view.page_content.get_node("AudioPage")
	page.add_theme_constant_override("separation", 19)
	view._show_category("general")
	view._show_category("audio")
	check(page.get_theme_constant("separation") == 19, "authored geometry survives category refresh")
	check(volume.value_changed.get_connections().size() == 1, "switching categories does not duplicate persistence callbacks")
	check(requests == root.get_node("GameServices").diagnostics().size(), "settings navigation sends no engine requests")
	check(view.scene_file_path == "res://scenes/ui/screens/settings.tscn", "router instances canonical production scene")
	router.queue_free()
	await process_frame
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
