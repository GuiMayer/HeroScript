extends SceneTree
## End-to-end inline selection against the disposable local API, never the player store.
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
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-") or root.get_node("Preferences").api_url != "http://127.0.0.1:61989":
		check(false, "payment test requires isolated Godot data and API"); quit(1); return
	var prefs = root.get_node("Preferences")
	prefs.reduced_motion = true; prefs.auto_animations = true; prefs.changed.emit()
	var session = root.get_node("GameSession")
	check(await session.connect_engine("payment-lab"), "connect payment-lab")
	check(await session.start_campaign(int(Time.get_unix_time_from_system()) + 11200000), "create disposable run")
	if failures: quit(1); return
	var start: Dictionary = session.command("START_ENCOUNTER")
	check(await session.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})), "enter encounter")
	var router = load("res://main.tscn").instantiate(); root.add_child(router)
	await process_frame; router.open_game(); await process_frame
	var screen = router.host.get_child(0)
	check(await wait_until(func(): return is_instance_valid(screen) and screen.get("controller") != null and not screen.controller.locked()), "combat accepts input")
	if failures: quit(1); return
	var origin := ""; var fee := ""
	for zone in session.card_zones.get("zones", []):
		for card in zone.get("cards", []):
			if card.definitionId == "payment_hand": origin = str(card.cardInstanceId)
			if card.definitionId == "core_strike": fee = str(card.cardInstanceId)
	check(not origin.is_empty() and not fee.is_empty() and await wait_until(func(): return screen.presenter.requires_payment(origin)), "payment and fee cards are in hand")
	if failures: quit(1); return
	var before: int = int(session.run.sequence)
	screen._choose_card(origin)
	check(await wait_until(func(): return screen.inline_selection.active and not screen.inline_selection.snapshot.is_empty()), "selection opens inline preview")
	check(int(session.run.sequence) == before, "preview preserves run state")
	screen._select_target("enemy_0")
	check(await wait_until(func(): return screen.inline_selection.command.get("targetIds", []) == ["enemy_0"] and not screen.inline_selection.requirements().is_empty()), "target and payment requirements are available")
	screen._choose_card(fee)
	check(await wait_until(func(): return int(session.run.sequence) == before + 1 and not session.busy and not session.has_pending_command), "selecting the fee commits once")
	var discarded: Array = session.card_zones.get("zones", []).filter(func(zone): return str(zone.get("zoneId", "")) == "discard")
	check(not discarded.is_empty() and discarded[0].get("cards", []).any(func(card): return str(card.cardInstanceId) == fee), "selected fee moves to discard")
	var spent_origin := ""
	for zone in session.card_zones.get("zones", []):
		for card in zone.get("cards", []):
			if card.definitionId == "payment_spent": spent_origin = str(card.cardInstanceId)
	check(not spent_origin.is_empty() and await wait_until(func(): return not screen.controller.locked() and screen.presenter.requires_payment(spent_origin)), "second card exposes payment from discard")
	if not spent_origin.is_empty() and failures == 0:
		before = int(session.run.sequence)
		screen._choose_card(spent_origin)
		check(await wait_until(func(): return screen.inline_selection.active and not screen.inline_selection.snapshot.is_empty()), "second inline preview is available")
		screen._select_target("enemy_0")
		check(await wait_until(func(): return screen.inline_selection.command.get("targetIds", []) == ["enemy_0"] and not screen.inline_selection.requirements().is_empty()), "discard payment requirements are published")
		# Exercise the shared inline-selection controller with the exact card ID;
		# this does not claim that the collection UI exposes selection from discard.
		screen.inline_selection.pick(fee)
		check(await wait_until(func(): return int(session.run.sequence) == before + 1 and not session.busy and not session.has_pending_command), "discard payment commits once")
		var exile: Array = session.card_zones.get("zones", []).filter(func(zone): return str(zone.get("zoneId", "")) == "exhaust")
		check(not exile.is_empty() and exile[0].get("cards", []).any(func(card): return str(card.cardInstanceId) == fee), "discarded fee moves to exile")
	var replay: Dictionary = await session.verify()
	check(replay.get("ok", false) and replay.get("data", {}).get("isValid", false), "payment command replays deterministically")
	root.get_node("Playback").clear(); router.queue_free()
	for _frame in 6: await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.2).timeout
	quit(1 if failures else 0)
