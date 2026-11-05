extends SceneTree
## Own isolated Kestrel/store only. Real REST commands, production screen, semantic replay.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 6: await process_frame
func _run() -> void:
	var session = root.get_node("GameSession")
	var cursor = root.get_node("Playback")
	var prefs = root.get_node("Preferences")
	prefs.auto_animations = true; prefs.animation_speed = 4.
	var router = load("res://main.tscn").instantiate(); root.add_child(router); await settle()
	for setting in ["default", "ascendant"]:
		check(await session.connect_engine(setting), "connect real REST setting " + setting)
		if failures: break
		check(await session.start_campaign(2026101600 + (1 if setting == "ascendant" else 0)), "create isolated real run " + setting)
		var start: Dictionary = session.command("START_ENCOUNTER")
		check(not start.is_empty() and await session.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})), "start real encounter " + setting)
		if failures: break
		router.open_game(); await settle()
		var count := 0
		for iteration in 16:
			if session.combat.get("status") != "ACTIVE" or router.current_screen != "combat": break
			var screen = router.host.get_child(0)
			var controller = screen.controller
			var deadline := Time.get_ticks_msec() + 8000
			while screen.controller.locked() and Time.get_ticks_msec() < deadline: await process_frame
			check(not screen.controller.locked(), "real input ready before action %s/%s" % [setting, iteration])
			if screen.controller.locked(): print("INPUT_DIAGNOSTIC=", JSON.stringify({"blockers": screen.controller.blocking_reasons(), "dispatchReady": cursor.dispatch_ready, "frames": cursor.total, "frameIndex": cursor.index}))
			if failures: break
			prefs.reduced_motion = iteration % 4 == 0
			var previous_sequence: int = session.run.sequence
			var cards: Array = session.legal_actions.filter(func(candidate): return candidate.get("source") == "Card")
			if not cards.is_empty():
				var id := str(cards[0].command.cardInstanceId)
				await screen._play_candidate(screen.presenter.candidate_for_card(id))
			else: await screen._execute_system(screen.view._system_candidate(["END_TURN"]))
			count += 1; deadline = Time.get_ticks_msec() + 8000
			while (session.busy or cursor.has_blocking_work() or controller.presentation_pending) and Time.get_ticks_msec() < deadline: await process_frame
			await settle()
			check(int(session.run.sequence) > previous_sequence and not session.busy and not session.has_pending_command and session.synchronized and not cursor.has_blocking_work() and not controller.state.submitting and not controller.presentation_pending, "real command %s/%s commits and settles every input gate" % [setting, iteration])
			if failures: break
		check(count >= 4, "multiple real cards/turns exercised for " + setting)
		var verified: Dictionary = await session.verify()
		check(verified.get("ok", false) and verified.data.get("isValid", false), "real run semantic replay remains valid for " + setting)
		router.back_to_menu(); await settle(); cursor.clear()
		if failures: break
	router.queue_free(); await settle()
	# Menu continuation discovery is a background read. Let its native/coalesced
	# callbacks unwind before exiting the test process (not a gameplay delay).
	var transport = root.get_node("GameServices")._transport
	var drain_deadline := Time.get_ticks_msec() + 5000
	while (not transport._get_flights.is_empty() or not transport._queue.is_empty() or transport._idle.size() != transport._workers.size()) and Time.get_ticks_msec() < drain_deadline: await process_frame
	await settle()
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
