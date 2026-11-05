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
	check(await session.start_campaign(2026110491), "start real seeded campaign")
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
	var shield_used := false
	var first_intent := ""
	for turn in 4:
		var screen = router.host.get_child(0)
		var intents: Array = session.combat.get("activation", {}).get("intents", [])
		var enemy_intents := intents.filter(func(intent): return intent.get("actorId") == "enemy_0")
		if not enemy_intents.is_empty():
			var action := str(enemy_intents[0].get("actionId", ""))
			if first_intent.is_empty(): first_intent = action
			elif turn == 1: check(action != first_intent and action in ["thin_slice_probe", "thin_slice_overload"], "enemy alternates published attack intentions")
		var hand: Array = session.card_zones.get("zones", []).filter(func(zone): return zone.get("zoneId") == "hand")
		if not hand.is_empty():
			for card in hand[0].get("cards", []):
				if card.get("definitionId") != "pyro_barreira_cinzas": continue
				var id := str(card.cardInstanceId)
				check(await wait_until(func(): return not screen.presenter.candidate_for_card(id).is_empty()), "shield card is available for input")
				check(await wait_until(func(): return not screen.presenter.card_view_model(id).get("presentationFacts", {}).is_empty()), "card inspection receives engine facts")
				var model: Dictionary = screen.presenter.card_view_model(id)
				check(JSON.stringify(model.get("presentationFacts", {})).contains("pyro_barreira_cinzas"), "shield hover is built from the authored card")
				check(screen.view.card_buttons.has(id), "authored shield is mounted in the hand")
				if screen.view.card_buttons.has(id):
					var tooltip = screen.view.card_buttons[id].create_tooltip_panel()
					check(tooltip != null, "production card creates its hover detail panel")
					if tooltip != null: tooltip.free()
				await screen._play_candidate(screen.presenter.candidate_for_card(id))
				check(await wait_until(func(): return session.synchronized and not screen.controller.locked()), "shield command settles")
				check(current_block(session) >= 8, "shield card adds eight block")
				shield_used = true
				break
		var before := current_block(session)
		check(await session.execute_combat_command("END_TURN", {"actorId": "player"}), "end turn through API")
		check(await wait_until(func(): return session.synchronized and not screen.controller.locked()), "next activation settles")
		if shield_used:
			check(current_block(session) > 0 and current_block(session) < before, "remaining block persists after enemy attack and next turn begins")
			var next_intents: Array = session.combat.get("activation", {}).get("intents", [])
			var next_enemy := next_intents.filter(func(intent): return intent.get("actorId") == "enemy_0")
			check(not next_enemy.is_empty() and str(next_enemy[0].get("actionId", "")) != first_intent, "enemy announces the next attack in its configured cycle")
			break
	check(shield_used, "draw the authored shield during the scenario")
	var replay: Dictionary = await session.verify()
	check(replay.get("ok", false) and replay.get("data", {}).get("isValid", false), "semantic replay validates the same actions")
	root.get_node("Playback").clear()
	router.queue_free()
	for _frame in 8: await process_frame
	root.get_node("GameAudio").shutdown()
	await create_timer(0.2).timeout
	quit(1 if failures else 0)
