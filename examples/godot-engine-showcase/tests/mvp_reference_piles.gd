extends SceneTree
## Stage 3 verification: left pile rail, full addresses, collection modal, focus return, drawer non-interference and draw motion origin.
const Context = preload("res://scripts/ui/visual_context.gd")
const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const Zones = preload("res://scripts/presentation/card_zone_presenter.gd")
const Collection = preload("res://scripts/presentation/card_collection_presenter.gd")
const Motion = preload("res://scripts/ui/card_zone_motion.gd")
const Pose = preload("res://scripts/ui/card_motion_pose.gd")
const Orchestrator = preload("res://scripts/ui/controllers/combat_beat_orchestrator.gd")
var failures := 0

class PileFixture extends RefCounted:
	var cards: Array = []
	func connect_engine() -> Dictionary:
		return {"ok": false, "status": 503, "error": "Offline pile fixture"}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		return {"ok": true, "data": {"cards": []}}
	func read_card_presentations(run_id: String, _definitions: Array = []) -> Dictionary:
		return {"ok": true, "data": {"runId": run_id, "runSequence": 1, "contentRevision": "", "cards": cards}}

func _init() -> void: run.call_deferred()
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 12: await process_frame

func _zone_fixture() -> Dictionary:
	return {
		"zones": [
			{
				"zoneId": "owned",
				"ownerId": "$run",
				"scopeId": null,
				"contentsVisible": true,
				"orderVisible": true,
				"count": 4,
				"presentation": {"displayName": "Collection / reserve"},
				"cards": [
					{"cardInstanceId": "owned-b", "definitionId": "defend", "name": "Guard", "effects": ["Gain 8 Block."], "costs": [{"resourceId": "energy", "amount": 1}]},
					{"cardInstanceId": "owned-a", "definitionId": "basic_attack", "name": "Strike", "effects": ["Deal 8 damage."], "costs": [{"resourceId": "energy", "amount": 1}]}
				]
			},
			{
				"zoneId": "discard",
				"ownerId": "hero",
				"scopeId": "combat-1",
				"contentsVisible": true,
				"orderVisible": true,
				"count": 3,
				"presentation": {"slot": "discard_pile", "labelKey": "Discard pile"},
				"cards": [
					{"cardInstanceId": "disc-z", "definitionId": "fireball", "name": "Fireball", "effects": ["Deal 10 damage."], "costs": [{"resourceId": "energy", "amount": 1}]},
					{"cardInstanceId": "disc-a", "definitionId": "basic_attack", "name": "Strike", "effects": ["Deal 8 damage."], "costs": [{"resourceId": "energy", "amount": 1}]}
				]
			},
			{
				"zoneId": "hand",
				"ownerId": "hero",
				"scopeId": "combat-1",
				"allowsCardPlay": true,
				"contentsVisible": true,
				"orderVisible": true,
				"count": 2,
				"cards": [
					{"cardInstanceId": "hand-0", "definitionId": "basic_attack", "name": "Strike", "effects": ["Deal 8 damage."], "costs": [{"resourceId": "energy", "amount": 1}]},
					{"cardInstanceId": "hand-1", "definitionId": "defend", "name": "Guard", "effects": ["Gain 8 Block."], "costs": [{"resourceId": "energy", "amount": 1}]}
				]
			},
			{
				"zoneId": "banish",
				"ownerId": "hero",
				"scopeId": "combat-1",
				"contentsVisible": true,
				"orderVisible": true,
				"count": 1,
				"presentation": {"slot": "exile_pile", "labelKey": "Exile"},
				"cards": [
					{"cardInstanceId": "exile-0", "definitionId": "heal", "name": "Recover", "effects": ["Restore 6 health."], "costs": [{"resourceId": "energy", "amount": 1}]}
				]
			},
			{
				"zoneId": "draw",
				"ownerId": "hero",
				"scopeId": "combat-1",
				"contentsVisible": true,
				"orderVisible": false,
				"count": 12,
				"presentation": {"slot": "draw_pile", "labelKey": "Draw pile"},
				"cards": [
					{"cardInstanceId": "draw-z", "definitionId": "fireball", "name": "Fireball", "effects": ["Deal 10 damage."], "costs": [{"resourceId": "energy", "amount": 1}]},
					{"cardInstanceId": "draw-a", "definitionId": "basic_attack", "name": "Strike", "effects": ["Deal 8 damage."], "costs": [{"resourceId": "energy", "amount": 1}]}
				]
			}
		]
	}

