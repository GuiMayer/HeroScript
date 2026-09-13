extends SceneTree
## Real REST gameplay + passive projection and layout regression tests.
var failures := 0
var router
var prefs
var session
var translator
var original := {}

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1

func settle() -> void:
	for _frame in 8: await process_frame

func _run() -> void:
	prefs = root.get_node("Preferences")
	session = root.get_node("GameSession")
	translator = root.get_node("I18n")
	original = {"locale": translator.locale, "size": prefs.window_resolution, "fullscreen": prefs.fullscreen,
		"scale": prefs.text_scale, "run": prefs.last_run_id, "auto": prefs.auto_animations}
	_projection_tests()
	router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await create_timer(.3).timeout
	while session.busy: await create_timer(.025).timeout
	if not await session.connect_engine():
		check(false, "engine ready")
		_finish()
		return
	var seed := int(Time.get_unix_time_from_system()) % 90000000 + 490000000
	if not await session.start_campaign(seed):
		check(false, "start isolated test journey")
		_finish()
		return
	router.show_activity()
	await settle()
	var initial_activity = router.host.get_child(0)
	check(initial_activity.presenter.route().size() == session.run.map.nodes.size(), "all engine map nodes are represented")
	check(not is_instance_valid(initial_activity.find_child("AbandonRunButton", true, false)), "activity screen does not expose run abandonment")
	router.toggle_pause()
	await settle()
	var abandon_button: Button = router.pause_layer.find_child("AbandonRunButton", true, false)
	check(is_instance_valid(abandon_button), "pause menu owns the advertised abandon action")
	var run_before_pause := JSON.stringify(session.run)
	abandon_button.pressed.emit()
	await settle()
	var abandon_dialog: ConfirmationDialog
	for child in router.pause_layer.get_children():
		if child is ConfirmationDialog: abandon_dialog = child
	check(is_instance_valid(abandon_dialog) and abandon_dialog.visible and JSON.stringify(session.run) == run_before_pause, "abandon waits for explicit confirmation without changing the run")
	if is_instance_valid(abandon_dialog): abandon_dialog.hide()
	router.toggle_pause()
	await capture("journey")
	var start: Dictionary = session.command("START_ENCOUNTER")
	check(await session.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})), "enter real combat")
	prefs.fullscreen = false
	prefs.auto_animations = false
	var inspection_checked := false
	for resolution in [Vector2i(1280, 720), Vector2i(1440, 900), Vector2i(2560, 1080)]:
		prefs.set_resolution(resolution, false)
		for language in ["en", "pt_BR"]:
			translator.set_locale(language)
			for scale in [1.0, 1.2]:
				prefs.text_scale = scale
				router.show_combat()
				await settle()
				var screen = router.host.get_child(0)
				if not inspection_checked:
					for _attempt in 120:
						if screen.card_buttons.values().all(func(card): return not str(card.model.get("rarity", "")).is_empty()): break
						await create_timer(.025).timeout
					check(screen.card_buttons.values().all(func(card): return not str(card.model.get("rarity", "")).is_empty()),
						"REST inspection enriches every visible card with engine rarity")
					inspection_checked = true
				var label := "%s %s scale=%s" % [resolution, language, scale]
				check(screen.get_global_rect().end.y <= router.size.y + 1, "combat fits: " + label)
				var costs_visible := true
				var rules_fit := true
				for button in screen.action_buttons:
					if button.visible: check(button.get_global_rect().end.y <= router.size.y + 1 and button.get_global_rect().end.x <= router.size.x + 1, "turn control fits: " + label)
				for id in screen.card_buttons:
					var card = screen.card_buttons[id]
					var cost: Control = card.find_child("Cost", true, false)
					costs_visible = costs_visible and cost.size.y > 5 and cost.get_child_count() > 0
					rules_fit = rules_fit and card.find_child("Rules", true, false).get_global_rect().end.y <= card.find_child("Availability", true, false).get_global_rect().position.y
				check(costs_visible and rules_fit, "every card has visible cost and fitting rules: " + label)
				if scale == 1.0: await capture("combat-%s-%s" % [resolution.x, language])
	if "--layout-only" in OS.get_cmdline_user_args():
		_finish()
		return
	prefs.text_scale = 1.0
	prefs.set_resolution(Vector2i(1440, 900), false)
	translator.set_locale("en")
	var driver = load("res://tests/smoke.gd").new()
	var visited := {}
	for _step in 130:
		if str(session.run.get("lifecycle", "Active")) != "Active": break
		if not session.combat.is_empty() and str(session.combat.get("status", "")) == "ACTIVE":
			var candidate: Dictionary = driver.damaging_card_candidate()
			if candidate.is_empty():
				for item in session.legal_actions:
					if str(item.command.get("actionType", "")) == "END_TURN":
						candidate = item
						break
			if candidate.is_empty() or not await session.submit_candidate(candidate):
				check(false, "advance test combat")
				break
			continue
		var forced: Dictionary = preload("res://scripts/application/activity_choices.gd").only_forced_advance(session.activity_choices())
		var node_before_auto := str(session.run.get("currentNodeId", ""))
		router.show_activity()
		if not forced.is_empty():
			for _attempt in 400:
				await create_timer(.025).timeout
				if not session.busy and str(session.run.get("currentNodeId", "")) != node_before_auto: break
			check(str(session.run.get("currentNodeId", "")) != node_before_auto, "single advertised next stop advances automatically")
			continue
		await settle()
		var activity = router.host.get_child(0)
		var node_id := str(session.run.get("currentNodeId", ""))
		var state_key := node_id + ":" + str(session.activity_choices().size())
		if not visited.has(state_key):
			visited[state_key] = true
			check(activity.get_global_rect().end.x <= router.size.x + 1, "activity fits: " + state_key)
			await capture("activity-" + state_key.replace(":", "-"))
		var choice: Dictionary = driver.choose_progression_command()
		# Exercise shop price presentation and one purchase, unlike the older campaign smoke.
		if node_id == "merchant" and not visited.has("purchase"):
			for item in session.activity_choices():
				if str(item.type) == "BUY_SHOP_ITEM":
					var offer: Dictionary = activity.presenter.offer(item)
					check(not str(offer.cost).is_empty(), "shop shows the published price")
					var before: String = JSON.stringify(session.run)
					activity._confirm_choice(item, offer, activity._choice_label(item, offer))
					await settle()
					var modal: ConfirmationDialog
					for child in activity.get_children():
						if child is ConfirmationDialog: modal = child
					check(is_instance_valid(modal) and modal.visible and before == JSON.stringify(session.run), "purchase waits for explicit confirmation")
					if is_instance_valid(modal): modal.hide()
					choice = item
					visited["purchase"] = true
					break
		if choice.is_empty() or not await session.submit_activity(choice):
			check(false, "advance activity: " + node_id)
			break
	driver.free()
	check(str(session.run.get("lifecycle", "")) == "Completed", "complete playable journey after UI changes")
	var replay: Dictionary = await session.verify()
	if not replay.ok: print("REPLAY_REQUEST_ERROR ", replay.get("error", ""))
	check(replay.ok and replay.data.get("isValid", false), "real journey replay remains deterministic")
	var completed_run_id := str(session.run.get("runId", ""))
	var completed_run_bytes := JSON.stringify(session.run)
	router.open_game()
	await settle()
	var ending = router.host.get_child(0)
	var ending_replay: Button = ending.find_child("EndingReplayButton", true, false)
	check(is_instance_valid(ending_replay), "run result exposes real replay playback")
	ending_replay.pressed.emit()
	var replay_screen = router.host.get_child(0)
	for _attempt in 400:
		await create_timer(.025).timeout
		if not replay_screen.entries.is_empty() and replay_screen.selected_index >= 0: break
	check(router.current_screen == "run_replay" and replay_screen.entries.size() == int(session.run.sequence), "ending replay loads every persisted command")
	var replay_cursor_before: int = replay_screen.selected_index
	replay_screen._toggle_play()
	for _attempt in 200:
		await create_timer(.025).timeout
		if replay_screen.selected_index > replay_cursor_before: break
	replay_screen._stop()
	check(replay_screen.selected_index > replay_cursor_before and JSON.stringify(session.run) == completed_run_bytes, "replay advances its local cursor without mutating the completed run")
	router.back_to_menu()
	await settle()
	var history_button: Button = router.host.find_child("HistoryButton", true, false)
	check(is_instance_valid(history_button) and not history_button.disabled, "main menu exposes journey history")
	history_button.pressed.emit()
	var history_screen = router.host.get_child(0)
	for _attempt in 400:
		await create_timer(.025).timeout
		if not history_screen.entries.is_empty(): break
	var archived_index := -1
	for index in history_screen.entries.size():
		if str(history_screen.entries[index].get("runId", "")) == completed_run_id:
			archived_index = index
			break
	check(archived_index >= 0 and JSON.stringify(session.run) == completed_run_bytes, "history finds the completed run without activating it")
	if archived_index >= 0:
		history_screen._select(archived_index)
		history_screen._watch()
		var archived_replay = router.host.get_child(0)
		for _attempt in 400:
			await create_timer(.025).timeout
			if not archived_replay.entries.is_empty(): break
		check(router.current_screen == "run_replay" and archived_replay.replay_run_id == completed_run_id, "history opens replay for the selected run")
	check(await session.start_campaign(seed + 901), "start journey for pause abandonment")
	router.show_activity()
	await settle()
	router.toggle_pause()
	await settle()
	var exit_button: Button = router.pause_layer.find_child("AbandonRunButton", true, false)
	exit_button.pressed.emit()
	await settle()
	var exit_dialog: ConfirmationDialog
	for child in router.pause_layer.get_children():
		if child is ConfirmationDialog: exit_dialog = child
	if is_instance_valid(exit_dialog): exit_dialog.confirmed.emit()
	for _attempt in 400:
		await create_timer(.025).timeout
		if not session.busy: break
	check(not paused and str(session.run.get("lifecycle", "")) == "Abandoned", "confirmed pause action abandons the run through REST")
	_finish()

