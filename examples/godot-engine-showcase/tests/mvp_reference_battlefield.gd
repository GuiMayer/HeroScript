extends SceneTree
## Stage 4 verification: battlefield composition, separated artwork above compact info plates, variable actor counts, targeting, resource/status updates, and honest published data.
const Context = preload("res://scripts/ui/visual_context.gd")
const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const Zones = preload("res://scripts/presentation/card_zone_presenter.gd")
var failures := 0

class BattlefieldFixture extends RefCounted:
	func connect_engine() -> Dictionary:
		return {"ok": false, "status": 503, "error": "Offline battlefield fixture"}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		return {"ok": true, "data": {"cards": []}}

func _init() -> void: run.call_deferred()
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 12: await process_frame

func _make_actor(id: String, definition: String, side: String, name_override := "", health_curr := 48, health_max := 72, block_curr := 8, statuses: Array = [], include_energy := true) -> Dictionary:
	var resources := {"health": {"current": health_curr, "minimum": 0, "maximum": health_max}}
	if block_curr > 0:
		resources["block"] = {"current": block_curr, "maximum": 999}
	if include_energy:
		resources["energy"] = {"current": 3, "maximum": 3}
	var actor := {"instanceId": id, "definitionId": definition, "sideId": side, "resources": resources, "statuses": statuses}
	if not name_override.is_empty():
		actor["name"] = name_override
	return actor

func present(view, data: Dictionary, context) -> void:
	var cards = Zones.new(data.zones, context.translator)
	var presenter = Presenter.new(data.run, data.combat, data.get("actions", []), context.translator, data.presentation, data.zones)
	view.apply_projection(data.presentation, presenter, cards,
		{"run": data.run, "combat": data.combat, "actions": data.get("actions", []), "zones": data.zones, "input_actor": "hero", "scope": "battlefield-test", "setting": {}, "setting_id": "prototype-test"}, context)
	view.apply_interaction({"synchronized": true, "locked": false})

