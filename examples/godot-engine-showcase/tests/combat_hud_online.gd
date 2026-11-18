extends SceneTree
## Production-screen exercise against a disposable API and player directory.

var failures := 0

func _initialize() -> void:
	run.call_deferred()

func check(ok: bool, message: String) -> bool:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
	return ok

func run() -> void:
	var options := {}
	for argument in OS.get_cmdline_user_args():
		var parts := argument.trim_prefix("--").split("=", true, 1)
		if parts.size() == 2: options[parts[0]] = parts[1]
	var prefs = root.get_node("Preferences")
	var session = root.get_node("GameSession")
	if not check(OS.get_user_data_dir().get_file().begins_with("HeroScript-hud-qa-") and options.has("api-url"), "isolated user data and API configured"):
		quit(1)
		return
	prefs.reduced_motion = true
	prefs.animation_preset = "instant"
	var setting: String = options.get("campaign", "prototype-test")
	session.failed.connect(func(message): print("API: ", message))
	await exercise(session, setting, options)
	root.get_node("GameAudio").shutdown()
	await process_frame
	quit(1 if failures else 0)

func exercise(session, setting: String, options: Dictionary) -> void:
	if not check(await session.connect_engine(setting), "connect to " + setting): return
	if not check(await session.start_campaign(int(Time.get_unix_time_from_system())), "start " + setting): return
	var opening: Array[String] = ["START_DIALOGUE", "CHOOSE_DIALOGUE_OPTION", "RESOLVE_NODE", "ADVANCE_NODE"]
	if setting == "alisyum":
		opening.append_array(["CREATE_PREPARATION", "APPLY_PREPARATION_OPTION", "RESOLVE_NODE", "ADVANCE_NODE"])
	opening.append("START_ENCOUNTER")
	for type in opening:
		var choices: Array = session.activity_choices().filter(func(choice): return choice.type == type)
		if not check(not choices.is_empty() and await session.submit_activity(choices[0]), type): return

	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	router.show_combat()
	await settle()
	var screen = router.host.get_child(0)
	var view = screen.view
	check(view.scene_file_path.ends_with("combat_mvp_view.tscn") and session.run.settingId == setting, "campaign mounts the shared authored HUD")
	var stage = view.battlefield_view
	var enemy_id := ""
	for actor in session.combat.actors:
		var id: String = actor.instanceId
		var panel: ActorPanel = stage.panels[id]
		var health: Dictionary = actor.resources.get("health", {})
		if not health.is_empty():
			var hp: ResourceView = panel.resource_view("health")
			check(hp != null and is_equal_approx(hp.bar.value, float(health.current)), "%s matches published health" % id)
		if panel.presentation_model().hostile: enemy_id = id
	if not check(not enemy_id.is_empty(), "an opponent is present"):
		router.queue_free()
		return
	var panel: ActorPanel = stage.panels[enemy_id]
	var original: Dictionary = session.combat.duplicate(true)
	var fixture: Dictionary = original.duplicate(true)
	var enemy: Dictionary = fixture.actors.filter(func(actor): return actor.instanceId == enemy_id)[0]
	enemy.statuses = []
	var status_ids := ["alisyum_burn", "alisyum_razor_edge", "alisyum_frostbite", "alisyum_chilled", "alisyum_cinder_vuln", "alisyum_overheat"] if setting == "alisyum" else ["burn", "focus", "poison", "strength", "vulnerable", "wet"]
	var i18n = root.get_node("I18n")
	var prefs = root.get_node("Preferences")
	for mode in [{"scale": 1.0, "locale": "en", "contrast": false}, {"scale": 1.2, "locale": "pt_BR", "contrast": true}]:
		prefs.text_scale = mode.scale
		prefs.high_contrast = mode.contrast
		i18n.set_locale(mode.locale)
		prefs.changed.emit()
		router.show_combat()
		await settle()
		view.present_combat_snapshot(original)
		await settle()
		var art: Control = view.actor_portraits[enemy_id]
		var art_before := art.get_global_rect()
		var field: Rect2 = view.battlefield_actors.get_global_rect()
		check(art.is_visible_in_tree() and art_before.intersection(field).size.y >= minf(24, art_before.size.y), "actor art is visible in the battle area (%s, %s)" % [art_before, field])
		var target_before := panel.target_button.get_global_rect()
		var plate_before := panel.plate.get_global_rect()
		enemy.statuses = status_ids.map(func(id): return {"statusId": id, "stacks": 12, "duration": 2})
		view.present_combat_snapshot(fixture)
		await settle()
		var badges := panel.get_node("%ActorStatusBadges").get_children().filter(func(node): return node is StatusBadge and node.visible)
		var slot: Rect2 = panel.get_node("%ActorStatusSlot").get_global_rect()
		check(is_equal_approx(panel.visual_context.text_scale, mode.scale) and panel.visual_context.high_contrast == mode.contrast, "campaign applies HUD accessibility preferences")
		check(is_zero_approx(panel.get_theme_stylebox("panel").bg_color.a), "actor input surface never obscures the artwork behind it")
		var silhouettes := {}
		for badge in badges: silhouettes[badge.icon_rect.texture.resource_path] = true
		check(badges.size() == 5 and silhouettes.size() == 5 and badges.all(func(badge): return slot.encloses(badge.get_global_rect())), "five identifiable status icons fit in the plate at %s%%" % roundi(mode.scale * 100))
		if mode.scale > 1.0:
			check(badges[0].position.y < badges[4].position.y, "text enlargement keeps five icons in two rows")
		check(art_before.is_equal_approx(art.get_global_rect()) and target_before.is_equal_approx(panel.target_button.get_global_rect()) and plate_before.is_equal_approx(panel.plate.get_global_rect()), "portrait, target and plate stay still at %s%%" % roundi(mode.scale * 100))
		if options.has("capture-dir") and DisplayServer.get_name() != "headless":
			await RenderingServer.frame_post_draw
			var file := "%s-%s.png" % [setting, mode.locale]
			check(root.get_texture().get_image().save_png(str(options["capture-dir"]).path_join(file)) == OK, "capture " + file)
	check(session.combat == original, "HUD projections never change the real combat")
	var boss := enemy.duplicate(true)
	boss["instanceId"] = "hud-reference-boss"
	boss["definitionId"] = "boss_vidaria_archon" if setting == "alisyum" else "prototype_warden"
	fixture.actors.append(boss)
	view.present_combat_snapshot(fixture)
	await settle()
	var chief: ActorPanel = stage.panels[boss.instanceId]
	var boss_role: String = chief.get_node("%ActorRole").text
	check(stage.panels.has(enemy_id) and boss_role == i18n.text("Boss") and chief.resource_view("health") != null, "published boss identity and another enemy use the same HUD")
	view.present_combat_snapshot(original)
	router.queue_free()
	await settle()
	var legal: Array = session.legal_actions.filter(func(action):
		return str(action.get("source", "")) == "Card" and action.get("command", {}).get("costOptionId", "") in ["", null])
	if check(not legal.is_empty(), "a card is legal in the opening hand"):
		check(await session.submit_candidate(legal[0]), "submit a published card")
		var replay: Dictionary = await session.verify()
		check(replay.get("ok", false) and replay.get("data", {}).get("isValid", false), "replay remains deterministic")

func settle() -> void:
	for frame in 12:
		await process_frame
