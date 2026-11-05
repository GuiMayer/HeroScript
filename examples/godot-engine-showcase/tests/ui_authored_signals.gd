extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run() -> void:
	var turn = load("res://scenes/ui/components/turn_controls.tscn").instantiate()
	var events: Array = []
	turn.end_requested.connect(func(): events.append("end"))
	turn.cancel_requested.connect(func(): events.append("cancel"))
	root.add_child(turn)
	for _i in 3: turn.mount(turn.visual_context.translator, {"confirm_action": "F", "end_turn": "E"}, {})
	turn.get_node("EndTurnButton").pressed.emit()
	turn.get_node("ContextActionSlot/SelectionActions/CancelSelection").pressed.emit()
	check(events == ["end", "cancel"], "authored turn signals emit once after repeated mounting")
	var header = load("res://scenes/ui/components/combat_header.tscn").instantiate()
	header.pause_requested.connect(func(): events.append("pause"))
	root.add_child(header)
	header.get_node("PauseButton").pressed.emit()
	check(events.back() == "pause", "authored pause signal is connected")
	turn.queue_free(); header.queue_free()
	await process_frame
	quit(1 if failures else 0)
