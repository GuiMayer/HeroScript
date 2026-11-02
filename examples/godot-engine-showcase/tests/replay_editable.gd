extends SceneTree
var failures := 0
func _init(): call_deferred("_run")
func check(ok: bool, message: String):
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func _run():
	var requests: int = root.get_node("GameServices").diagnostics().size()
	var context = load("res://scripts/ui/visual_context.gd").new()
	var screen = load("res://scenes/ui/screens/run_replay.tscn").instantiate()
	var view = screen.get_node("RunReplayView")
	view.setup(context,true); root.add_child(screen)
	var calls: Array = []; view.intent_requested.connect(func(action,args): calls.append([action,args]))
	var list_id: int = view.list.get_instance_id()
	view.setup(context,true); view.play_button.pressed.emit()
	check(calls == [["toggle_play",[]]], "repeated binding keeps one replay intent connection")
	view.add_theme_constant_override("separation",17)
	var model := {"entries":[{"sequence":1,"commandType":"DRAW","step":1}],"selected_index":0,"through_sequence":1,"page_error":false,"state_text":"Saved state","frame_text":"Frame","has_more":true,"playing":false,"verifying":false,"page_loading":true,"inspection_allowed":false,"presentation_loading":false}
	var original := model.duplicate(true)
	view.apply_model(model); view.apply_model(model)
	check(view.list.get_instance_id() == list_id and view.list.item_count == 1 and view.get_theme_constant("separation") == 17, "progressive refresh retains list identity and editor metrics")
	check(view.more_button.disabled and not view.inspect_button.visible and not view.watch_button.visible, "loading and inspection capabilities control native tools")
	check(model == original and view.state_text.get_parsed_text() == "Saved state", "replay reads immutable models without simulating gameplay")
	var history = load("res://scenes/ui/components/historical_combat.tscn").instantiate(); history.visual_context = context; root.add_child(history)
	var row = history.get_node("Scroll/Actors"); row.add_theme_constant_override("separation",31)
	history.display({"combat":{"actors":[]},"run":{},"runSequence":4},{})
	history.display({"combat":{"actors":[]},"run":{},"runSequence":5},{})
	check(history.get_node("Scroll/Actors") == row and row.get_theme_constant("separation") == 31 and history.get_node("Heading").text.ends_with("#5"), "historical refresh retains authored shell and updates saved-command heading")
	history.queue_free(); screen.queue_free(); await process_frame
	check(root.get_node("GameServices").diagnostics().size() == requests, "passive replay scene does not read or write the live session")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
