extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func _run() -> void:
	root.get_node("I18n").set_locale("en")
	var session = root.get_node("GameSession")
	session._run = {"runId": "attempt", "sequence": 3, "lifecycle": "Active", "currentNodeId": "fight", "playerEntityId": "hero",
		"map": {"nodes": [{"nodeId": "fight", "activity": {"type": "Encounter"}}], "resolvedNodeIds": []},
		"encounterAttempt": {"number": 1, "outcome": "DEFEAT", "outcomeRuleId": "hero.unavailable", "awaitingConfirmation": true, "pendingRetry": false}}
	session._available_commands = [{"type": "RESOLVE_COMBAT", "validPayload": {"combatId": "first"}}]
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	check(router.gameplay_route() == "activity", "retryable defeat routes to result confirmation without ending campaign")
	router.show_activity(); await settle()
	var screen = router.host.get_child(0)
	var banner = screen.view.get_node("ActivityColumns/ActivityWorkspace/EncounterAttempt")
	check(banner.visible and not banner.get_node("AdjustBuild").visible, "cannot edit build before result transaction is confirmed")
	check(banner.get_node("Reason").text.contains("Hero Unavailable"), "published outcome rule is shown without guessing a resource cause")
	check(session.run.sequence == 3, "reading defeat does not submit confirmation")
	session._run.encounterAttempt.awaitingConfirmation = false
	session._run.encounterAttempt.pendingRetry = true
	session._run.sequence = 4
	session._available_commands = [{"type": "START_ENCOUNTER", "validPayload": {"participants": []}}, {"type": "EDIT_DECK_LOADOUT", "validPayload": {}}]
	screen.refresh_state(router.presentation); await settle()
	check(router.gameplay_route() == "activity" and banner.get_node("AdjustBuild").visible, "confirmed defeat stays in preparation with server-authorized current-character loadout")
	check(session.activity_choices().size() == 1 and screen.view.choice_buttons[0].text == "TRY AGAIN", "dedicated build command never becomes a malformed generic action")
	check(session.run.sequence == 4, "sole retry never auto-starts and skips build preparation")
	var choices = load("res://scripts/application/activity_choices.gd")
	check(choices.only_forced_advance(session.activity_choices()).is_empty() and choices.only_completion(session.activity_choices()).is_empty(), "retry requires an explicit player intent")
	var context = router.visual_context
	banner.apply_projection(session.run, false, context)
	check(not banner.get_node("AdjustBuild").visible, "missing editing permission hides adjustment action")
	var pending = session.run.duplicate(true)
	session._run = pending.duplicate(true)
	check(router.gameplay_route() == "activity", "restored pending snapshot resumes preparation without menu detour")
	session._run.erase("encounterAttempt")
	screen.refresh_state(router.presentation); await settle()
	check(not banner.visible and router.gameplay_route() == "map", "settings without retry retain the existing map route")
	session._run.encounterAttempt = null
	check(router.gameplay_route() == "map", "null optional summary cannot break scene mounting")
	router.queue_free(); await settle()
	root.get_node("GameAudio").shutdown()
	quit(1 if failures else 0)
