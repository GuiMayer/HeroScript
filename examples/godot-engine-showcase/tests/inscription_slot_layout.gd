extends SceneTree
var failures := 0
class Session extends "res://tests/fixtures/unified_loadout_session.gd":
	func _init() -> void: run.settingId = "prototype-test"
	func snapshot() -> Dictionary:
		var value := super.snapshot()
		value.policy = {"capacityOnly": true, "capacity": 6, "slots": [], "equipmentWindows": ["OutsideCombat"]}
		# Real HTTP JSON numbers are floats in GDScript.
		return JSON.parse_string(JSON.stringify(value))
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func settle() -> void:
	for _frame in 12: await process_frame
func _run() -> void:
	root.size = Vector2i(1920, 1080); root.content_scale_size = root.size
	var session := Session.new()
	var screen = load("res://scenes/ui/screens/unified_loadout.tscn").instantiate()
	screen.session = session; screen.initial_tab = "inscriptions"; root.add_child(screen)
	await settle()
	var view = screen.inscriptions_view
	check(view.slot_grid.get_child_count() == 6 and view._positions.size() == 6, "MVP exposes six stable common positions even before equipping anything")
	check(view.find_child("CharacterIdentity", true, false).visible and view.find_child("IdentityPlaceholder", true, false).visible, "older content revisions reserve a visibly separate identity position without fabricating an item")
	screen._select_item("owned"); screen._select_slot("capacity_6"); await settle()
	check(session.commits == ["hero"] and session.assignments == {"capacity_6": "owned"}, "the sixth common position supports authoritative autosave with JSON-decoded snapshots")
	check(view.slot_grid.get_child_count() == 6, "equipping does not add or remove displayed positions")
	var current: Dictionary = screen.controller.inscriptions.source.duplicate(true)
	current.policy = {"capacity": 6, "slots": [{"slotId": "identity"}] + range(1, 7).map(func(index): return {"slotId": "capacity_%d" % index})}
	screen.controller.inscriptions.accept(current); screen._render(); await settle()
	check(view.identity_slots.get_child_count() == 1 and view.slot_grid.get_child_count() == 6, "published identity position is separated from the six common positions")
	check(not view.find_child("IdentityPlaceholder", true, false).visible and view._positions.identity.get_parent() == view.identity_slots, "real identity slots reuse the same interactive inscription component")
	check(preload("res://tests/layout_inspector.gd").vertical_text_issues(screen).is_empty(), "identity and common slots contain no compressed vertical text")
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="):
			await RenderingServer.frame_post_draw
			root.get_texture().get_image().save_png(arg.trim_prefix("--lab-output=").path_join("identity-loadout.png"))
	screen.queue_free(); await settle(); root.get_node("GameAudio").shutdown()
	quit(1 if failures else 0)
