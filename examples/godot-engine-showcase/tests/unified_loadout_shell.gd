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
	lab.shell.select("deck")
	var card: Button = lab.shell.modules.deck.find_children("*", "Button", true, false)[0]
	card.grab_focus(); await process_frame
	lab.shell.select("overview")
	check(root.gui_get_focus_owner() == lab.shell.buttons.overview, "tab changes never leave focus in a hidden module")
	lab.shell.select("deck")
	check(root.gui_get_focus_owner() == card, "returning to a module restores its keyboard focus")
	lab.shell.buttons.overview.grab_focus()
	var next := InputEventAction.new(); next.action = "ui_right"; next.pressed = true; root.push_input(next); await process_frame
	check(root.gui_get_focus_owner() == lab.shell.buttons.inscriptions, "directional UI input reaches the next module tab")
	var accept := InputEventAction.new(); accept.action = "ui_accept"; accept.pressed = true; root.push_input(accept); await process_frame
	accept = InputEventAction.new(); accept.action = "ui_accept"; accept.pressed = false; root.push_input(accept); await process_frame
	check(lab.shell.active_tab == "inscriptions", "keyboard/controller accept activates the focused tab")
	root.size = Vector2i(1280, 720); root.content_scale_size = root.size; await process_frame
	lab.shell._drawer_open = true; lab.shell._responsive()
	var sources: Button = lab.shell.attributes_view.find_children("*", "Button", true, false)[0]
	sources.grab_focus(); lab.shell._drawer_open = false; lab.shell._responsive()
	check(root.gui_get_focus_owner() == lab.shell.attributes_button, "closing the floating drawer restores a visible focus target")
	lab.queue_free(); await process_frame
	quit(1 if failures else 0)
