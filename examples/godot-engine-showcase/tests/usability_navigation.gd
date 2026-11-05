extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(value: bool, message: String) -> void:
	print(("[PASS] " if value else "[FAIL] ") + message)
	if not value: failures += 1
func run() -> void:
	for name in ["combat_header", "turn_controls"]:
		var scene = load("res://scenes/ui/components/" + name + ".tscn").instantiate()
		for node in scene.find_children("*", "Button", true, false):
			if not node.has_theme_stylebox_override("focus"): continue
			var style = node.get_theme_stylebox("focus")
			check(style is StyleBoxFlat and style.border_color.a > 0 and style.border_width_left >= 2, name + ": visible focus " + node.name)
		scene.free()
	var codex = load("res://scenes/ui/screens/codex.tscn").instantiate()
	root.add_child(codex)
	await process_frame
	var calls := [0]
	codex.get_node("Header/Menu").pressed.connect(func(): calls[0] += 1)
	var event := InputEventAction.new()
	event.action = "ui_cancel"; event.pressed = true
	codex._unhandled_key_input(event)
	check(calls[0] == 1, "Escape uses the existing return action")
	codex.hide()
	codex._unhandled_key_input(event)
	check(calls[0] == 1, "Hidden screens do not consume Escape")
	codex.free()
	quit(1 if failures else 0)
