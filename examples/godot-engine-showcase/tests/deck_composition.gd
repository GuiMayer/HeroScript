extends SceneTree
var failures := 0
func _init(): call_deferred("run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func run() -> void:
	var session = load("res://tests/fixtures/deck_composition_session.gd").new()
	var screen = load("res://scenes/ui/screens/unified_loadout.tscn").instantiate(); screen.session = session; root.add_child(screen)
	await process_frame; await process_frame
	screen._select_tab("deck"); await process_frame
	screen.deck_view.subtab = "composition"; screen.deck_view._render()
	var view = screen.deck_view.get_node("Body/Composition")
	check(view.visible and view.scene_file_path.ends_with("deck_composition.tscn"), "composition uses an editable scene in the live loadout")
	var card = view._cards.one
	view._choose("one"); check(session.commits.is_empty(), "selecting the outgoing card never saves an incomplete deck")
	view._choose("two"); await process_frame; await process_frame
	check(session.commits == ["composition"] and session.active == "two", "click replacement previews and autosaves exactly once")
	check(view._cards.one == card and view._outgoing.is_empty(), "authoritative refresh preserves card controls and clears local intent")
	check(view._accepts("two", {"kind": "deck_build", "context": view._model.context, "cardId": "one"}), "reserve drag accepts an active destination")
	check(not view._accepts("two", {"kind": "deck_build", "context": ["foreign"], "cardId": "one"}), "drag cannot cross session context")
	view._replace("two", "one"); await process_frame; await process_frame
	check(session.active == "one" and session.commits.size() == 2, "drag replacement uses the same autosave controller")
	view.get_node("Tools/Modules").item_selected.emit(1); await process_frame; await process_frame
	check(session.module == "alternate" and session.commits.size() == 3, "module choice autosaves through engine preview")
	session.reject_preview = true; view._replace("one", "two"); await process_frame
	check(session.active == "one" and session.commits.size() == 3, "rejected preview consumes nothing")
	session.reject_preview = false; session.uncertain_next = true; view._replace("one", "two"); await process_frame
	check(screen.controller.uncertain and not screen.controller.can_leave(), "uncertain receipt blocks additional edits and premature exit")
	session.uncertain_next = false; screen.controller.recover(); await process_frame; await process_frame
	check(not screen.controller.uncertain and session.active == "two" and session.commits.size() == 4, "receipt recovery rereads confirmed build without resubmitting the edit")
	session.hold_preview = true; view._replace("two", "one")
	session.run.settingId = "another"; session.release.emit(); await process_frame; await process_frame
	check(session.commits.size() == 4 and not screen.controller.current(), "late preview after setting change never commits")
	screen.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
