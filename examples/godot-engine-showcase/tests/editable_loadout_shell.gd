extends SceneTree
var failures := 0
func _init(): call_deferred("run")
func check(ok: bool, label: String):
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run():
	var requests = root.get_node("GameServices").diagnostics().size()
	var lab = load("res://scenes/unified_loadout_lab.tscn").instantiate(); root.add_child(lab)
	await process_frame
	var shell = lab.shell
	var first_tab = shell.buttons.overview
	shell.get_node("%Page").add_theme_constant_override("separation", 23)
	shell.mount(shell.visual_context)
	check(shell.buttons.overview == first_tab and shell.buttons.size() == 5, "remount retains catalog tabs and one set of controls")
	check(shell.get_node("%Page").get_theme_constant("separation") == 23, "authored shell spacing survives model binding")
	check(shell.get_node("%BackButton").pressed.get_connections().size() == 1, "remount preserves a single back intent")
	check(shell.attributes_view.scene_file_path == "res://scenes/ui/components/loadout/character_attributes_view.tscn", "shell composes canonical attributes scene")
	check(shell.scene_file_path == "res://scenes/ui/components/loadout/loadout_page.tscn", "lab uses production shell scene")
	check(requests == root.get_node("GameServices").diagnostics().size(), "lab shell makes no engine calls")
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