func _projection_tests() -> void:
	var catalog = preload("res://scripts/presentation/art_catalog.gd")
	for category in ["cards", "actors", "activities", "backgrounds"]:
		check(catalog.texture_for(catalog.resolve(category, "mod.unregistered")) != null, "unknown mod art has a fallback: " + category)
	var returned: Dictionary = catalog.manifest()
	returned.clear()
	check(not catalog.manifest().is_empty(), "art manifest is a defensive copy")
	var fixture := {"map": {"nodes": [
		{"nodeId": "start", "nextNodeIds": ["left", "right"]},
		{"nodeId": "left", "nextNodeIds": []}, {"nodeId": "right", "nextNodeIds": []}],
		"resolvedNodeIds": ["start"]}, "currentNodeId": "start",
		"shops": [{"shopInstanceId": "shop", "rerollCosts": [{"resourceId": "mana", "amount": 4}],
			"items": [{"itemId": "offer", "cardId": "defend", "costs": [{"resourceId": "mana", "amount": 2.5}]}]}]}
	var travel := {"type": "ADVANCE_NODE", "subjectId": "right", "payload": {"targetNodeId": "right"}}
	var presenter = preload("res://scripts/presentation/activity_presenter.gd").new(fixture, [travel], {}, translator)
	check(presenter.route()[0].next == ["left", "right"] and presenter.route()[1].travel.is_empty() and not presenter.route()[2].travel.is_empty(), "graph edges and legal navigation have separate authorities")
	var offer: Dictionary = presenter.offer({"type": "BUY_SHOP_ITEM", "subjectId": "offer", "payload": {"shopInstanceId": "shop"}})
	check(str(offer.cost).contains(translator.number(2.5)) and offer.definitionId == "defend", "shop joins actual item identity and generic resource cost")
	var reroll: Dictionary = presenter.offer({"type": "REROLL_SHOP", "payload": {"shopInstanceId": "shop"}})
	check(str(reroll.cost).contains("4") and str(reroll.cost).contains(translator.content_name("mana")), "reroll shows its published cost without recalculation")
	var candidate := {"command": {"cardInstanceId": "card"}, "costs": [{"componentId": "arbitrary-price"}], "applications": [
		{"targetEntityId": "hero", "resourceId": "mana", "previousValue": 3, "currentValue": 2, "provenance": {"sourceId": "card", "componentId": "arbitrary-price"}},
		{"targetEntityId": "enemy", "resourceId": "mana", "previousValue": 10, "currentValue": 7, "provenance": {"sourceId": "card", "componentId": "drain"}}]}
	var combat = preload("res://scripts/presentation/combat_presenter.gd").new({}, {}, [candidate], translator)
	check(not combat.card_summary("card").contains("-1") and combat.card_summary("card").contains("-3"), "costs are not repeated as effects; damage to any resource stays visible")
	var card_run := {"sequence": 7, "deck": {"cardInstances": [
		{"cardInstanceId": "card", "definitionId": "fireball", "upgrades": [{"upgradeId": "focused_flame"}]}
	]}}
	var card_candidate := {"command": {"cardInstanceId": "card"}, "costs": [
		{"resourceId": "energy", "amount": 1}, {"resourceId": "mana", "amount": 1}
	], "applications": [{"targetEntityId": "enemy", "resourceId": "health", "previousValue": 20, "currentValue": 8,
		"provenance": {"sourceId": "card", "componentId": "damage"}}]}
	var appearance := {"cards": {"fireball": {"name": "Fire Orb", "tone": "power"}}, "resources": {
		"energy": {"symbol": "E", "cost_color": "#e29b45"},
		"mana": {"symbol": "M", "cost_color": "#5d87dc"}}}
	var card_presenter = preload("res://scripts/presentation/combat_presenter.gd").new(card_run, {}, [card_candidate], translator, appearance)
	card_presenter.accept_evaluations([{"version": {"runSequence": 7},
		"evaluation": {"cardInstanceId": "card"}, "baseContainer": {"rarity": "Uncommon", "tags": ["attack"]},
		"appliedUpgrades": [{"upgradeId": "focused_flame"}], "calculations": [{"buckets": [{"contributions": [
			{"sourceKind": "Relic", "sourceId": "ember_core", "applied": true, "input": 10, "output": 12}
		]}]}]}])
	var card_model: Dictionary = card_presenter.card_view_model("card")
	check(card_model.costs.size() == 2 and card_model.costs[0].symbol == "E" and card_model.costs[1].symbol == "M",
		"card cost keeps every engine resource and applies only its presentation token")
	check(card_model.cardType == translator.text("ATTACK") and card_model.rarity == translator.text("UNCOMMON"),
		"card type and engine rarity have a dedicated presentation line")
	check(card_model.changeBadges.any(func(item): return item.kind == "upgrade") and
		card_model.changeBadges.any(func(item): return item.kind == "buff"),
		"permanent upgrades and contextual buffs remain distinct")
	var unavailable_presenter = preload("res://scripts/presentation/combat_presenter.gd").new(card_run, {}, [], translator, appearance)
	unavailable_presenter.accept_evaluations([{"version": {"runSequence": 7},
		"evaluation": {"cardInstanceId": "card", "costs": [{"resourceId": "mana", "amount": 3}]},
		"baseContainer": {"rarity": "Uncommon", "tags": ["attack"]}}])
	var unavailable_model: Dictionary = unavailable_presenter.card_view_model("card")
	check(unavailable_model.costs.size() == 1 and unavailable_model.costs[0].resourceId == "mana" and
		is_equal_approx(float(unavailable_model.costs[0].amount), 3.0),
		"an unplayable card still shows its engine-published cost separately from availability")

func capture(label: String) -> void:
	if "--capture-polish" not in OS.get_cmdline_user_args() or DisplayServer.get_name() == "headless": return
	await create_timer(.25).timeout
	await RenderingServer.frame_post_draw
	var path := ProjectSettings.globalize_path("res://tests/output/polish-" + label + ".png")
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	check(root.get_texture().get_image().save_png(path) == OK, "capture " + label)

func _finish() -> void:
	prefs.window_resolution = original.size
	prefs.fullscreen = original.fullscreen
	prefs.text_scale = original.scale
	prefs.last_run_id = original.run
	prefs.auto_animations = original.auto
	translator.set_locale(original.locale)
	prefs.save()
	if is_instance_valid(router): router.queue_free()
	root.get_node("GameAudio").shutdown()
	await create_timer(.2).timeout
	print("SHOWCASE_GAMEPLAY_POLISH failures=", failures)
	quit(0 if failures == 0 else 1)
