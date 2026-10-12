extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var reading = load("res://scripts/ui/dialogue_reading.gd").new()
	var model := {"runId": "r", "branchId": "b", "dialogueInstanceId": "d", "nodeId": "n", "text": "First paragraph is read before choosing. Second paragraph is still only local reading."}
	reading.bind(model, "en", 45)
	check(reading.pages.size() > 1 and reading.fully_revealed(), "instant readable text is default")
	reading.gradual = true; reading.advance_reading()
	check(reading.page == 0 and reading.fully_revealed(), "confirm reveals without advancing or choosing")
	reading.advance_reading()
	check(reading.page == 1 and not reading.fully_revealed(), "second confirm changes only the local page")
	var state: Dictionary = reading.capture()
	reading.page = 0; reading.restore(state)
	check(reading.page == 1, "reading state restores in the same context")
	model.branchId = "other"; reading.bind(model, "en", 45); reading.restore(state)
	check(reading.page == 0, "reading state cannot leak to another branch")
	var panel = load("res://scripts/ui/dialogue_panel.gd").new()
	root.add_child(panel); panel.size = Vector2(1000, 620)
	model.merge({"title": "A conversation", "speaker": "", "portrait": "", "history": [], "options": [{"id": "one", "text": "A costly choice", "available": true, "cost": "5 Gold", "reason": "", "command": {"type": "CHOOSE_DIALOGUE_OPTION"}}]})
	panel.apply_model(model)
	var calls: Array = []; panel.choice_requested.connect(func(command): calls.append(command))
	panel.reading.gradual = true; panel.reading.revealed = 0.; panel._sync_reading()
	panel.choice_buttons[0].pressed.emit()
	check(calls.is_empty() and panel.choice_buttons[0].disabled, "unread text cannot submit a choice")
	panel.advance_reading(); panel.set_read_only(true)
	check(panel.choice_buttons[0].disabled, "session lock remains authoritative after reveal")
	panel.queue_free(); for _i in 8: await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
