extends SceneTree
## Dedicated disposable QA API/store only; never the player's port or user directory.
var failures := 0
func _init(): call_deferred("run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func wait_until(predicate: Callable, timeout := 12000) -> bool:
	var deadline := Time.get_ticks_msec() + timeout
	while not predicate.call() and Time.get_ticks_msec() < deadline: await process_frame
	return predicate.call()
func run() -> void:
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-"):
		check(false, "live payment test requires the isolated UI test directory"); quit(1); return
	var prefs = root.get_node("Preferences"); var previous: String = prefs.api_url
	prefs.api_url = "http://127.0.0.1:61989"; prefs.reduced_motion = true; prefs.auto_animations = true; prefs.changed.emit()
	var session = root.get_node("GameSession")
	check(await session.connect_engine("payment-lab"), "connect dedicated real payment setting")
	check(await session.start_campaign(2026110306), "create a run in the dedicated QA store")
	if failures: quit(1); return
	var start: Dictionary = session.command("START_ENCOUNTER")
	check(not start.is_empty() and await session.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})), "start the authored proof encounter")
	var router = load("res://main.tscn").instantiate(); root.add_child(router)
	await process_frame; router.open_game(); await process_frame
	var screen = router.host.get_child(0)
	check(await wait_until(func(): return is_instance_valid(screen) and screen.get("controller") != null and not screen.controller.locked()), "actual combat input becomes ready")
	if failures: quit(1); return
	var fee := ""
	for zone in session.card_zones.get("zones", []):
		for card in zone.get("cards", []):
			if card.definitionId == "core_strike": fee = str(card.cardInstanceId)
	for definition in ["payment_hand", "payment_spent"]:
		var card_id := ""
		for zone in session.card_zones.get("zones", []):
			for card in zone.get("cards", []):
				if card.definitionId == definition: card_id = str(card.cardInstanceId)
		check(not card_id.is_empty() and await wait_until(func(): return screen.presenter.requires_payment(card_id)), "real inspection advertises payment requirements for " + definition)
		if failures: print("PAYMENT_DIAGNOSTIC=", JSON.stringify({"id": card_id, "ready": session.card_evaluations_ready, "count": session.card_evaluations.size(), "presented": screen.presenter._evaluations.keys()}))
		if failures: break
		var before: int = session.run.sequence
		screen._choose_card(card_id); await process_frame
		var dialog = screen.get_children().filter(func(node): return node.scene_file_path == "res://scenes/ui/overlays/card_payment.tscn")[0]
		check(await wait_until(func(): return not dialog.snapshot.is_empty()), "same payment scene receives the REST preview")
		if failures: break
		dialog.command.targetIds = ["enemy_0"]; dialog._chosen["cost:offer"] = [fee]; dialog.refresh_preview()
		check(await wait_until(func(): return not dialog.get_ok_button().disabled), "real selected target and fee enable confirmation")
		check(session.run.sequence == before, "selection and preview do not commit gameplay")
		if failures: break
		dialog.confirmed.emit()
		check(await wait_until(func(): return int(session.run.sequence) == before + 1 and not session.busy and not session.has_pending_command), "Godot submits one complete integer-safe card command")
		check(await wait_until(func(): return is_instance_valid(router.host.get_child(0)) and not router.host.get_child(0).controller.locked()), "payment animations settle without blocking the next input")
		screen = router.host.get_child(0)
		if failures: break
	var exhausted: Array = session.card_zones.get("zones", []).filter(func(zone): return zone.zoneId == "exhaust")
	check(not exhausted.is_empty() and exhausted[0].cards.any(func(card): return str(card.cardInstanceId) == fee), "the same physical fee card moved from hand through discard to exile")
	root.get_node("Playback").clear(); router.queue_free()
	for _frame in 8: await process_frame
	prefs.api_url = previous; prefs.changed.emit()
	root.get_node("GameAudio").shutdown()
	# Allow the audio mixer to release its last Ogg decoder before the test host exits.
	await create_timer(0.2).timeout
	quit(1 if failures else 0)
