extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func _run() -> void:
	var session = load("res://tests/fixtures/unified_loadout_session.gd").new()
	var screen = load("res://scenes/ui/screens/unified_loadout.tscn").instantiate(); screen.session = session; root.add_child(screen)
	await process_frame; await process_frame
	check(screen.controller.inscriptions != null, "canonical current-character loadout mounted")
	check(screen.inscriptions_view.attributes_view == null, "module does not duplicate shared attributes")
	screen._select_item("owned"); screen._select_slot("one")
	await process_frame; await process_frame
	check(session.commits == ["hero"] and session.assignments == {"one": "owned"}, "one equip intent automatically previews commits and rereads")
	screen._select_tab("deck"); screen._select_tab("inscriptions")
	check(session.commits.size() == 1, "inspection and tab navigation never write")
	var original_card = screen.deck_view._card_cache.get("card-one")
	screen.controller.move_card("card-one", "utility")
	await process_frame; await process_frame
	check(session.commits == ["hero", "compartments"] and session.allocations.size() == 1, "compartments autosave without Apply using latest run version")
	check(is_instance_valid(original_card) and screen.deck_view._card_cache.get("card-one") == original_card, "canonical refresh retains card components instead of flashing replacements")
	session.hold_preview = true
	screen.controller.move_card("card-one", "")
	screen._select_tab("relics")
	check(screen.controller.busy and not screen.controller.can_leave(), "tab switch retains pending save and prevents premature exit")
	session.hold_preview = false; session.release.emit()
	await process_frame; await process_frame
	check(session.allocations.is_empty() and session.commits.size() == 3, "hidden module completes exactly one canonical transaction")
	session.reject_preview = true
	screen.controller.move_card("card-one", "utility")
	await process_frame
	check(session.commits.size() == 3 and session.allocations.is_empty(), "rejected component composition never commits")
	session.can_equip = false
	session.assignments.clear(); session.run.sequence += 1
	await screen.controller.refresh()
	check(screen.inscriptions_view._inventory.owned.drag_payload() == null, "closed equipment window keeps inspection available but prevents dragging")
	check(not screen.inscriptions_view._positions.one._can_drop_data(Vector2.ZERO, {"kind": "inscription_draft", "scope": screen.inscriptions_view._positions.one.drag_scope, "revision": screen.inscriptions_view._positions.one.revision, "item": "owned", "source": ""}), "closed equipment window rejects drops")
	var preview_count: int = session.previews
	screen._select_item("owned"); screen._select_slot("one")
	await process_frame
	check(session.previews == preview_count and session.commits.size() == 3 and screen.controller.message == "Equipment cannot be changed here under the current game mode rules.", "fresh authoritative equipment lock explains the refusal without preview or write")
	session.can_equip = true; session.reject_preview = false
	await screen.controller.refresh()
	screen._select_item("owned"); screen._select_slot("one")
	await process_frame; await process_frame
	check(session.commits.size() == 4 and session.assignments == {"one": "owned"}, "equipment works again immediately when the authoritative window reopens")
	screen.queue_free(); await process_frame
	quit(1 if failures else 0)
