extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print(("[PASS] " if ok else "[FAIL] ") + message)
	if not ok: failures += 1
func run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").new(null, {"reduced_motion": true})
	var dialog = load("res://scripts/ui/inspection_dialog.gd").create(context)
	var data := {"detail": "Full", "value": 17}
	var before := JSON.stringify(data)
	dialog.setup("First", "Summary", data)
	root.add_child(dialog)
	var content = dialog.content
	var summary = dialog.get_node("%Summary")
	content.add_theme_constant_override("separation", 19)
	dialog.get_node("%TechnicalToggle").button_pressed = true
	check(summary.text.contains("17"), "Inspection toggle displays supplied technical data")
	dialog.setup("Second", "Updated", {})
	check(dialog.content == content and dialog.get_node("%Summary") == summary, "Inspection updates retain authored nodes")
	check(content.get_theme_constant("separation") == 19, "Inspection update preserves authored spacing")
	check(summary.text == "Updated" and not dialog.get_node("%TechnicalToggle").visible, "Updated model resets obsolete technical state")
	check(JSON.stringify(data) == before, "Inspection never mutates input")
	check(dialog.confirmed.get_connections().size() == 1, "Repeated setup does not duplicate close handlers")
	var history = load("res://scenes/ui/overlays/conversation_history.tscn").instantiate()
	history.visual_context = context
	history.setup("History", "Speaker: remembered line")
	check(history.exclusive and history.get_node("%Summary").text.contains("remembered"), "History reuses the same modal structure")
	var confirmation = load("res://scenes/ui/overlays/confirmation_dialog.tscn").instantiate()
	check(confirmation is ConfirmationDialog, "Confirmation template retains the native dialog contract")
	var toast = load("res://scenes/ui/overlays/toast.tscn").instantiate()
	check(toast is PanelContainer and toast.mouse_filter == Control.MOUSE_FILTER_IGNORE and not toast.visible, "Toast template is passive and initially hidden")
	for view in [dialog, history, confirmation, toast]: view.free()
	quit(1 if failures else 0)