func run() -> void:
	var i18n = root.get_node("I18n")
	var prefs = root.get_node("Preferences")
	var session = root.get_node("GameSession")
	var prior := {"locale": i18n.locale, "scale": prefs.text_scale, "motion": prefs.reduced_motion,
		"run": session._run.duplicate(true), "combat": session._combat.duplicate(true), "zones": session._card_zones.duplicate(true),
		"actions": session._legal_actions.duplicate(true), "gateway": session._gateway, "attributes": session._character_attributes.duplicate(true)}
	var profile = load("res://data/themes/mvp_combat_theme.tres")
	var profile_before: String = profile.presentation_key()

	# 1. Verify authored battlefield_actor.tscn structure and separation of free-standing artwork from compact plate.
	var standalone_actor: ActorPanel = load("res://scenes/ui/components/battlefield_actor.tscn").instantiate()
	root.add_child(standalone_actor)
	await settle()
	var plate: PanelContainer = standalone_actor.get_node_or_null("%ActorPlate")
	var target_btn: Button = standalone_actor.target_button
	var title_lbl: Label = standalone_actor.get_node("%ActorTitle")
	var resources_box: VBoxContainer = standalone_actor.get_node("%ActorResources")
	var counters_box: HFlowContainer = standalone_actor.get_node("%ActorCounters")
	var statuses_box: HFlowContainer = standalone_actor.get_node("%ActorStatusBadges")
	var intent_row: HBoxContainer = standalone_actor.get_node("%ActorIntentRow")
	check(standalone_actor.authored_battlefield_layout and is_instance_valid(plate), "MVP battlefield_actor.tscn authors a dedicated %ActorPlate below artwork")
	check(target_btn.get_parent() == standalone_actor.get_node("%ActorContent") and plate.get_parent() == standalone_actor.get_node("%ActorContent"), "artwork target and info plate are siblings in ActorContent")
	check(target_btn.get_index() < plate.get_index() and target_btn.get_index() < resources_box.get_index(), "artwork target precedes compact plate and resources")
	check(plate.is_ancestor_of(title_lbl) and plate.is_ancestor_of(resources_box) and plate.is_ancestor_of(counters_box) and plate.is_ancestor_of(statuses_box) and plate.is_ancestor_of(intent_row), "name, resources, counters, statuses, and intent live inside the compact plate")
	var outer_style := standalone_actor.get_theme_stylebox("panel") as StyleBoxFlat
	var plate_style := plate.get_theme_stylebox("panel") as StyleBoxFlat
	check(outer_style != null and is_zero_approx(outer_style.bg_color.a), "outer unit container has no enclosing card background")
	check(plate_style != null and plate_style.bg_color.a > 0.9 and plate_style.border_width_top >= 1, "compact info plate has its own surface and border from the current theme")

	# Verify different widths between artwork target and compact plate are supported.
	target_btn.custom_minimum_size.x = 160.0
	plate.custom_minimum_size.x = 236.0
	await settle()
	check(not is_equal_approx(target_btn.size.x, plate.size.x) and plate.size.x >= 236.0, "artwork target width and compact plate width can differ independently")
	standalone_actor.queue_free()
	await process_frame

	# 2. Verify MVP combat view battlefield across actor configurations, resolutions, locales, and text scales.
	var view = load("res://scenes/ui/components/combat_mvp_view.tscn").instantiate()
	view.size = Vector2(1920, 1080)
	root.add_child(view)
	var base_snapshot: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/ui_editor_preview.json"))
	view.get_node(view.battlefield_path).embedded_artwork = true

	var configs := [
		{
			"name": "solo_vs_1",
			"actors": [
				_make_actor("hero", "spire_adept", "player", "Pyromancer", 72, 72, 10),
				_make_actor("boss", "stone_golem", "enemy", "Stone Golem", 120, 120, 15, [{"statusId": "burn", "stacks": 4, "isActive": true}])
			],
			"intents": [{"actorId": "boss", "actionId": "Heavy Slam"}]
		},
		{
			"name": "ally_vs_2",
			"actors": [
				_make_actor("hero", "spire_adept", "player", "Pyromancer", 64, 72, 8),
				_make_actor("ally", "water_elemental", "player", "Water Elemental", 20, 20, 4, [], false),
				_make_actor("wisp", "spire_wisp", "enemy", "Wandering Wisp", 32, 40, 0),
				_make_actor("sentinel", "spire_sentinel", "enemy", "Obsidian Sentinel", 55, 60, 12)
			],
			"intents": [{"actorId": "ally", "actionId": "Tidal Aid"}, {"actorId": "wisp", "actionId": "Attack"}, {"actorId": "sentinel", "actionId": "Guard"}]
		},
		{
			"name": "solo_vs_4",
			"actors": [
				_make_actor("hero", "spire_adept", "player", "Pyromancer", 50, 72, 6),
				_make_actor("e1", "spire_wisp", "enemy", "Ash Wisp Alpha", 24, 30, 0),
				_make_actor("e2", "spire_sentinel", "enemy", "Obsidian Vanguard", 44, 50, 8),
				_make_actor("e3", "stone_golem", "enemy", "Runic Colossus", 80, 90, 14),
				_make_actor("e4", "spire_wisp", "enemy", "Ember Stalker", 28, 30, 0)
			],
			"intents": [{"actorId": "e1", "actionId": "Spark"}, {"actorId": "e2", "actionId": "Fortify"}, {"actorId": "e3", "actionId": "Crush"}]
		}
	]

	for cfg in configs:
		var snap: Dictionary = base_snapshot.duplicate(true)
		snap.combat.actors = cfg.actors
		snap.combat.activation.intents = cfg.intents
		for locale in ["en", "pt_BR"]:
			i18n.set_locale(locale)
			for scale in [1.0, 1.6] if cfg.name == "ally_vs_2" else [1.0]:
				var context = Context.new(i18n, {"text_scale": scale, "reduced_motion": true}, profile)
				for resolution in [Vector2i(1920, 1080), Vector2i(1280, 720), Vector2i(1280, 800), Vector2i(2560, 1080)]:
					root.size = resolution
					view.size = Vector2(resolution)
					present(view, snap, context)
					await settle()
					var tag := "%s %s scale=%s res=%s" % [cfg.name, locale, scale, resolution]
					var stage = view.battlefield_view
					var actors_rect: Rect2 = view.battlefield_actors.get_global_rect()
					var header_rect: Rect2 = view.header_view.get_global_rect()
					var rail_rect: Rect2 = view.pile_controls.get_global_rect()
					var hand_rect: Rect2 = view.bottom_hud.get_global_rect()
					var first_card_top: float = view.hand.scroll.get_global_rect().position.y + view.hand.visual_style.padding
					check(actors_rect.position.x >= rail_rect.end.x - 1.0, "battlefield stays to the right of PileRail: " + tag)
					check(actors_rect.position.y >= header_rect.end.y - 1.0 and actors_rect.end.y <= first_card_top + 1.0, "battlefield stays between CombatHeader and raised hand cards: " + tag)
					check(not view.battlefield_actors.get_v_scroll_bar().visible, "battlefield actors fit vertically without vertical scrollbar: " + tag)
					check(stage.allies.get_global_rect().end.x <= stage.opponents.get_global_rect().position.x + 1.0, "allies stay on the left and opponents on the right: " + tag)
					for panel in stage.panels.values():
						var t_rect: Rect2 = panel.target_button.get_global_rect()
						var p_rect: Rect2 = panel.plate.get_global_rect()
						check(t_rect.end.y <= p_rect.position.y + 2.0, "free-standing artwork stays above compact info plate: " + tag)
						check(p_rect.end.y <= first_card_top + 2.0, "compact info plate stays above raised hand cards: " + tag)

	# 3. Verify honest data rules: no invented companion HP when only supportCombat exists, no fabricated ally intent row when ally has no intent, and explicit unrevealed state when enemy has no published intent.
	i18n.set_locale("en")
	var base_context = Context.new(i18n, {"text_scale": 1.0, "reduced_motion": true}, profile)
	root.size = Vector2i(1920, 1080)
	view.size = Vector2(1920, 1080)
	var honest_snap: Dictionary = base_snapshot.duplicate(true)
	honest_snap.run["supportCombat"] = {"supportId": "water_elemental", "routineId": "water_routine", "remainingOwnerActivations": 2}
	honest_snap.combat.actors = [
		_make_actor("hero", "custom_long_hero", "player", "Exceptionally Long Hero Name That Must Trim Cleanly Inside Plate", 48, 72, 8),
		_make_actor("mystery_enemy", "spire_wisp", "enemy", "Unrevealed Stalker", 40, 40, 0)
	]
	honest_snap.combat.activation.intents = []
	present(view, honest_snap, base_context)
	await settle()
	check(view.battlefield_view.panels.size() == 2 and view.battlefield_view.allies.get_child_count() == 1, "supportCombat metadata never fabricates a fake battlefield actor or companion HP bar")
	var hero_panel: ActorPanel = view.battlefield_view.panels["hero"]
	var mystery_panel: ActorPanel = view.battlefield_view.panels["mystery_enemy"]
	check(not hero_panel.get_node("%ActorIntentRow").visible, "ally without a published intent hides the intent row instead of showing redundant filler")
	check(mystery_panel.get_node("%ActorIntentRow").visible and mystery_panel.get_node("%ActorIntention").text == "No intent revealed", "enemy without a published intent shows honest unrevealed state without inventing an action")
	check(hero_panel.get_node("%ActorTitle").text_overrun_behavior == TextServer.OVERRUN_TRIM_ELLIPSIS and hero_panel.get_node("%ActorTitle").tooltip_text.contains("Exceptionally Long Hero Name"), "long actor names trim cleanly with full tooltip")

	# 4. Verify damage, healing, counter updates, status badge lifecycle, and actor removal on the battlefield.
	var combat_panel: ActorPanel = mystery_panel
	var hp_view: ResourceView = combat_panel.resource_view("health")
	check(hp_view != null and hp_view.amount.text == "40/40", "enemy health bar starts at 40/40")
	combat_panel.present_resource({"resourceId": "health", "resourceField": "Current", "previousValue": 40, "currentValue": 25}, 0.0)
	check(hp_view.amount.text == "25/40" and is_equal_approx(hp_view.bar.value, 25.0), "damage updates health bar value and label")
	combat_panel.present_resource({"resourceId": "health", "resourceField": "Current", "previousValue": 25, "currentValue": 34}, 0.0)
	check(hp_view.amount.text == "34/40" and is_equal_approx(hp_view.bar.value, 34.0), "healing updates health bar value and label")

	honest_snap.combat.actors[1].statuses = [
		{"statusId": "burn", "stacks": 5, "isActive": true},
		{"statusId": "vulnerable", "stacks": 2, "isActive": true}
	]
	present(view, honest_snap, base_context)
	await settle()
	check(combat_panel.status_badge("burn") != null and combat_panel.status_badge("vulnerable") != null and combat_panel.get_node("%ActorStatusBadges").visible, "active statuses render badges inside the compact plate")
	honest_snap.combat.actors[1].statuses = [{"statusId": "burn", "stacks": 3, "isActive": false}]
	present(view, honest_snap, base_context)
	await settle()
	check(combat_panel.status_badge("burn") == null and not combat_panel.get_node("%ActorStatusBadges").visible, "cleared/inactive statuses prune badges and hide status row")

	# Add a second enemy then remove it via snapshot update.
	honest_snap.combat.actors.append(_make_actor("summon", "spire_sentinel", "enemy", "Summoned Construct", 30, 30, 5))
	present(view, honest_snap, base_context)
	await settle()
	check(view.battlefield_view.panels.has("summon") and view.battlefield_view.opponents.get_child_count() == 2, "added actor appears in Opponents team")
	honest_snap.combat.actors.pop_back()
	present(view, honest_snap, base_context)
	await settle()
	check(not view.battlefield_view.panels.has("summon") and view.battlefield_view.opponents.get_child_count() == 1, "removed actor is pruned from panels, targets, and Opponents team")

	view.queue_free()
	await process_frame

	# 5. Full CombatScreen integration: scenery portrait anchor sync, target selection, hover preview, and non-mutation of engine state.
	prefs.reduced_motion = true
	prefs.text_scale = 1.0
	session.configure(BattlefieldFixture.new())
	session.synchronized = true
	session._run = {"settingId": "prototype-test", "runId": "stage4-run", "sequence": 1, "playerEntityId": "hero"}
	session._combat = {
		"combatId": "stage4-combat",
		"activeActorId": "hero",
		"activation": {
			"round": 1,
			"activeActorId": "hero",
			"waitingForInput": true,
			"intents": [{"actorId": "wisp", "actionId": "Attack"}, {"actorId": "sentinel", "actionId": "Poison Spit"}]
		},
		"relationships": {"sameSide": "Ally", "differentSides": "Enemy"},
		"actors": [
			_make_actor("hero", "spire_adept", "player", "", 48, 72, 8),
			_make_actor("ally", "water_elemental", "player", "Water Elemental", 20, 20, 4, [], false),
			_make_actor("wisp", "spire_wisp", "enemy", "", 32, 48, 0),
			_make_actor("sentinel", "spire_sentinel", "enemy", "", 60, 60, 10, [{"statusId": "burn", "stacks": 3, "isActive": true}])
		]
	}
	session._card_zones = base_snapshot.zones.duplicate(true)
	session._legal_actions = [
		{"source": "Card", "command": {"cardInstanceId": "preview-0", "actionType": "PLAY_CARD", "targetIds": ["wisp", "sentinel"]}, "costs": [{"resourceId": "energy", "amount": 1}], "applications": [{"targetEntityId": "wisp", "resourceId": "health", "previousValue": 32, "currentValue": 24}]},
		{"source": "Card", "command": {"cardInstanceId": "preview-0", "actionType": "PLAY_CARD", "targetIds": ["wisp"], "costOptionId": "empower"}, "costs": [{"resourceId": "energy", "amount": 2}], "applications": [{"targetEntityId": "wisp", "resourceId": "health", "previousValue": 32, "currentValue": 18}]}
	]
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	root.size = Vector2i(1920, 1080)
	router.show_combat()
	await settle()
	var screen = router.host.get_child(0)
	var scenery = screen.view.scenery
	check(is_instance_valid(scenery) and scenery.portraits.size() == 4, "CombatSceneryView creates free-standing artwork slots for all four actors")
	for actor_id in ["hero", "ally", "wisp", "sentinel"]:
		var art_slot: Control = scenery.portraits[actor_id]
		var anchor_btn: Button = screen.view.target_buttons[actor_id]
		var actor_plate: PanelContainer = screen.view.battlefield_view.panels[actor_id].plate
		check(art_slot.get_global_rect().is_equal_approx(anchor_btn.get_global_rect()), "scenery portrait aligns with ActorTarget anchor: " + actor_id)
		check(art_slot.get_global_rect().end.y <= actor_plate.get_global_rect().position.y + 2.0, "scenery portrait stands above compact info plate: " + actor_id)

	screen._choose_card("preview-0")
	await settle()
	check(not screen.view.target_buttons["wisp"].disabled and not screen.view.target_buttons["sentinel"].disabled and screen.view.target_buttons["hero"].disabled, "selecting a targeted card enables only legal opponent targets")
	screen.view.target_buttons["wisp"].mouse_entered.emit()
	await settle()
	check(screen.view.detail_label.text.contains("32 → 24"), "hovering a legal target previews the published application")
	screen.view.target_buttons["wisp"].pressed.emit()
	await settle()
	check(screen.selected_target == "wisp" and screen.view.candidate_panel.visible and session._run.sequence == 1, "clicking target selects actor and shows candidate options without mutating engine state")
	screen._cancel_selection()

	check(profile.presentation_key() == profile_before, "Stage 4 never mutates theme resource")
	router.queue_free()
	await settle()
	session._run = prior.run; session._combat = prior.combat; session._card_zones = prior.zones
	session._character_attributes = prior.attributes; session._legal_actions = prior.actions; session.configure(prior.gateway)
	prefs.text_scale = prior.scale; prefs.reduced_motion = prior.motion; i18n.set_locale(prior.locale)
	root.get_node("GameAudio").shutdown()
	await create_timer(.05).timeout
	quit(1 if failures else 0)
