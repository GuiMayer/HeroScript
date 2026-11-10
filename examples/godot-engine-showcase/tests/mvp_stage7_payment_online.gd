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
		var pile_buttons: Array = screen.view.pile_buttons().filter(func(button): return button.get_meta("zone_address", {}).get("zoneId") == "discard")
		check(pile_buttons.size() == 1, "discard pile is available through the combat interface")
		if pile_buttons.is_empty(): quit(1); return
		pile_buttons[0].pressed.emit()
		check(await wait_until(func(): return is_instance_valid(screen._collection_dialog) and screen._collection_dialog.visible), "open the real discard collection")
		if failures: quit(1); return
		var dialog = screen._collection_dialog
		check(dialog.collection.cards.has(fee) and dialog.collection.cards[fee].get_meta("card_selection_eligible", false), "published eligible card is highlighted in discard")
		if DisplayServer.get_name() != "headless":
			for argument in OS.get_cmdline_user_args():
				if argument.begins_with("--lab-output="):
					await RenderingServer.frame_post_draw
					check(dialog.get_texture().get_image().save_png(argument.trim_prefix("--lab-output=").path_join("discard-selection.png")) == OK, "capture discard selection with real API")
		dialog.collection.cards[fee].pressed.emit()
		check(await wait_until(func(): return screen.inline_selection.chosen.values().any(func(ids): return fee in ids)), "select the card through its collection button")
		check(int(session.run.sequence) == before, "incomplete payment selection does not commit")
		dialog.collection.cards[fee].pressed.emit()
		check(await wait_until(func(): return not screen.inline_selection.chosen.values().any(func(ids): return fee in ids)), "click again to remove the selected cost")
		var dialog_ref: WeakRef = weakref(dialog)
		dialog.confirmed.emit()
		check(await wait_until(func(): return dialog_ref.get_ref() == null), "close collection before choosing a target")
		screen._select_target("enemy_0")
		check(await wait_until(func(): return screen.inline_selection.command.get("targetIds", []) == ["enemy_0"] and not screen.inline_selection.requirements().is_empty()), "discard payment requirements are published")
		pile_buttons[0].pressed.emit()
		check(await wait_until(func(): return is_instance_valid(screen._collection_dialog) and screen._collection_dialog.visible and screen._collection_dialog.collection.cards.has(fee)), "reopen discard to choose the cost")
		if failures: quit(1); return
		var final_dialog = screen._collection_dialog
		var fee_card: Control = final_dialog.collection.cards[fee]
		for _frame in 8: await process_frame
		var point: Vector2 = fee_card.get_global_rect().get_center()
		var window_id: int = final_dialog.get_window_id()
		var motion := InputEventMouseMotion.new(); motion.position = point; motion.window_id = window_id; Input.parse_input_event(motion)
		await process_frame
		var press := InputEventMouseButton.new(); press.button_index = MOUSE_BUTTON_LEFT; press.position = point; press.window_id = window_id; press.pressed = true; Input.parse_input_event(press)
		await process_frame
		var release := InputEventMouseButton.new(); release.button_index = MOUSE_BUTTON_LEFT; release.position = point; release.window_id = window_id; release.pressed = false; Input.parse_input_event(release)
		check(await wait_until(func(): return int(session.run.sequence) == before + 1 and not session.busy and not session.has_pending_command), "discard payment commits once")
		var exile: Array = session.card_zones.get("zones", []).filter(func(zone): return str(zone.get("zoneId", "")) == "exhaust")
		check(not exile.is_empty() and exile[0].get("cards", []).any(func(card): return str(card.cardInstanceId) == fee), "discarded fee moves to exile")
	var replay: Dictionary = await session.verify()
	check(replay.get("ok", false) and replay.get("data", {}).get("isValid", false), "payment command replays deterministically")
	root.get_node("Playback").clear(); router.queue_free()
	for _frame in 6: await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.2).timeout
	quit(1 if failures else 0)
