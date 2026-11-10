extends SceneTree

var failures := 0

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1

func wait_until(predicate: Callable, timeout := 15000) -> bool:
	var deadline := Time.get_ticks_msec() + timeout
	while not predicate.call() and Time.get_ticks_msec() < deadline:
		await process_frame
	return predicate.call()

func actor(session, id: String) -> Dictionary:
	for value in session.combat.get("actors", []):
		if value.get("instanceId") == id: return value
	return {}

func current_block(session) -> float:
	return float(actor(session, "player").get("resources", {}).get("block", {}).get("current", -1))

func _run() -> void:
	var prefs = root.get_node("Preferences")
	prefs.api_url = "http://127.0.0.1:61989"
	prefs.reduced_motion = true
	prefs.auto_animations = true
	prefs.changed.emit()
	var session = root.get_node("GameSession")
	check(await session.connect_engine("prototype-test"), "connect to published MVP")
	check(await session.start_campaign(int(Time.get_unix_time_from_system()) + 11700000), "start real seeded campaign")
	if failures: quit(1); return
	check(await session.execute_run_command("START_DIALOGUE", {"dialogueId": "prototype_opening"}), "start opening dialogue")
	var choice: Dictionary = session.command("CHOOSE_DIALOGUE_OPTION").get("validPayload", {})
	check(await session.execute_run_command("CHOOSE_DIALOGUE_OPTION", {"dialogueInstanceId": choice.get("dialogueInstanceId"), "nodeId": choice.get("nodeId"), "choiceId": "continue"}), "choose opening dialogue")
	check(await session.execute_run_command("RESOLVE_NODE", {"currentNodeId": "chapter_opening"}), "resolve opening")
	check(await session.execute_run_command("ADVANCE_NODE", {"targetNodeId": "chapter_scout"}), "advance to scout")
	var start: Dictionary = session.command("START_ENCOUNTER")
	check(await session.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})), "start encounter")
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await process_frame
	router.open_game()
	check(await wait_until(func(): return router.current_screen == "combat" and not router.host.get_child(0).controller.locked()), "mount combat screen")
	if failures: quit(1); return
	check(actor(session, "enemy_0").get("definitionId") == "inimigo_automato_batedor", "published scout is present")
	check(not actor(session, "player").get("resources", {}).has("heat"), "MVP player has no Heat resource")
	# Continue the same encounter using legal published candidates until its outcome.
	var driver = load("res://tests/smoke.gd").new()
	var completed := false
	for _step in 60:
		if str(session.combat.get("status", "ACTIVE")) != "ACTIVE":
			completed = str(session.combat.get("status", "")) == "VICTORY"
			break
		var candidate: Dictionary = driver.damaging_card_candidate()
		if candidate.is_empty(): candidate = driver.first_card_candidate()
		if candidate.is_empty():
			var end: Array = session.legal_actions.filter(func(item): return str(item.get("command", {}).get("actionType", "")) == "END_TURN")
			if end.is_empty() or not await session.submit_candidate(end[0]): break
		else:
			if not await session.submit_candidate(candidate): break
		if not await wait_until(func(): return session.synchronized and not session.busy and not session.has_pending_command): break
	check(completed, "first MVP encounter reaches victory with legal actions")
	if completed:
		var options: Array = session.activity_choices().filter(func(item): return str(item.get("type", "")) == "RESOLVE_COMBAT")
		check(not options.is_empty() and await session.submit_activity(options[0]), "resolve MVP encounter")
		var run_id: String = str(session.run.runId)
		# Remove the route before capturing: the map can automatically issue its
		# own node-resolution command as soon as it opens.
		router.queue_free()
		for _frame in 8: await process_frame
		check(await wait_until(func(): return not session.busy and not session.has_pending_command), "automatic navigation settles before the snapshot")
		var before: Dictionary = await session._gateway.read_session(run_id)
		check(before.get("ok", false), "read authoritative session fingerprint")
		var fingerprint := str(before.get("data", {}).get("version", {}).get("stateFingerprint", ""))
		var captured: Dictionary = before.get("data", {}).duplicate(true)
		var verified: Dictionary = await session.verify()
		check(verified.get("ok", false) and verified.get("data", {}).get("isValid", false), "completed encounter replay is valid")
		var after: Dictionary = await session._gateway.read_session(run_id)
		check(not fingerprint.is_empty() and after.get("data", {}).get("version", {}).get("stateFingerprint", "") == fingerprint and before.get("data", {}) == captured, "replay preserves the retained snapshot and authoritative fingerprint")
		check(await session.continue_run(run_id), "resume the same MVP run")
		var resumed: Dictionary = await session._gateway.read_session(run_id)
		check(str(session.run.runId) == run_id and resumed.get("data", {}).get("version", {}).get("stateFingerprint", "") == fingerprint, "resume restores the same authoritative state")
	driver.free()
	root.get_node("Playback").clear()
	if is_instance_valid(router): router.queue_free()
	for _frame in 8: await process_frame
	root.get_node("GameAudio").shutdown()
	await create_timer(0.2).timeout
	quit(1 if failures else 0)
