extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func _run() -> void:
	var session = load("res://tests/fixtures/unified_loadout_session.gd").new()
	var controller = load("res://scripts/application/unified_loadout_controller.gd").new(session)
	await controller.refresh(); await controller.ensure_deck()
	session.hold_preview = true
	controller.edit_inscription("stage_drop", ["owned", "one", ""])
	controller.move_card("card-one", "utility")
	check(session.commits.is_empty() and controller.busy, "a second module cannot race an in-flight transaction")
	session.run.settingId = "foreign-setting"; session.hold_preview = false; session.release.emit()
	await process_frame
	check(session.commits.is_empty() and not controller.current(), "late preview cannot write into another setting")
	controller.dispose()
	session = load("res://tests/fixtures/unified_loadout_session.gd").new()
	controller = load("res://scripts/application/unified_loadout_controller.gd").new(session)
	await controller.refresh(); await controller.ensure_deck()
	session.uncertain_next = true
	await controller.edit_inscription("stage_drop", ["owned", "one", ""])
	check(controller.uncertain and not controller.can_leave() and session.commits.size() == 1, "uncertain receipt never appears as confirmed or permits premature exit")
	await controller.recover()
	check(not controller.uncertain and controller.can_leave() and session.commits.size() == 1 and controller.inscriptions.assignments == {"one": "owned"}, "recovery uses session pending command without a second mutation")
	session.mismatched_step = true
	await controller.move_card("card-one", "utility")
	check(session.commits.size() == 1 and session.allocations.is_empty(), "foreign preview step is rejected before commit")
	session.info_fail = true; session.run.sequence += 1
	await controller.refresh()
	check(controller.read_error and not controller.info_ready and controller.attributes.is_empty(), "failed new-version read cannot show old attributes as current")
	session.info_fail = false; await controller.recover()
	check(controller.info_ready and not controller.read_error, "read recovery restores authoritative state")
	controller.apply_modifier("unadvertised")
	check(session.commits.size() == 1, "unadvertised modifier application is never submitted")
	var choice := {"type": "APPLY_CARD_MODIFIER_ITEM", "payload": {"itemInstanceId": "item", "cardInstanceId": "card-one"}}
	session.modifier_choices = [choice]
	await controller.apply_modifier(load("res://scripts/presentation/activity_presenter.gd").option_reference(choice))
	check(session.commits == ["hero", "modifier"], "advertised modifier validates and autosaves as one canonical action")
	controller.dispose(); quit(1 if failures else 0)
