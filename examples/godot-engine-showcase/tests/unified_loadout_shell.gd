extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func _run() -> void:
	var lab = load("res://scenes/unified_loadout_lab.tscn").instantiate(); root.add_child(lab)
	await process_frame; await process_frame
	check(lab.shell.buttons.size() == 5, "five independent presentation modules")
	var first = lab.shell.modules.overview
	lab.shell.select("deck"); lab.shell.select("overview")
	check(lab.shell.modules.overview == first and first.visible, "tab navigation preserves mounted views")
	check(lab.shell.attributes_panel.name == "SharedCharacterAttributes", "one shared attribute panel")
	check(not FileAccess.get_file_as_string("res://scripts/labs/unified_loadout_lab.gd").contains("GameSession"), "lab has no session fallback")
	lab.queue_free(); await process_frame
	quit(1 if failures else 0)
