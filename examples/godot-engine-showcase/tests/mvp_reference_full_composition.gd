extends SceneTree
## Stage 7 certification: full MVP composition across 1920x1080, 1280x720, 1280x800, and 2560x1080,
## text scaling, EN/PT-BR, long texts, 0/1/2/5/10/12 cards, multi-actor teams, card/motion baseline parity,
## legacy assembly removal, and non-MVP setting isolation.
const Context = preload("res://scripts/ui/visual_context.gd")
const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const ZonePresenter = preload("res://scripts/presentation/card_zone_presenter.gd")
const Choreography = preload("res://scripts/presentation/card_choreography.gd")
const Live = preload("res://tests/animation_live.gd")
var failures := 0

class Stage7Fixture extends RefCounted:
	var run_id := "stage7-run"
	var combat_id := "stage7-combat"
	var run_sequence := 1
	var combat_step := 1
	var content_revision := "rev"
	var combat_snapshot: Dictionary = {}
	var zones_snapshot: Dictionary = {}
	var actions_snapshot: Array = []
	func connect_engine() -> Dictionary:
		return {"ok": false, "status": 503, "error": "Offline Stage 7 fixture"}
	func _version() -> Dictionary:
		return {"runId": run_id, "combatId": combat_id, "runSequence": run_sequence, "combatStep": combat_step, "contentRevision": content_revision}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		var evals: Array = []
		for zone in zones_snapshot.get("zones", []):
			if not zone.get("allowsCardPlay", false): continue
			for card in zone.get("cards", []):
				var cid := str(card.get("cardInstanceId", ""))
				var def_id := str(card.get("definitionId", "basic_attack"))
				evals.append({
					"version": _version(),
					"cardInstanceId": cid,
					"definitionId": def_id,
					"isInPlayableZone": true,
					"baseContainer": {"name": def_id.capitalize(), "tags": ["fire", "attack"], "keywords": []},
					"evaluation": {
						"isPlayable": true,
						"costs": [{"resourceId": "energy", "amount": 1}],
						"affordableCostOptionIds": ["default"],
						"legalTargetIds": ["wisp", "sentinel"]
					},
					"presentationFacts": {"summary": "Deal 8 fire damage."}
				})
		return {"ok": true, "data": {"version": _version(), "cards": evals}}
	func read_card_presentations(_run_id: String) -> Dictionary:
		return {"ok": true, "data": {"cards": []}}
	func inspect_card(_combat_id: String, _actor_id: String, card_id: String) -> Dictionary:
		return {"ok": true, "data": {
			"version": _version(),
			"cardInstanceId": card_id,
			"definitionId": "basic_attack",
			"isInPlayableZone": true,
			"baseContainer": {"name": "Fire Strike", "tags": ["fire", "attack"], "keywords": []},
			"evaluation": {"isPlayable": true, "costs": [{"resourceId": "energy", "amount": 1}]},
			"presentationFacts": {"summary": "Deal 8 fire damage."}
		}}

class ChoreographyFixture extends Live.Fixture:
	var operation := "use"
	func send_prepared(command: Dictionary) -> Dictionary:
		var result: Dictionary = await super.send_prepared(command)
		if operation != "retained":
			zones.zones[1].cards = [{"cardInstanceId": "played", "definitionId": "basic_attack"}]
			zones.zones[1].count = 1
			result.data.state.resolution.frames.append({
				"transitionType": "effect",
				"cardZoneSteps": [{
					"flowId": "card.played.to-exhaust" if operation == "burn_center" else "card.played.to-discard",
					"sourceAddress": "$run::::custom",
					"targetAddress": "$run::::spent",
					"instanceIds": ["played"]
				}]
			})
		return result

func _init() -> void:
	run.call_deferred()

func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok:
		failures += 1

func settle(frames := 14) -> void:
	for _frame in frames:
		await process_frame

func _make_actor(id: String, def_id: String, side: String, hp: int, max_hp: int, block_val: int, statuses: Array = []) -> Dictionary:
	var res := {"health": {"current": hp, "minimum": 0, "maximum": max_hp}}
	if block_val > 0:
		res["block"] = {"current": block_val, "maximum": max_hp}
	if id == "hero":
		res["energy"] = {"current": 3, "minimum": 0, "maximum": 3}
	return {"instanceId": id, "definitionId": def_id, "sideId": side, "resources": res, "statuses": statuses}

