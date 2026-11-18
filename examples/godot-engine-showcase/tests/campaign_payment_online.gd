extends SceneTree
## Exercises the campaign's actual Void and Necro choices against a disposable API.

var failures := 0

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, label: String) -> bool:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
	return ok

func wait_until(predicate: Callable) -> bool:
	var deadline := Time.get_ticks_msec() + 9000
	while not predicate.call() and Time.get_ticks_msec() < deadline: await process_frame
	return predicate.call()

func _submit(session, type: String) -> bool:
	var choices: Array = session.activity_choices().filter(func(choice): return choice.type == type)
	return check(not choices.is_empty() and await session.submit_activity(choices[0]), type)

func _card(session, family: String) -> String:
	for zone in session.card_zones.get("zones", []):
		if zone.get("zoneId") != "hand": continue
		for value in zone.get("cards", []):
			if str(value.get("definitionId", "")).begins_with("prototype_" + family + "_"):
				return str(value.get("cardInstanceId", ""))
	return ""

func _pay(session, router, family: String, source: String) -> bool:
	for cycle in 7:
		var screen = router.host.get_child(0)
		if not await wait_until(func(): return not screen.controller.locked()): break
		var id := _card(session, family)
		if not id.is_empty() and await wait_until(func(): return screen.presenter.requires_payment(id)):
			var sequence: int = int(session.run.sequence)
			screen._choose_card(id)
			var inline = screen.inline_selection
			if await wait_until(func(): return not inline.snapshot.is_empty()):
				var targets: Array = inline.snapshot.get("evaluation", {}).get("legalTargetIds", [])
				if not targets.is_empty(): screen._select_target("enemy_0" if "enemy_0" in targets else str(targets[0]))
				var requirements: Array = inline.requirements().filter(func(item): return item.sourceZoneId == source and not item.eligibleCardIds.is_empty())
				if not requirements.is_empty():
					var fee := str(requirements[0].eligibleCardIds[0])
					if source == "discard":
						var pile = screen.view.pile_buttons().filter(func(button): return button.get_meta("zone_address", {}).get("zoneId") == source)[0]
						pile.pressed.emit()
						if await wait_until(func(): return is_instance_valid(screen._collection_dialog) and screen._collection_dialog.collection.cards.has(fee)):
							var dialog = screen._collection_dialog
							dialog.collection.cards[fee].pressed.emit()
							dialog.confirmed.emit()
					else:
						inline.pick(fee)
					if await wait_until(func(): return not inline.confirmable_preview().is_empty()):
						if not check(int(session.run.sequence) == sequence, family + " preview does not commit"): return false
						var play_label: String = root.get_node("I18n").text("Play card")
						var button: Button = screen.view.choices.get_children().filter(func(child): return child is Button and child.text == play_label)[0]
						button.pressed.emit()
						return check(await wait_until(func(): return int(session.run.sequence) == sequence + 1 and not session.busy),
							family + " confirms exactly once using " + source)
				print("Payment attempt ", family, ": ", inline.snapshot.get("evaluation", {}).get("failureReasons", []))
			screen._cancel_selection()
		if cycle < 6:
			screen._end_turn()
			if not await wait_until(func(): return not screen.controller.locked()): break
			# Play no-effect-to-target candidates to free hand space so later draws can enter.
			for _index in 6:
				if _card(session, family) != "": break
				var options: Array = session.legal_actions.filter(func(option):
					var id_to_play := str(option.get("command", {}).get("cardInstanceId", ""))
					return not id_to_play.is_empty() and not screen.presenter.requires_payment(id_to_play))
				if options.is_empty() or not await session.submit_candidate(options[0]): break
				if not await wait_until(func(): return not screen.controller.locked()): break
	return check(false, "find and confirm " + family + " from " + source)

func _exercise(session, family: String, source: String) -> void:
	if not check(await session.start_campaign(4202), "start a seeded tactical campaign"): return
	for type in ["START_DIALOGUE", "CHOOSE_DIALOGUE_OPTION", "RESOLVE_NODE", "ADVANCE_NODE"]:
		if not await _submit(session, type): return
	if family == "necro":
		var deck: Dictionary = await session.read_deck_loadout()
		var edit := {"actorId": session.run.playerEntityId, "groupId": "module", "moduleId": "prototype_necro"}
		if not check(deck.get("ok", false) and await session.apply_deck_loadout(deck.data, edit), "equip the Necro module between encounters"): return
	var start: Dictionary = session.command("START_ENCOUNTER")
	if not check(not start.is_empty() and await session.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})),
			"start the scout encounter"): return
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await process_frame
	router.open_game()
	await process_frame
	if source == "discard":
		var screen = router.host.get_child(0)
		await wait_until(func(): return not screen.controller.locked())
		var discard: Array = session.card_zones.get("zones", []).filter(func(zone): return zone.zoneId == "discard")
		if not discard.is_empty() and int(discard[0].count) == 0:
			var fees: Array = session.legal_actions.filter(func(option):
				var card: Dictionary = screen.presenter.card_instance(str(option.get("command", {}).get("cardInstanceId", "")))
				return str(card.get("definitionId", "")) in ["prototype_strike", "prototype_guard", "prototype_scorch", "prototype_soak"])
			if not fees.is_empty():
				await session.submit_candidate(fees[0])
	await _pay(session, router, family, source)
	root.get_node("Playback").clear()
	router.queue_free()
	for _frame in 6: await process_frame

func _run() -> void:
	var prefs = root.get_node("Preferences")
	if check(OS.get_user_data_dir().contains("HeroScript-ui-refactor-") and prefs.api_url != "http://127.0.0.1:5271",
			"payment journey uses an isolated UI profile and API"):
		prefs.animation_preset = "instant"
		prefs.auto_animations = true
		prefs.changed.emit()
		var session = root.get_node("GameSession")
		session.failed.connect(func(error): print("ENGINE: ", error.get("error", error)))
		if check(await session.connect_engine("prototype-test") and session.selected_setting_id == "prototype-test",
				"connect to the tactical campaign"):
			await _exercise(session, "void", "hand")
			await _exercise(session, "necro", "discard")
	root.get_node("GameAudio").shutdown()
	await create_timer(0.1).timeout
	quit(1 if failures else 0)
