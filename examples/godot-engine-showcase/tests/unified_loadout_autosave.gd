extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func _run() -> void:
	var session = load("res://tests/fixtures/unified_loadout_session.gd").new()
	var screen = load("res://scripts/ui/unified_loadout_screen.gd").new(); screen.session = session; root.add_child(screen)
	await process_frame; await process_frame
	check(screen.controller.inscriptions != null, "canonical current-character loadout mounted")
	check(screen.inscriptions_view.attributes_view == null, "module does not duplicate shared attributes")
	screen._select_item("owned"); screen._select_slot("one")
	await process_frame; await process_frame
	check(session.commits == ["hero"] and session.assignments == {"one": "owned"}, "one equip intent automatically previews commits and rereads")
	screen._select_tab("deck"); screen._select_tab("inscriptions")
	check(session.commits.size() == 1, "inspection and tab navigation never write")
	screen.queue_free(); await process_frame
	quit(1 if failures else 0)