func present(view, data: Dictionary, context) -> void:
	var cards = Zones.new(data.zones, context.translator)
	var presenter = Presenter.new(data.run, data.combat, [], context.translator, data.presentation, data.zones)
	view.apply_projection(data.presentation, presenter, cards,
		{"run": data.run, "combat": data.combat, "actions": [], "zones": data.zones, "input_actor": "hero", "scope": "piles-test", "setting": {}, "setting_id": "prototype-test"}, context)
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

	var view = load("res://scenes/ui/components/combat_mvp_view.tscn").instantiate()
	view.size = Vector2(1920, 1080)
	root.add_child(view)
	var snapshot: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/ui_editor_preview.json"))
	snapshot.zones = _zone_fixture()

	check(view.has_node("CanvasHost/ReferenceCanvas/PileRail"), "MVP authors dedicated PileRail on ReferenceCanvas")
	check(view.primary_piles_path == NodePath("CanvasHost/ReferenceCanvas/PileRail") and view.secondary_piles_path == NodePath("CanvasHost/ReferenceCanvas/RightPiles"), "MVP binds the draw and right-side piles to separate groups")

	for locale in ["en", "pt_BR"]:
		i18n.set_locale(locale)
		for scale in [1.0, 1.6]:
			var context = Context.new(i18n, {"text_scale": scale, "reduced_motion": true}, profile)
			for resolution in [Vector2(1920, 1080), Vector2(1280, 720), Vector2(1280, 800), Vector2(2560, 1080)]:
				view.size = resolution
				present(view, snapshot, context)
				await settle()
				var label := "%s scale=%s res=%s" % [locale, scale, resolution]
				var rail: Control = view.pile_controls
				var canvas: Rect2 = view.gameplay_column.get_global_rect()
				var header_rect: Rect2 = view.header_view.get_global_rect()
				var hand_rect: Rect2 = view.bottom_hud.get_global_rect()
				var rail_rect: Rect2 = rail.get_global_rect()
				check(rail == view.get_node("CanvasHost/ReferenceCanvas/PileRail") and rail.visible, "PileRail is active and visible: " + label)
				check(not view.turn_view.get_node("PileControls").visible and not view.turn_view.get_node("RightPileControls").visible, "legacy right-side pile containers are hidden in MVP: " + label)
				check(canvas.encloses(rail_rect) and rail_rect.position.y >= header_rect.end.y and rail_rect.end.x <= hand_rect.position.x, "PileRail stays in left column below header and left of hand: " + label)
				var buttons: Array[Button] = view.pile_buttons()
				check(buttons.size() == 4, "exactly four auxiliary pile buttons are mounted: " + label)
				check(buttons.map(func(b): return str(b.get_meta("zone_address").get("zoneId", ""))) == ["draw", "banish", "discard", "owned"], "all four pile addresses remain reachable: " + label)
				if buttons.size() == 4:
					var turn: Rect2 = view.turn_view.get_global_rect()
					check(buttons[0].get_global_rect().end.x <= hand_rect.position.x, "draw stays left of the hand: " + label)
					check(buttons[1].get_global_rect().end.y <= buttons[2].get_global_rect().position.y and buttons[2].get_global_rect().end.y <= turn.position.y, "exile and discard stay above end turn: " + label)
					check(buttons[1].get_global_rect().position.x >= hand_rect.end.x - 1.0, "exile and discard stay right of the hand: " + label)
					check(header_rect.encloses(buttons[3].get_global_rect()), "deck stays in the header: " + label)

	i18n.set_locale("en")
	var base_context = Context.new(i18n, {"text_scale": 1.0, "reduced_motion": true}, profile)
	view.size = Vector2(1920, 1080)
	present(view, snapshot, base_context)
	await settle()
	var buttons: Array[Button] = view.pile_buttons()
	check(buttons[0].get_meta("zone_address") == {"zoneId": "draw", "ownerId": "hero", "scopeId": "combat-1"}, "Draw retains full zone address")
	check(buttons[1].get_meta("zone_address") == {"zoneId": "banish", "ownerId": "hero", "scopeId": "combat-1"}, "Exile retains full zone address")
	check(buttons[2].get_meta("zone_address") == {"zoneId": "discard", "ownerId": "hero", "scopeId": "combat-1"}, "Discard retains full zone address")
	check(buttons[3].get_meta("zone_address") == {"zoneId": "owned", "ownerId": "$run", "scopeId": null}, "Owned retains full run-scoped address without fabricating sum")
	check(buttons[0].text.ends_with("\n12") and buttons[1].text.ends_with("\n1") and buttons[2].text.ends_with("\n3") and buttons[3].text.ends_with("\n4"), "pile buttons show published counts without summing zones")
	var floating_counts: Array = snapshot.zones.zones.duplicate(true)
	for zone in floating_counts: zone.count = float(zone.get("count", 0))
	var context_zones = preload("res://scripts/presentation/card_zone_presenter.gd").new({"zones": floating_counts}, base_context.translator)
	var saved_presenter = view.zone_presenter
	view.zone_presenter = context_zones
	view._build_piles()
	for button in view.pile_buttons():
		var count_text: String = button.text.get_slice("\n", 1)
		check(count_text.is_valid_int() and button.tooltip_text.ends_with(count_text), "JSON numeric counts render as integers in labels and tooltips")
	view.zone_presenter = saved_presenter
	view._build_piles()
	check(buttons[1].icon == preload("res://scripts/ui/combat_chrome.gd").texture("exile"), "banish/exile_pile uses the exile icon")
	check(buttons[3].tooltip_text.begins_with("Collection / reserve · 4"), "owned zone uses published displayName")

	# Left-edge drawer non-interference check: hovering near left edge at PileRail height must NOT open drawer, while top-left above PileRail still opens it.
	var drawer = view.character_sidebar
	drawer.set_open(false)
	var motion := InputEventMouseMotion.new()
	motion.position = Vector2(120, buttons[0].get_global_rect().get_center().y)
	view._input(motion)
	motion.position = Vector2(4, buttons[0].get_global_rect().get_center().y)
	view._input(motion)
	check(not drawer.expanded and not drawer.visible, "hovering left edge over PileRail does not trigger CombatAttributesDrawer")
	motion.position = Vector2(120, view.header_view.get_global_rect().end.y + 8.0)
	view._input(motion)
	motion.position = Vector2(4, view.header_view.get_global_rect().end.y + 8.0)
	view._input(motion)
	check(drawer.expanded and drawer.visible, "hovering left edge above PileRail still opens CombatAttributesDrawer")
	drawer.set_open(false)

	# Empty auxiliary zones hide PileRail cleanly without errors.
	var empty_snapshot: Dictionary = snapshot.duplicate(true)
	empty_snapshot.zones = {"zones": [snapshot.zones.zones[2]]}
	present(view, empty_snapshot, base_context)
	await settle()
	check(not view.pile_controls.visible and view.pile_buttons().is_empty(), "PileRail hides when no auxiliary zones are published")
	present(view, snapshot, base_context)
	await settle()

	view.queue_free()
	await process_frame

	# Full CombatScreen integration: opening collection modal, CardView instances, focus return, and draw motion from left PileRail.
	var fixture := PileFixture.new()
	prefs.reduced_motion = true
	prefs.text_scale = 1.0
	session.configure(fixture)
	session.synchronized = true
	session._run = {"settingId": "prototype-test", "runId": "pile-run", "sequence": 1, "playerEntityId": "hero"}
	session._combat = snapshot.combat.duplicate(true)
	session._combat.combatId = "combat-1"
	session._card_zones = _zone_fixture()
	session._legal_actions = []
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	root.size = Vector2i(1920, 1080)
	router.show_combat()
	await settle()
	var screen = router.host.get_child(0)
	var screen_buttons: Array[Button] = screen.view.pile_buttons()
	check(screen_buttons.size() == 4, "CombatScreen mounts four pile buttons in PileRail")

	# Click Discard button -> opens CardZoneDialog with CardView instances in published order, then restores focus on close.
	var discard_btn: Button = screen_buttons[2]
	discard_btn.grab_focus()
	discard_btn.pressed.emit()
	await settle()
	var dialog: Window = screen._collection_dialog
	check(is_instance_valid(dialog) and dialog.visible, "clicking Discard pile opens CardZoneDialog")
	check(dialog.collection.cards.size() == 2 and dialog.collection.cards.values().all(func(node): return node is CardView), "collection modal renders real CardView instances, not a text list")
	check(dialog.collection.model.ordered and dialog.collection.model.cards.map(func(c): return c.cardInstanceId) == ["disc-z", "disc-a"], "Discard pile preserves published orderVisible=true order")
	dialog.confirmed.emit()
	await settle()
	check(root.gui_get_focus_owner() == discard_btn, "closing Discard collection restores focus to the Discard button in PileRail")

	# Click Draw button (orderVisible=false) -> sorted catalog order, then restores focus.
	var draw_btn: Button = screen_buttons[0]
	draw_btn.grab_focus()
	draw_btn.pressed.emit()
	await settle()
	dialog = screen._collection_dialog
	check(is_instance_valid(dialog) and not dialog.collection.model.ordered and dialog.collection.model.cards.map(func(c): return c.cardInstanceId) == ["draw-a", "draw-z"], "Draw pile with orderVisible=false uses neutral sorted catalog")
	dialog.confirmed.emit()
	await settle()
	check(root.gui_get_focus_owner() == draw_btn, "closing Draw collection restores focus to the Draw button in PileRail")

	# Hidden contents (contentsVisible=false) -> shows hidden placeholder, zero CardView instances.
	session._card_zones.zones[4].contentsVisible = false
	screen.refresh_state(router.presentation)
	await settle()
	draw_btn = screen.view.pile_buttons()[0]
	draw_btn.pressed.emit()
	await settle()
	dialog = screen._collection_dialog
	check(is_instance_valid(dialog) and not dialog.collection.model.visible and dialog.collection.cards.is_empty() and dialog.collection._back.visible, "contentsVisible=false hides card faces and shows hidden back placeholder")
	dialog.confirmed.emit()
	await settle()

	# Verify zone motion anchors resolve to the left-side PileRail buttons and draw originates from Draw button without changing durations.
	var after_zones: Dictionary = _zone_fixture()
	after_zones.zones[4].count = 11
	after_zones.zones[4].cards = [after_zones.zones[4].cards[0]]
	after_zones.zones[2].count = 3
	after_zones.zones[2].cards.append({"cardInstanceId": "draw-a", "definitionId": "basic_attack", "name": "Strike", "effects": ["Deal 8 damage."], "costs": [{"resourceId": "energy", "amount": 1}]})
	var anchors := Orchestrator.collect_zone_anchors_and_roles(screen.view, [session._card_zones, after_zones])
	var draw_key := Motion.address_key({"zoneId": "draw", "ownerId": "hero", "scopeId": "combat-1"})
	var exile_key := Motion.address_key({"zoneId": "banish", "ownerId": "hero", "scopeId": "combat-1"})
	var discard_key := Motion.address_key({"zoneId": "discard", "ownerId": "hero", "scopeId": "combat-1"})
	var owned_key := Motion.address_key({"zoneId": "owned", "ownerId": "$run", "scopeId": null})
	check(anchors.pile_anchors.get(draw_key) == screen.view.pile_buttons()[0] and anchors.pile_anchors.get(exile_key) == screen.view.pile_buttons()[1] and anchors.pile_anchors.get(discard_key) == screen.view.pile_buttons()[2] and anchors.pile_anchors.get(owned_key) == screen.view.pile_buttons()[3], "CombatBeatOrchestrator resolves all four pile anchors to left PileRail buttons")
	var draw_origin: Vector2 = anchors.pile_anchors[draw_key].get_global_rect().get_center()
	check(draw_origin.x < screen.view.bottom_hud.get_global_rect().position.x, "Draw anchor origin is on the left of the hand host")

	var cursor = root.get_node("Playback")
	prefs.reduced_motion = false
	var before_snap := {"run": session._run.duplicate(true), "combat": session._combat.duplicate(true), "zones": session._card_zones.duplicate(true)}
	var after_snap := {"run": session._run.duplicate(true), "combat": session._combat.duplicate(true), "zones": after_zones}
	after_snap.run.sequence = 2
	after_snap.combat.step = 2
	var frame := {"transitionType": "effect", "cardZoneSteps": [{"sourceAddress": draw_key, "targetAddress": Motion.address_key({"zoneId": "hand", "ownerId": "hero", "scopeId": "combat-1"}), "instanceIds": ["draw-a"]}]}
	var receipt := {"sequence": 2, "state": {"combat": after_snap.combat, "resolution": {"frames": [frame]}}}
	var envelope = preload("res://scripts/presentation/presentation_envelope.gd").build(receipt, before_snap, after_snap, [])
	session._run = after_snap.run.duplicate(true)
	session._combat = after_snap.combat.duplicate(true)
	session._card_zones = after_snap.zones.duplicate(true)
	var default_policy = preload("res://scripts/presentation/motion_policy.gd").capture("default", {})
	var expected_draw_duration: float = default_policy.duration("draw", true)
	screen._active_motion = default_policy
	cursor.load_envelope(envelope)
	screen._on_presentation_ready(envelope)
	check(screen._zone_anchor(draw_key, "draw-a").is_equal_approx(draw_origin), "CombatScreen._zone_anchor resolves draw source to left PileRail Draw button center")
	check(is_equal_approx(screen._active_motion.duration("draw", true), expected_draw_duration), "draw motion duration remains unchanged")
	screen.presentation_driver.skip()
	await settle()

	check(profile.presentation_key() == profile_before, "Stage 3 never mutates theme resource")
	router.queue_free()
	await settle()
	cursor.clear()
	session._run = prior.run; session._combat = prior.combat; session._card_zones = prior.zones
	session._character_attributes = prior.attributes; session._legal_actions = prior.actions; session.configure(prior.gateway)
	prefs.text_scale = prior.scale; prefs.reduced_motion = prior.motion; i18n.set_locale(prior.locale)
	root.get_node("GameAudio").shutdown()
	await create_timer(.05).timeout
	quit(1 if failures else 0)
