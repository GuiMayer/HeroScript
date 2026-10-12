extends SceneTree
var failures := 0
var calls: Array = []
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func _run() -> void:
	var panel = load("res://scripts/ui/dialogue_panel.gd").new()
	root.add_child(panel)
	var model := {"title": "Dialogue", "speaker": "Speaker", "text": "First", "portrait": "unknown", "history": [],
		"options": [{"id": "same", "text": "Choose", "cost": "", "reason": "", "available": true, "command": {"payload": {"node": "first"}}}]}
	panel.apply_model(model)
	panel.choice_requested.connect(func(choice): calls.append(choice))
	var retained: Button = panel.choice_buttons[0]
	model.text = "Second"
	model.options[0].command.payload.node = "second"
	model.options.append({"id": "new", "text": "New", "cost": "", "reason": "", "available": true, "command": {"payload": {"node": "new"}}})
	model.history = [{"speaker": "Speaker", "text": "First"}]
	panel.apply_model(model)
	await settle()
	retained.pressed.emit()
	panel.choice_buttons[1].pressed.emit()
	check(panel.choice_buttons[0] == retained and calls.size() == 2 and calls[0].payload.node == "second" and calls[1].payload.node == "new", "retained and new dialogue choices resolve current payload through the live panel")
	panel._open_history()
	var history: Window = panel.get_children().filter(func(child): return child is Window)[0]
	check(history.visible, "conversation history opens in an independent read-only modal")
	history.queue_free()
	var context = load("res://scripts/ui/visual_context.gd").new(null, {"reduced_motion": false})
	var orphan_before: int = int(Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT))
	var hand = load("res://scripts/ui/card_hand_view.gd").new(context)
	root.add_child(hand)
	hand.sync_models([{ "cardInstanceId": "a", "name": "Test", "summary": "Test", "costs": []}], "lifecycle")
	await settle()
	hand.show_overview()
	await settle()
	var overview = hand.get_children().filter(func(child): return child is Window)[0]
	check(overview.visual_context == context and overview.hand.visual_context == context, "overview inherits the injected presentation context")
	overview.queue_free()
	await settle()
	var overlay := Control.new()
	root.add_child(overlay)
	var card: CardView = hand.card_for("a")
	var flight := {"completed": false}
	_finish_flight(card, overlay, flight)
	await process_frame
	overlay.queue_free()
	await settle()
	check(flight.completed, "closing feedback while card flies completes the presentation await")
	hand.queue_free(); panel.queue_free()
	await settle()
	check(not context.changed.has_connections(), "destroyed hand disconnects its presentation subscriptions")
	check(int(Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT)) <= orphan_before, "closing modals, hand and interrupted flight leaves no new orphan nodes")
	var warm_count := 0
	for iteration in 31:
		var sample := CardView.new(context)
		root.add_child(sample)
		sample.configure({"cardInstanceId": "sample", "name": "Sample", "costs": []})
		sample.queue_free()
		await settle()
		if iteration == 0: warm_count = int(Performance.get_monitor(Performance.OBJECT_COUNT))
	check(int(Performance.get_monitor(Performance.OBJECT_COUNT)) <= warm_count + 2, "thirty component lifecycles have bounded object count after asset warmup")
	root.get_node("GameAudio").shutdown()
	await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
func _finish_flight(card: CardView, overlay: Control, result: Dictionary) -> void:
	await card.fly_to(Vector2(400, 400), overlay)
	result.completed = true