func _present(view: Control, snapshot: Dictionary, context, interaction_override := {}) -> void:
	var combat_presenter = Presenter.new(snapshot.run, snapshot.combat, snapshot.get("actions", []), context.translator, snapshot.presentation, snapshot.zones)
	combat_presenter.accept_presentations(snapshot.get("presentations", []))
	combat_presenter.accept_evaluations(snapshot.get("evaluations", []))
	var zone_presenter = ZonePresenter.new(snapshot.zones, context.translator)
	view.apply_projection(snapshot.presentation, combat_presenter, zone_presenter, {
		"run": snapshot.run,
		"combat": snapshot.combat,
		"actions": snapshot.get("actions", []),
		"zones": snapshot.zones,
		"input_actor": "hero",
		"scope": "stage7-fixture",
		"presentation": snapshot.presentation,
		"setting": snapshot.get("setting", {}),
		"setting_id": "prototype-test",
		"attributes": snapshot.get("attributes", {}),
		"scenery": snapshot.get("scenery", {})
	}, context)
	var state := {"synchronized": true, "locked": false}
	state.merge(interaction_override, true)
	view.apply_interaction(state)

func run() -> void:
	var i18n = root.get_node("I18n")
	var prefs = root.get_node("Preferences")
	var session = root.get_node("GameSession")
	var playback = root.get_node("Playback")
	var prior := {
		"locale": i18n.locale,
		"scale": prefs.text_scale,
		"motion": prefs.reduced_motion,
		"speed": prefs.animation_speed,
		"auto_anim": prefs.auto_animations,
		"run": session._run.duplicate(true),
		"combat": session._combat.duplicate(true),
		"zones": session._card_zones.duplicate(true),
		"actions": session._legal_actions.duplicate(true),
		"gateway": session._gateway,
		"attributes": session._character_attributes.duplicate(true),
		"revision": session.content_revision
	}
	var profile = load("res://data/themes/mvp_combat_theme.tres")
	var profile_before: String = profile.presentation_key()

	# 1. Verify legacy MVP assembly removal and authoritative ReferenceCanvas tree.
	check(not ResourceLoader.exists("res://scenes/ui/components/mvp_arena.tscn"), "unused legacy mvp_arena.tscn assembly is removed")
	var view = load("res://scenes/ui/components/combat_mvp_view.tscn").instantiate()
	view.size = Vector2(1920, 1080)
	root.add_child(view)
	view.get_node(view.battlefield_path).embedded_artwork = true
	view.show_editor_preview()
	await settle()
	view.apply_interaction({"synchronized": true})

	check(not view.has_node("CombatBody") and not view.has_node("Balance") and not view.has_node("InscriptionStrip"), "combat_mvp_view has no legacy CombatBody, Balance, or InscriptionStrip nodes")
	var canvas: Control = view.get_node("CanvasHost/ReferenceCanvas")
	check(not canvas.has_node("TemporaryControls"), "ReferenceCanvas has no temporary stage placeholders")
	check(canvas.has_node("CombatHeader") and canvas.has_node("Battlefield") and canvas.has_node("PileRail") and canvas.has_node("EnergyIndicator") and canvas.has_node("HandHost") and canvas.has_node("EndTurnAndContextActions"), "ReferenceCanvas mounts all six primary regions directly")
	check(not view.header_view.has_node("TimelineButton"), "MVP header does not contain legacy TimelineButton")

	# 2. Verify card style, dimensions, 2-card fan destination center, and motion policy durations against Stage 1 baseline.
	var ctx_base = view.visual_context
	var expected_card_size: Vector2 = ctx_base.card_style.effective_size(1.0)
	check(expected_card_size.is_equal_approx(Vector2(212, 300)), "baseline card size remains 212x300 at text_scale 1.0")
	var expected_style_key: String = ctx_base.card_style.presentation_key()
	var duration_keys := ["hover", "draw", "card_flight", "discard", "exile", "hand_transition", "staged_discard", "retain"]
	var baseline_motion = preload("res://scripts/presentation/motion_policy.gd").capture("prototype-test", {"animation_speed": 1.0, "reduced_motion": false})
	var sampled_motion = ctx_base.capture_motion()
	for d_key in duration_keys:
		check(is_equal_approx(sampled_motion.duration(d_key, true), baseline_motion.duration(d_key, true)), "motion duration matches baseline for " + d_key)
	# Stage 1's committed motion manifest specifies 0.5s for each phase.
	check(is_equal_approx(baseline_motion.duration("card_flight", true) + baseline_motion.duration("staged_discard", true), 1.0), "default use cycle duration remains 1.0s")
	check(is_equal_approx(baseline_motion.duration("card_flight", true) + baseline_motion.duration("retain", true), 1.0), "default retained cycle duration remains 1.0s")

	# 3. Full matrix across resolutions (1920x1080, 1280x720, 1280x800, 2560x1080), locales (en, pt_BR),
	#    text scales (1.0, 1.25), card counts (0, 1, 2, 5, 10, 12), actor teams (1v1, 2v2, 1v4), and long texts.
	var base_snapshot: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/ui_editor_preview.json"))
	var source_models: Array = view.hand.ordered_cards().map(func(c): return c.model.duplicate(true))

	var team_configs := [
		{
			"name": "1v2_standard",
			"actors": [
				_make_actor("hero", "spire_adept", "player", 54, 72, 12, [{"statusId": "ember_forge", "stacks": 1}]),
				_make_actor("wisp", "spire_wisp", "enemy", 28, 36, 0, [{"statusId": "burn", "stacks": 4}]),
				_make_actor("sentinel", "spire_sentinel", "enemy", 46, 60, 10, [{"statusId": "armor", "stacks": 25}])
			],
			"intents": [{"actorId": "wisp", "actionId": "Attack"}, {"actorId": "sentinel", "actionId": "Poison Spit"}]
		},
		{
			"name": "2v4_long_names",
			"actors": [
				_make_actor("hero", "Arch-Pyromancer of the Eternal Cinder Conclave", "player", 68, 72, 24, [{"statusId": "burn", "stacks": 3}, {"statusId": "strength", "stacks": 5}]),
				_make_actor("ally_1", "Crystalline Water Elemental Construct Companion", "player", 40, 40, 8, [{"statusId": "ward", "stacks": 2}]),
				_make_actor("enemy_0", "Automaton Scout Unit Alpha-Nine", "enemy", 22, 32, 0, [{"statusId": "burn", "stacks": 6}]),
				_make_actor("enemy_1", "Thermal Bastion Heavy Shield Bearer", "enemy", 58, 75, 18, [{"statusId": "armor", "stacks": 35}]),
				_make_actor("enemy_2", "Incandescent Cinder Drone Mark IV", "enemy", 19, 28, 0, [{"statusId": "burn", "stacks": 8}]),
				_make_actor("enemy_3", "Obsidian Furnace Warden Phase Two", "enemy", 112, 160, 30, [{"statusId": "burn", "stacks": 12}, {"statusId": "armor", "stacks": 50}])
			],
			"intents": [
				{"actorId": "enemy_0", "actionId": "Overcharged Thermal Lance Barrage"},
				{"actorId": "enemy_1", "actionId": "Fortified Bulwark Slam"},
				{"actorId": "enemy_2", "actionId": "Volatile Ash Detonation"},
				{"actorId": "enemy_3", "actionId": "Cataclysmic Furnace Eruption"}
			]
		}
	]

	for locale in ["en", "pt_BR"]:
		i18n.set_locale(locale)
		for scale in [1.0, 1.25]:
			var ctx = Context.new(i18n, {"text_scale": scale, "reduced_motion": true, "action_labels": {"end_turn": "E", "confirm_action": "F", "pause_game": "Escape", "inspect_card": "V"}}, profile)
			var scaled_card_size: Vector2 = ctx.card_style.effective_size(scale)
			for team_cfg in team_configs:
				var snap: Dictionary = base_snapshot.duplicate(true)
				snap.combat.actors = team_cfg.actors.duplicate(true)
				snap.combat.activation.intents = team_cfg.intents.duplicate(true)
				snap["actions"] = [{"source": "System", "command": {"actionType": "END_TURN"}}]
				if team_cfg.name == "2v4_long_names":
					snap.run["supportCombat"] = {"supportId": "Autonomous Cooling Conduit Array", "routineId": "Emergency Thermal Dispersion Protocol", "remainingOwnerActivations": 2}
				for resolution in [Vector2i(1920, 1080), Vector2i(1280, 720), Vector2i(1280, 800), Vector2i(2560, 1080)]:
					root.size = resolution
					view.size = Vector2(resolution)
					_present(view, snap, ctx)
					await settle(10)
					var tag := "%s scale=%s team=%s res=%s" % [locale, scale, team_cfg.name, resolution]
					var canvas_rect: Rect2 = canvas.get_global_rect()
					var header_rect: Rect2 = view.header_view.get_global_rect()
					var piles_rect: Rect2 = view.pile_controls.get_global_rect()
					var actors_rect: Rect2 = view.battlefield_actors.get_global_rect()
					var energy_rect: Rect2 = view.energy_indicator.get_global_rect()
					var hand_rect: Rect2 = view.bottom_hud.get_global_rect()
					var turn_rect: Rect2 = view.turn_view.get_global_rect()
					var end_rect: Rect2 = view.turn_view.get_node("EndTurnButton").get_global_rect()

					check(is_equal_approx(canvas_rect.size.x / canvas_rect.size.y, 16.0 / 9.0) and view.get_global_rect().encloses(canvas_rect), "16:9 canvas fits within viewport: " + tag)
					check(absf(canvas_rect.get_center().x - view.get_global_rect().get_center().x) <= 1.0 and absf(canvas_rect.get_center().y - view.get_global_rect().get_center().y) <= 1.0, "canvas stays centered: " + tag)
					check(canvas_rect.encloses(header_rect) and header_rect.end.y <= piles_rect.position.y + 1.0 and header_rect.end.y <= actors_rect.position.y + 1.0, "header stays at top above piles and battlefield: " + tag)
					check(piles_rect.end.x <= actors_rect.position.x + 1.0 and piles_rect.end.x <= hand_rect.position.x + 1.0, "pile rail stays on the left of battlefield and hand: " + tag)
					check(turn_rect.position.x >= hand_rect.end.x - 1.0 and canvas_rect.encloses(end_rect), "EndTurn stays isolated on bottom-right: " + tag)
					check(absf(energy_rect.get_center().x - canvas_rect.get_center().x) <= 2.0, "EnergyIndicator stays horizontally centered: " + tag)

					# Check card counts 0, 1, 2, 5, 10, 12 at 1920x1080 and 1280x720 for standard team.
					if team_cfg.name == "1v2_standard" and resolution in [Vector2i(1920, 1080), Vector2i(1280, 720)]:
						for count in [0, 1, 2, 5, 10, 12]:
							var models: Array = []
							for idx in count:
								var m: Dictionary = source_models[idx % source_models.size()].duplicate(true)
								m.cardInstanceId = "s7-%s-%s" % [count, idx]
								models.append(m)
							view.hand.sync_models(models, "s7-%s-%s-%s-%s" % [locale, scale, resolution.x, count], ctx.translator.text("Your hand is empty."))
							view.card_buttons = view.hand.cards()
							await settle(8)
							var c_tag := "%s cards=%s" % [tag, count]
							check(view.hand.order.size() == count, "hand mounts exact card count: " + c_tag)
							check(view.hand.ordered_cards().all(func(card): return (card.size * 0.67).is_equal_approx(view.hand._reserved_card_size) and card.size.x >= scaled_card_size.x and card.size.y >= scaled_card_size.y), "cards share reserved dimensions at least as large as effective_size: " + c_tag)
							check(absf(view.hand.get_global_rect().end.y - view.get_node("CanvasHost").get_global_rect().end.y) <= 1.0, "hand reaches the screen bottom: " + c_tag)
							var overflow := bool(view.hand._layout.get("overflow", false))
							check(view.hand.toolbar.visible == overflow, "HandTools visibility matches hand overflow state: " + c_tag)
							if count <= 2:
								check(not overflow and not view.hand.toolbar.visible, "0/1/2 cards keep HandTools hidden: " + c_tag)
							if count == 2:
								var first_c: Vector2 = view.hand.destination_center(view.hand.order.front())
								var last_c: Vector2 = view.hand.destination_center(view.hand.order.back())
								check(absf((first_c.x + last_c.x) * 0.5 - canvas_rect.get_center().x) <= 1.5, "two-card fan stays centered on ReferenceCanvas: " + c_tag)

	view.queue_free()
	await settle(6)

	# 4. Live card choreography timing parity on combat_mvp.tscn (use, retained, burn_center).
	i18n.set_locale("en")
	prefs.text_scale = 1.0
	prefs.animation_speed = 1.0
	prefs.reduced_motion = false
	prefs.auto_animations = true
	for operation in ["use", "retained", "burn_center"]:
		var fixture := ChoreographyFixture.new()
		fixture.operation = operation
		fixture.retained = operation == "retained"
		fixture.state["settingId"] = "prototype-test"
		fixture.zones.zones.append({
			"zoneId": "spent",
			"contentsVisible": true,
			"count": 0,
			"cards": [],
			"presentation": {"slot": "exile_pile" if operation == "burn_center" else "discard_pile", "labelKey": "Exile" if operation == "burn_center" else "Discard pile"}
		})
		session.configure(fixture)
		session._run = fixture.state.duplicate(true)
		session._combat = fixture.combat.duplicate(true)
		session._card_zones = fixture.zones.duplicate(true)
		session._legal_actions = fixture.read_legal_actions("", "").data.candidates
		session.synchronized = true
		session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
		root.size = Vector2i(1920, 1080)
		var router = load("res://main.tscn").instantiate()
		root.add_child(router)
		router.show_combat()
		await settle(10)
		var screen = router.host.get_child(0)
		check(screen.scene_file_path.ends_with("combat_mvp.tscn"), operation + ": choreography mounts combat_mvp.tscn")
		check(screen._card_stage().is_equal_approx(Choreography.stage_center(screen.view.hand)), operation + ": MVP combat uses canonical stage_center formula")
		var expected_sec := 0.5 if operation == "burn_center" else 1.0
		var started := Time.get_ticks_usec()
		await screen._play_candidate(screen.presenter.candidate_for_card("played"))
		while playback.has_blocking_work() and Time.get_ticks_usec() - started < 4000000:
			await process_frame
		var elapsed_sec := (Time.get_ticks_usec() - started) / 1000000.0
		await settle(6)
		check(absf(elapsed_sec - expected_sec) < 0.25 and not playback.has_blocking_work() and not screen.controller.locked(), "%s: MVP card animation settles within expected duration (%.3fs vs %.3fs)" % [operation, elapsed_sec, expected_sec])
		router.queue_free()
		await settle(6)
		playback.clear()

	# 5. Verify non-MVP setting ("default") still mounts combat_screen.tscn with its own composition intact.
	prefs.reduced_motion = true
	var non_mvp_fixture := Stage7Fixture.new()
	session.configure(non_mvp_fixture)
	session.synchronized = true
	session.content_revision = "rev"
	session._run = {"settingId": "default", "runId": "stage7-run", "sequence": 1, "playerEntityId": "hero", "contentRevision": "rev"}
	session._combat = {
		"combatId": "stage7-combat", "step": 1, "activeActorId": "hero",
		"activation": {"round": 1, "activeActorId": "hero", "waitingForInput": true, "intents": [{"actorId": "wisp", "actionId": "Attack"}]},
		"relationships": {"sameSide": "Ally", "differentSides": "Enemy"},
		"actors": [_make_actor("hero", "spire_adept", "player", 60, 72, 8), _make_actor("wisp", "spire_wisp", "enemy", 30, 36, 0)]
	}
	session._card_zones = {"zones": [
		{"zoneId": "hand", "allowsCardPlay": true, "contentsVisible": true, "cards": [{"cardInstanceId": "c1", "definitionId": "basic_attack"}], "count": 1},
		{"zoneId": "draw", "ownerId": "hero", "scopeId": "stage7-combat", "count": 5, "presentation": {"slot": "draw_pile", "labelKey": "Draw pile"}}
	]}
	session._legal_actions = [{"source": "System", "command": {"actionType": "END_TURN"}}, {"source": "Card", "command": {"cardInstanceId": "c1", "actionType": "PLAY_CARD", "targetIds": ["wisp"]}, "costs": [{"resourceId": "energy", "amount": 1}]}]
	non_mvp_fixture.combat_snapshot = session._combat.duplicate(true)
	non_mvp_fixture.zones_snapshot = session._card_zones.duplicate(true)
	non_mvp_fixture.actions_snapshot = session._legal_actions.duplicate(true)

	var router_def = load("res://main.tscn").instantiate()
	root.add_child(router_def)
	router_def.show_combat()
	await settle(10)
	var def_screen = router_def.host.get_child(0)
	check(def_screen.scene_file_path.ends_with("combat_screen.tscn") and def_screen.view.has_node("CombatBody/CombatGameplay"), "non-MVP setting 'default' continues mounting combat_screen.tscn with CombatBody/CombatGameplay intact")
	router_def.queue_free()
	await settle(6)

	# Restore global state and verify theme & card_style immutability.
	prefs.text_scale = prior.scale
	prefs.reduced_motion = prior.motion
	prefs.animation_speed = prior.speed
	prefs.auto_animations = prior.auto_anim
	i18n.set_locale(prior.locale)
	session._gateway = prior.gateway
	session._run = prior.run
	session._combat = prior.combat
	session._card_zones = prior.zones
	session._legal_actions = prior.actions
	session._character_attributes = prior.attributes
	session.content_revision = prior.revision
	playback.clear()
	check(profile.presentation_key() == profile_before, "Stage 7 never mutates mvp_combat_theme.tres")
	check(ctx_base.card_style.presentation_key() == expected_style_key, "Stage 7 never mutates mvp_card_style.tres")

	root.get_node("GameAudio").shutdown()
	await create_timer(0.05).timeout
	quit(1 if failures else 0)
