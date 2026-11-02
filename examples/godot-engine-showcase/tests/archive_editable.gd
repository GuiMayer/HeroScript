extends SceneTree
var failures := 0
func _init(): call_deferred("_run")
func check(ok: bool, message: String):
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func _run():
	var requests: int = root.get_node("GameServices").diagnostics().size()
	var history = load("res://scenes/ui/screens/run_history.tscn").instantiate()
	root.add_child(history)
	history.entries = [{"runId":"archive", "sequence":4,"lifecycle":"Completed","modeId":"mode","currentNodeId":"stop"}]
	history.list.add_item("Recorded"); history._select(0)
	check(history.selected.runId == "archive" and not history.replay_button.disabled and history.continue_button.disabled, "native archive controls reflect selected immutable record")
	var original: Dictionary = history.entries[0].duplicate(true)
	history.selected.seed = 999
	check(history.entries[0] == original, "archive selection does not mutate its source")
	var list_id: int = history.list.get_instance_id()
	history.get_node("Body").split_offset = 520; history._bind_nodes()
	check(history.list.get_instance_id() == list_id and history.get_node("Body").split_offset == 520, "binding preserves native list and editor split position")
	var timeline = load("res://scenes/ui/screens/timeline.tscn").instantiate(); root.add_child(timeline)
	timeline.entries = [{"runSequence":3,"turn":2,"phase":"middle","actorId":"hero","stateAvailable":true}]
	timeline.list.add_item("Command"); timeline._select(0)
	check(timeline.selected_sequence == 3 and timeline.list.get_selected_items() == PackedInt32Array([0]), "native timeline selection updates only inspector cursor")
	check(timeline.frame_text.text.contains("not available") and timeline.history_view.get_child_count() == 0, "unavailable history capability prevents inspection reads")
	check(timeline.tree_view.columns == 2 and timeline.scrubber.step == 1 and timeline.branch_key.text == "alternative", "branch tree and scrubber are authored in scene")
	history.queue_free(); timeline.queue_free(); await process_frame
	check(root.get_node("GameServices").diagnostics().size() == requests, "passive archive previews issue no requests")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
