extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func wait_until(predicate: Callable, timeout := 15000) -> bool:
	var deadline := Time.get_ticks_msec() + timeout
	while not predicate.call() and Time.get_ticks_msec() < deadline: await process_frame
	return predicate.call()
func run() -> void:
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-"):
		check(false, "prototype test requires isolated user directory"); quit(1); return
	var prefs = root.get_node("Preferences"); var previous: String = prefs.api_url
	prefs.api_url = "http://127.0.0.1:61989"; prefs.reduced_motion = true; prefs.auto_animations = true; prefs.changed.emit()
	var session = root.get_node("GameSession")
	check(await session.connect_engine("prototype-test"), "Godot discovers opt-in tactical campaign setting")
	check(await session.start_campaign(2026110311), "Godot creates JSON-authored prototype campaign")
	if failures: quit(1); return
	# The setting launch is now the playable chapter, not the short proof run.
	check(await session.execute_run_command("START_DIALOGUE", {"dialogueId": "prototype_opening"}), "normal launch opens authored chapter dialogue")
	var dialogue = session.command("CHOOSE_DIALOGUE_OPTION")
	var choice: Dictionary = dialogue.get("validPayload", {})
	check(not choice.is_empty() and await session.execute_run_command("CHOOSE_DIALOGUE_OPTION", {"dialogueInstanceId": choice.get("dialogueInstanceId"), "nodeId": choice.get("nodeId"), "choiceId": "continue"}), "canonical choice closes the opening briefing")
	check(await session.execute_run_command("RESOLVE_NODE", {"currentNodeId": "chapter_opening"}), "completed dialogue resolves without client-authored consequence")
	check(await session.execute_run_command("ADVANCE_NODE", {"targetNodeId": "chapter_scout"}), "published chapter reaches the first encounter")
	var start: Dictionary = session.command("START_ENCOUNTER")
	check(await session.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})), "published encounter initializes selected build and support")
	var router = load("res://main.tscn").instantiate(); root.add_child(router)
	await process_frame; router.open_game()
	check(await wait_until(func(): return router.current_screen == "combat" and not router.host.get_child(0).controller.locked()), "canonical combat scene mounts and input becomes ready")
	if failures: quit(1); return
	var build: Dictionary = await session.read_deck_loadout()
	check(build.get("ok", false) and build.data.selection.groups.hero.size() == 15 and build.data.selection.groups.module.size() == 5, "Godot reads 15 plus 5 composition without local deck construction")
	check(not build.data.canChange, "active encounter prevents build changes")
	for index in 3:
		var hand: Array = session.card_zones.get("zones", []).filter(func(zone): return zone.zoneId == "hand")
		check(hand.size() == 1 and hand[0].cards.size() == [5,7,8][index], "retained hand follows published capacity at activation %s" % str(index + 1))
		check(await session.execute_combat_command("END_TURN", {"actorId": "player"}), "configured activation advances without a universal energy resource")
		check(await wait_until(func(): return session.synchronized and not router.host.get_child(0).controller.locked()), "animation executor releases the next tactical input")
	check(session.run.get("supportCombat", {}).get("remainingOwnerActivations") == 3, "support cadence is published after its third-activation action")
	var screen = router.host.get_child(0)
	check(screen.view.header_view.get_node("Spacer/SupportStatus").visible, "same combat component displays the configured support")
	root.get_node("Playback").clear(); router.queue_free()
	for _frame in 8: await process_frame
	prefs.api_url = previous; prefs.changed.emit(); root.get_node("GameAudio").shutdown()
	await create_timer(0.2).timeout
	quit(1 if failures else 0)
