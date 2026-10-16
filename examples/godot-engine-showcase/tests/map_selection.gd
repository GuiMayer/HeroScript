extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").new()
	var run := {"runId": "r", "currentNodeId": "a", "map": {"nodes": [{"nodeId": "a", "activity": {"type": "Encounter"}, "nextNodeIds": ["b", "c"]}, {"nodeId": "b", "activity": {"type": "Shop"}, "nextNodeIds": []}, {"nodeId": "c", "activity": {"type": "Custom"}, "nextNodeIds": []}]}}
	var choice := {"type": "ADVANCE_NODE", "subjectId": "b", "payload": {"targetNodeId": "b"}}
	var presenter = load("res://scripts/presentation/activity_presenter.gd").new(run, [choice], {}, context.translator)
	var view = load("res://scripts/ui/map_view.gd").new(); view.size = Vector2(1200, 650); root.add_child(view)
	var intents: Array = []; view.intent_requested.connect(func(action, arguments): intents.append([action, arguments]))
	view.apply_projection(run, [choice], presenter, context)
	for _i in 8: await process_frame
	view.route.buttons.c.pressed.emit()
	check(intents.is_empty() and view._selected == "c" and not view.route.buttons.c.disabled, "unavailable node can be inspected without gameplay command")
	check(view.find_child("MapAction_ADVANCE_NODE", true, false) == null, "inspector cannot travel to a node without a published choice")
	view.route.buttons.b.pressed.emit()
	check(intents.is_empty(), "selecting an available node still does not travel")
	var travel: Button = view.find_child("MapAction_ADVANCE_NODE", true, false)
	travel.pressed.emit()
	check(intents.size() == 1 and intents[0][1][0] == presenter.option_reference(choice), "explicit travel emits only the published opaque reference")
	var state: Dictionary = view.capture_local_state()
	view.route.buttons.a.pressed.emit(); view.restore_local_state(state)
	check(view._selected == "b", "map reading selection restores independently of engine progress")
	check(run.currentNodeId == "a", "inspection/restoration do not change current node")
	var before := intents.size()
	view._next_choices(); view._toggle_overview()
	check(intents.size() == before and view._selected == "b", "next choices and overview only change reading state")
	state = view.capture_local_state(); view._toggle_overview(); view.restore_local_state(state)
	check(view._overview and view.route.nodes.size() == 3, "overview restores without hiding unavailable nodes")
	view.camera.begin(Vector2(100, 100), Vector2(40, 50))
	view.camera.motion(Vector2(102, 101))
	check(not view.camera.dragged, "small pointer movement is not a pan")
	var target: Vector2 = view.camera.motion(Vector2(120, 130))
	check(view.camera.dragged and target == Vector2(20, 20), "camera pan is independent from gameplay and uses a threshold")
	view.camera.cancel()
	check(not view.camera.pressed, "camera releases its gesture")
	view._overview = false; view._zoom = 1; view._update_extent(); view._center_node("b")
	for _i in 8: await process_frame
	var original: String = view._selected
	var point: Vector2 = view.route.buttons.b.get_global_rect().get_center()
	var down := InputEventMouseButton.new(); down.button_index = MOUSE_BUTTON_LEFT; down.pressed = true; down.position = point; down.global_position = point
	root.push_input(down, true); await process_frame
	var move := InputEventMouseMotion.new(); move.position = point + Vector2(40, 20); move.global_position = move.position; move.button_mask = MOUSE_BUTTON_MASK_LEFT
	root.push_input(move, true); await process_frame
	var up := InputEventMouseButton.new(); up.button_index = MOUSE_BUTTON_LEFT; up.position = move.position; up.global_position = move.position
	root.push_input(up, true)
	for _i in 4: await process_frame
	check(intents.size() == before and view._selected == original and not view.camera.pressed, "real pointer drag starting on a node suppresses activation and releases gesture")
	check(not view.route.buttons.b.focus_neighbor_left.is_empty() and not view.route.buttons.b.focus_neighbor_bottom.is_empty(), "map defines explicit directional focus neighbors")
	run.currentNodeId = "c"; run.map.nodes[2].activity.type = "Dialogue"
	run["dialogues"] = [{"activityNodeId": "a", "completed": false}]
	presenter = load("res://scripts/presentation/activity_presenter.gd").new(run, [], {}, context.translator)
	view.apply_projection(run, [], presenter, context); view._inspect("c")
	check(view.find_child("MapResumeDialogue", true, false) == null, "conversation at another stop does not create a resume action here")
	run.dialogues.append({"activityNodeId": "c", "completed": false})
	view.apply_projection(run, [], presenter, context)
	check(view.find_child("MapResumeDialogue", true, false) != null, "same-stop narrative projection refreshes its resume action without geometry changes")
	view.queue_free(); for _i in 8: await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
