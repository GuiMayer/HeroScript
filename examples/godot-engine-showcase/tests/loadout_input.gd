extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 12: await process_frame
func move_mouse(point: Vector2, held := false) -> void:
	var event := InputEventMouseMotion.new(); event.position = point; event.relative = Vector2(30, 0)
	event.button_mask = MOUSE_BUTTON_MASK_LEFT if held else 0; root.push_input(event)
func mouse_button(point: Vector2, pressed: bool) -> void:
	var event := InputEventMouseButton.new(); event.position = point; event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed; root.push_input(event)
func _run() -> void:
	root.size = Vector2i(1920, 1080); root.content_scale_size = root.size
	var session = load("res://tests/run_loadout.gd").Session.new(); session.fail = false
	var screen = load("res://scenes/run_loadout.tscn").instantiate(); screen.session = session; root.add_child(screen); await settle()
	var source: Button = screen.inscriptions_view._inventory.owned; var target: Button = screen.inscriptions_view._positions.one
	var start := source.get_global_rect().get_center(); var end := target.get_global_rect().get_center()
	move_mouse(start); await settle(); mouse_button(start, true); await settle(); move_mouse(start + Vector2(30, 0), true); await settle()
	check(root.gui_is_dragging(), "native pointer starts an inscription drag in the editorial view")
	move_mouse(end, true); await settle(); mouse_button(end, false); await settle()
	check(session.assignments == {"one": "owned"} and session.commits.size() == 1, "native drop validates and saves equipment exactly once")
	check(screen.controller.inscriptions.assignments == session.assignments and not screen.controller.busy, "drag returns to confirmed equipment and unlocks input")
	screen.controller.edit_inscription("clear"); await settle()
	move_mouse(start); mouse_button(start, true); mouse_button(start, false); await settle()
	check(session.assignments.is_empty(), "clicking an inventory item only inspects it before a destination is chosen")
	move_mouse(end); mouse_button(end, true); mouse_button(end, false); await settle()
	check(session.assignments == {"one": "owned"} and session.commits.size() == 3, "item then slot equips and saves directly, without a hidden extra button")
	move_mouse(end); mouse_button(end, true); mouse_button(end, false); await settle()
	check(session.commits.size() == 3, "inspecting a slot after saving does not submit another equipment command")
	screen.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
