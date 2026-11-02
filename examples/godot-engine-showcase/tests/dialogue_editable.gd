extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var requests: int = root.get_node("GameServices").diagnostics().size()
	var context = load("res://scripts/ui/visual_context.gd").new()
	var screen = load("res://scenes/ui/screens/dialogue_screen.tscn").instantiate()
	var view = screen.get_node("DialogueScene")
	check(view.get_node("DialoguePanel/Stage/Portrait") != null, "production scene contains the editable portrait and reading panel before setup")
	root.add_child(screen)
	var panel = view.get_node("DialoguePanel")
	panel.get_node("SpeechPanel/Content").add_theme_constant_override("separation", 19)
	var model := {"runId":"r", "branchId":"b", "dialogueInstanceId":"d", "nodeId":"n", "title":"Title", "speaker":"Speaker", "text":"Read this", "portrait":"", "options":[{"id":"one", "text":"Choose", "available":true, "cost":"", "reason":"", "reference":"opaque", "command":{"type":"CHOICE", "revision":1}}]}
	var original := model.duplicate(true)
	view.apply_projection(model, context)
	var button: Button = panel.choice_buttons[0]
	var calls: Array = []; view.intent_requested.connect(func(action, args): calls.append([action,args]))
	view.apply_projection(model, context)
	check(panel.choice_buttons[0] == button and panel.get_node("SpeechPanel/Content").get_theme_constant("separation") == 19, "refresh preserves choice identity and authored spacing")
	button.pressed.emit()
	check(calls == [["choice",["opaque"]]], "one click emits one published opaque choice reference")
	check(model == original, "presentation leaves source read model immutable")
	model.options[0].command.revision = 2; model.options[0].reason = "Not available"; model.options[0].available = false
	view.apply_projection(model,context); button.pressed.emit()
	check(button.disabled and calls.size() == 1 and button.get_meta("dialogue_command").revision == 2, "reconciled choice consumes current command and availability")
	view.apply_projection({},context)
	check(not panel.visible and view.get_node("Recover").visible, "empty projection uses native loading and recovery controls")
	screen.queue_free(); await process_frame
	check(root.get_node("GameServices").diagnostics().size() == requests, "passive scene issues no requests")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
