extends SceneTree
## Stage 5 verification: central EnergyIndicator above the hand, isolated EndTurnAndContextActions in the bottom-right corner,
## published energy states (zero, surplus, unbounded, absent), EndTurn availability/locking, remapped shortcuts,
## context controls visibility (NextAnimation, SkipPresentationButton, CancelSelection, no permanent ActionPreview or Pass),
## pause/Escape priority during selection and resolution, and hover/selection near the rightmost hand cards.
const Context = preload("res://scripts/ui/visual_context.gd")
const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const Zones = preload("res://scripts/presentation/card_zone_presenter.gd")
var failures := 0

class EnergyTurnFixture extends RefCounted:
	var submissions: Array = []
	var combat_snapshot: Dictionary = {}
	var zones_snapshot: Dictionary = {}
	var actions_snapshot: Array = []
	func connect_engine() -> Dictionary:
		return {"ok": false, "status": 503, "error": "Offline energy/turn fixture"}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		return {"ok": true, "data": {"cards": []}}
	func candidate_payload(command_data: Dictionary) -> Dictionary:
		return command_data.duplicate(true)
	func prepare_command(scope: String, id: String, type: String, payload: Dictionary, sequence: int, expected_step: int) -> Dictionary:
		return {"scope": scope, "id": id, "type": type, "payload": payload.duplicate(true), "sequence": sequence, "expectedStep": expected_step}
	func send_prepared(pending: Dictionary) -> Dictionary:
		submissions.append(pending.duplicate(true))
		return {"ok": true, "data": {"sequence": 1, "state": {"resolution": null, "combat": combat_snapshot.duplicate(true)}}}
	func read_session(_id: String, _expected := -1) -> Dictionary:
		var version := {"runId": "stage5-run", "settingId": "prototype-test", "runSequence": 1, "runStep": 0,
			"engineVersion": "31", "contentRevision": "rev", "stateFingerprint": "hash"}
		return {"ok": true, "data": {"version": version,
			"run": {"runId": "stage5-run", "settingId": "prototype-test", "sequence": 1, "step": 0, "playerEntityId": "hero", "lifecycle": "Active", "engineVersion": "31", "contentRevision": "rev", "stateHash": "hash"},
			"combat": combat_snapshot.duplicate(true), "commands": {"commands": []}, "capabilities": {"granted": []},
			"cardZones": zones_snapshot.duplicate(true), "attributes": {"settingId": "prototype-test", "runId": "stage5-run", "runSequence": 1, "actorId": "hero", "attributes": [], "criticalRules": []},
			"cardPresentations": {}, "modifierItems": {},
			"actions": {"candidates": actions_snapshot.duplicate(true)}, "evaluations": {"cards": []}}}

func _init() -> void: run.call_deferred()
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle(frames := 12) -> void:
	for _frame in frames: await process_frame

func motion(point: Vector2) -> void:
	var event := InputEventMouseMotion.new()
	event.position = point
	event.global_position = point
	root.push_input(event, true)

func click(point: Vector2, pressed: bool, button_index := MOUSE_BUTTON_LEFT) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = button_index
	event.pressed = pressed
	event.position = point
	event.global_position = point
	root.push_input(event, true)

func press_key(keycode: int) -> void:
	var down := InputEventKey.new()
	down.pressed = true
	down.keycode = keycode
	down.physical_keycode = keycode
	root.push_input(down, true)
	var up := InputEventKey.new()
	up.pressed = false
	up.keycode = keycode
	up.physical_keycode = keycode
	root.push_input(up, true)

func _key_event(keycode: int) -> InputEventKey:
	var event := InputEventKey.new()
	event.physical_keycode = keycode
	return event

func present(view, data: Dictionary, context, interaction_override: Dictionary = {}) -> void:
	var cards = Zones.new(data.zones, context.translator)
	var presenter = Presenter.new(data.run, data.combat, data.get("actions", []), context.translator, data.presentation, data.zones)
	view.apply_projection(data.presentation, presenter, cards,
		{"run": data.run, "combat": data.combat, "actions": data.get("actions", []), "zones": data.zones, "input_actor": str(data.get("input_actor", "hero")), "scope": "energy-turn-test", "setting": {}, "setting_id": "prototype-test"}, context)
	var state := {"synchronized": true, "locked": false}
	state.merge(interaction_override, true)
	view.apply_interaction(state)

func run() -> void:
	var i18n = root.get_node("I18n")
	var prefs = root.get_node("Preferences")
	var session = root.get_node("GameSession")
	var playback = root.get_node("Playback")
	var prior := {"locale": i18n.locale, "scale": prefs.text_scale, "motion": prefs.reduced_motion,
		"end_key": prefs.action_key("end_turn"), "confirm_key": prefs.action_key("confirm_action"),
		"pause_key": prefs.action_key("pause_game"),
		"run": session._run.duplicate(true), "combat": session._combat.duplicate(true), "zones": session._card_zones.duplicate(true),
		"actions": session._legal_actions.duplicate(true), "gateway": session._gateway, "attributes": session._character_attributes.duplicate(true)}
	var profile = load("res://data/themes/mvp_combat_theme.tres")
	var profile_before: String = profile.presentation_key()

	# 1. Verify authored scene structure in combat_mvp_view.tscn.
	var view = load("res://scenes/ui/components/combat_mvp_view.tscn").instantiate()
	view.size = Vector2(1920, 1080)
	root.add_child(view)
	var base_snapshot: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/ui_editor_preview.json"))
	view.get_node(view.battlefield_path).embedded_artwork = true

	var canvas: Control = view.get_node("CanvasHost/ReferenceCanvas")
	var energy_node: Label = view.get_node(view.energy_indicator_path)
	var turn_node: Control = view.get_node(view.turn_controls_path)
	var hand_host: Control = view.get_node(view.hand_host_path)
	var hand_section: VBoxContainer = view.get_node(view.hand_section_path)
	check(energy_node.get_parent() == canvas and not hand_section.has_node("Energy"), "EnergyIndicator is authored on ReferenceCanvas outside HandHost/HandSection")
	check(turn_node.get_parent() == canvas and turn_node.name == "EndTurnAndContextActions", "EndTurnAndContextActions is authored on ReferenceCanvas in bottom-right")
	check(not turn_node.has_meta("stage_note") and not canvas.has_node("TemporaryControls"), "temporary stage placeholder is replaced by authored EndTurnAndContextActions")
	check(not hand_host.is_ancestor_of(energy_node) and not hand_host.is_ancestor_of(turn_node), "neither EnergyIndicator nor EndTurnAndContextActions live inside HandHost")

	# 2. Verify EnergyIndicator states: standard (3/3), zero (0/3), surplus (5/3), unbounded (4 without maximum), and absent.
	i18n.set_locale("en")
	var context_en = Context.new(i18n, {"text_scale": 1.0, "reduced_motion": true, "action_labels": {"end_turn": "E", "confirm_action": "F", "pause_game": "Escape", "inspect_card": "V"}}, profile)
	var snap: Dictionary = base_snapshot.duplicate(true)
	snap["actions"] = [{"source": "System", "command": {"actionType": "END_TURN"}}]
	present(view, snap, context_en)
	await settle()
	check(energy_node.visible and energy_node.text == "◇  Energy  3 / 3", "standard bounded energy displays published current / maximum")

	snap.combat.actors[0].resources.energy = {"current": 0, "minimum": 0, "maximum": 3}
	present(view, snap, context_en)
	await settle()
	check(energy_node.visible and energy_node.text == "◇  Energy  0 / 3", "zero energy remains visible and displays 0 / maximum")

	snap.combat.actors[0].resources.energy = {"current": 5, "minimum": 0, "maximum": 3}
	present(view, snap, context_en)
	await settle()
	check(energy_node.visible and energy_node.text == "◇  Energy  5 / 3", "surplus energy above maximum displays actual published current / maximum without clamping to 3")

	snap.combat.actors[0].resources.energy = {"current": 7, "minimum": 0, "maximum": 5}
	present(view, snap, context_en)
	await settle()
	check(energy_node.visible and energy_node.text == "◇  Energy  7 / 5", "custom maximum energy displays published maximum, never a hardcoded /3")

	snap.combat.actors[0].resources.energy = {"current": 4}
	present(view, snap, context_en)
	await settle()
	check(energy_node.visible and energy_node.text == "◇  Energy  4" and not energy_node.text.contains("/"), "unbounded energy without maximum omits slash and maximum")

	snap.combat.actors[0].resources.erase("energy")
	present(view, snap, context_en)
	await settle()
	check(not energy_node.visible and energy_node.text.is_empty(), "absent energy resource hides EnergyIndicator cleanly")

	# Verify intermediate frame snapshot updates EnergyIndicator without mutating authoritative combat snapshot.
	snap.combat.actors[0].resources["energy"] = {"current": 3, "minimum": 0, "maximum": 3}
	present(view, snap, context_en)
	await settle()
	var auth_before := JSON.stringify(view._combat_snapshot)
	var frame_state: Dictionary = snap.combat.duplicate(true)
	frame_state.actors[0].resources.energy.current = 1
	view.present_combat_snapshot(frame_state)
	await settle()
	check(energy_node.visible and energy_node.text == "◇  Energy  1 / 3", "intermediate combat snapshot updates EnergyIndicator during playback")
	check(JSON.stringify(view._combat_snapshot) == auth_before, "intermediate energy display does not mutate authoritative snapshot")

	# 3. Verify EndTurnButton and context controls visibility across states, plus remapped shortcuts.
	var end_btn: Button = turn_node.get_node("EndTurnButton")
	var next_btn: Button = turn_node.next_frame_button
	var skip_btn: Button = turn_node.skip_button
	var cancel_box: Control = turn_node.selection_actions
	var cancel_btn: Button = cancel_box.get_node("CancelSelection")
	var preview_panel: Control = turn_node.get_node("ActionPreviewView")
	var pass_slot: Control = turn_node.get_node("SystemActionSlot")

	present(view, snap, context_en, {"locked": false, "has_frames": false, "selected_card": ""})
	await settle()
	check(end_btn.visible and not end_btn.disabled, "EndTurnButton is visible and enabled when END_TURN is published and unlocked")
	check(end_btn.text == "END TURN" and end_btn.tooltip_text == "END TURN", "EndTurnButton displays its label without the shortcut")
	check(not next_btn.visible and not skip_btn.visible and not cancel_box.visible and not turn_node.get_node("ContextActionSlot").visible, "advance/skip/cancel are hidden when idle with no selected card or frames")
	check(not preview_panel.visible and not pass_slot.visible, "ActionPreviewView and Pass remain hidden in MVP")
	check(not view.hand.toolbar.visible, "HandTools stays hidden when no overflow pagination buttons are visible (<= 5 cards)")

	# Selecting a card reveals CancelSelection only.
	present(view, snap, context_en, {"locked": false, "has_frames": false, "selected_card": "preview-0"})
	await settle()
	check(cancel_box.visible and not cancel_btn.disabled and not next_btn.visible and not skip_btn.visible, "selecting a card reveals CancelSelection without showing NextAnimation or Skip")
	check(not preview_panel.visible, "selecting a card does not introduce a permanent ActionPreviewView")

	# Active presentation frames reveal NextAnimation and SkipPresentationButton, and lock EndTurnButton.
	present(view, snap, context_en, {"locked": true, "has_frames": true, "remaining_frames": 2, "selected_card": ""})
	await settle()
	check(next_btn.visible and skip_btn.visible and not skip_btn.disabled and not cancel_box.visible, "presentation frames reveal NextAnimation and SkipPresentationButton while hiding CancelSelection")
	check(end_btn.disabled, "EndTurnButton is disabled while presentation frames lock input")
	var playback_rect: Rect2 = next_btn.get_parent().get_global_rect()
	var playback_end_rect: Rect2 = end_btn.get_global_rect()
	check(playback_rect.end.x <= playback_end_rect.position.x and absf(playback_rect.get_center().y - playback_end_rect.get_center().y) < 1.0, "playback controls sit directly left of end turn")
	check(next_btn.get_global_rect().end.x <= skip_btn.get_global_rect().position.x, "advance and skip are arranged horizontally")
	check(next_btn.pressed.is_connected(Callable(turn_node, "_on_next_pressed")) and skip_btn.pressed.is_connected(Callable(turn_node, "_on_skip_pressed")), "relocated playback buttons keep their actions")


	# Busy/submitting during presentation disables SkipPresentationButton as well.
	present(view, snap, context_en, {"locked": true, "has_frames": true, "busy": true, "submitting": true})
	await settle()
	check(skip_btn.disabled and end_btn.disabled, "busy/submitting state disables both SkipPresentationButton and EndTurnButton")

	# Unpublished END_TURN disables EndTurnButton even when unlocked.
	var snap_no_end: Dictionary = snap.duplicate(true)
	snap_no_end["actions"] = []
	present(view, snap_no_end, context_en, {"locked": false, "has_frames": false, "selected_card": ""})
	await settle()
	check(end_btn.disabled, "EndTurnButton is disabled when END_TURN is not in published legal actions")

	# Remapped shortcuts update button text and tooltip in EN and PT-BR.
	i18n.set_locale("pt_BR")
	var context_remapped = Context.new(i18n, {"text_scale": 1.0, "reduced_motion": true, "action_labels": {"end_turn": "R", "confirm_action": "G", "pause_game": "P", "inspect_card": "Q"}}, profile)
	present(view, snap, context_remapped, {"locked": false, "has_frames": true, "selected_card": ""})
	await settle()
	check(end_btn.text == "ENCERRAR TURNO" and end_btn.tooltip_text == "ENCERRAR TURNO", "localized EndTurnButton omits the remapped shortcut")
	check(next_btn.tooltip_text.contains("[G]"), "remapped confirm_action shortcut updates NextAnimation tooltip in pt_BR")
	check(energy_node.text == "◇  Energia  3 / 3", "EnergyIndicator localizes resource name in pt_BR")

	# 4. Multi-resolution, locale, and text_scale geometry checks (1920x1080, 1280x720, 1280x800, 2560x1080).
	for locale in ["en", "pt_BR"]:
		i18n.set_locale(locale)
		for scale in [1.0, 1.6]:
			var ctx = Context.new(i18n, {"text_scale": scale, "reduced_motion": true, "action_labels": {"end_turn": "E", "confirm_action": "F", "pause_game": "Escape", "inspect_card": "V"}}, profile)
			for resolution in [Vector2i(1920, 1080), Vector2i(1280, 720), Vector2i(1280, 800), Vector2i(2560, 1080)]:
				root.size = resolution
				view.size = Vector2(resolution)
				present(view, snap, ctx, {"locked": false, "has_frames": false, "selected_card": ""})
				await settle()
				var tag := "%s scale=%s res=%s" % [locale, scale, resolution]
				var canvas_rect: Rect2 = canvas.get_global_rect()
				var energy_rect: Rect2 = energy_node.get_global_rect()
				var hand_rect: Rect2 = hand_host.get_global_rect()
				var turn_rect: Rect2 = turn_node.get_global_rect()
				var end_rect: Rect2 = end_btn.get_global_rect()
				var actors_rect: Rect2 = view.battlefield_actors.get_global_rect()
				var first_card_top: float = view.hand.scroll.get_global_rect().position.y + view.hand.visual_style.padding
				var non_hover_card_top: float = first_card_top + view.hand.visual_style.lift
				check(absf(energy_rect.get_center().x - (canvas_rect.position.x + view.battlefield_axis_x())) <= 2.0, "EnergyIndicator follows the battlefield axis: " + tag)
				var round_center: float = view.header_view.get_node("RoundAndTurn").get_global_rect().get_center().x
				check(absf(round_center - energy_rect.get_center().x) <= 2.0, "round and energy share the same axis: " + tag)
				var allies: Rect2 = view.battlefield_view.allies.get_global_rect()
				var opponents: Rect2 = view.battlefield_view.opponents.get_global_rect()
				var gap_center := (allies.end.x + opponents.position.x) * 0.5
				check(absf(round_center - gap_center) <= 2.0, "shared axis bisects the gap between teams: " + tag)
				check(energy_rect.end.y <= non_hover_card_top + 1.0, "EnergyIndicator sits above hand cards: " + tag)
				check(actors_rect.end.y <= first_card_top + 2.0, "battlefield actors stay above raised hand cards: " + tag)
				check(turn_rect.position.x >= hand_rect.end.x - 1.0, "EndTurnAndContextActions stays to the right of HandHost without overlap: " + tag)
				check(canvas_rect.encloses(end_rect) and end_rect.end.x <= canvas_rect.end.x - 4.0 and end_rect.end.y <= canvas_rect.end.y - 6.0, "EndTurnButton is isolated inside bottom-right corner with clean inset: " + tag)

	view.queue_free()
	await settle(4)

	# 5. Full router + CombatScreen integration: remapped key execution, Esc priority (cancel selection vs pause), pause during resolution, and hover/selection of rightmost cards near EndTurnButton.
	i18n.set_locale("en")
	prefs.text_scale = 1.0
	prefs.reduced_motion = true
	var fixture := EnergyTurnFixture.new()
	session.configure(fixture)
	session.synchronized = true
	session._run = {"settingId": "prototype-test", "runId": "stage5-run", "sequence": 1, "playerEntityId": "hero"}
	session._character_attributes = {"settingId": "prototype-test", "runId": "stage5-run", "runSequence": 1, "actorId": "hero", "attributes": [], "criticalRules": []}
	session._combat = {"combatId": "stage5-combat", "activeActorId": "hero", "activation": {
		"round": 1, "activeActorId": "hero", "waitingForInput": true,
		"intents": [{"actorId": "wisp", "actionId": "Attack"}]},
		"relationships": {"sameSide": "Ally", "differentSides": "Enemy"},
		"actors": [
			{"instanceId": "hero", "definitionId": "spire_adept", "sideId": "player", "resources": {"health": {"current": 60, "minimum": 0, "maximum": 72}, "energy": {"current": 4, "minimum": 0, "maximum": 3}, "block": {"current": 6}}, "statuses": []},
			{"instanceId": "wisp", "definitionId": "spire_wisp", "sideId": "enemy", "resources": {"health": {"current": 30, "minimum": 0, "maximum": 40}}, "statuses": []}
		]}
	var hand_cards: Array = []
	session._legal_actions = [{"source": "System", "command": {"actionType": "END_TURN"}}]
	for i in 8:
		var cid := "stage5-card-%s" % i
		hand_cards.append({"cardInstanceId": cid, "definitionId": ["basic_attack", "defend", "fireball", "heal", "arcane_bolt"][i % 5]})
		session._legal_actions.append({"source": "Card", "command": {"cardInstanceId": cid, "actionType": "PLAY_CARD", "targetIds": ["wisp"]}, "costs": [{"resourceId": "energy", "amount": 1}]})
	session._card_zones = {"zones": [
		{"zoneId": "hand", "allowsCardPlay": true, "contentsVisible": true, "cards": hand_cards, "count": hand_cards.size()},
		{"zoneId": "draw", "ownerId": "hero", "scopeId": "stage5-combat", "count": 10, "presentation": {"slot": "draw_pile", "labelKey": "Draw pile"}},
		{"zoneId": "discard", "ownerId": "hero", "scopeId": "stage5-combat", "count": 2, "presentation": {"slot": "discard_pile", "labelKey": "Discard pile"}}
	]}
	fixture.combat_snapshot = session._combat.duplicate(true)
	fixture.zones_snapshot = session._card_zones.duplicate(true)
	fixture.actions_snapshot = session._legal_actions.duplicate(true)

	root.size = Vector2i(1920, 1080)
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	router.show_combat()
	await settle()
	var screen = router.host.get_child(0)
	var mvp_view = screen.view
	var hand = mvp_view.hand
	var live_end_btn: Button = mvp_view.turn_view.get_node("EndTurnButton")
	var live_energy: Label = mvp_view.energy_indicator

	check(live_energy.visible and live_energy.text == "◇  Energy  4 / 3", "live combat screen presents surplus energy above the hand")
	var expected_card_size: Vector2 = mvp_view.visual_context.card_style.effective_size(mvp_view.visual_context.text_scale)
	var baseline_card_size: Vector2 = hand.ordered_cards()[0].size
	check(baseline_card_size.is_equal_approx(expected_card_size) and hand.ordered_cards()[0].get_global_transform().get_scale().is_equal_approx(Vector2.ONE * 0.67), "MVP hand cards are 33% smaller")
	var host: Control = mvp_view.get_node("CanvasHost")
	check(absf(mvp_view.bottom_hud.get_global_rect().end.y - host.get_global_rect().end.y) <= 1.0, "hand dock reaches the bottom edge of the screen")

	# Hover and select the rightmost card in hand (near EndTurnButton) and verify no overlap or hit-test interference.
	var rightmost_index: int = hand.order.size() - 1
	var rightmost_id: String = hand.order[rightmost_index]
	var rightmost_point: Vector2 = hand.scroll.global_position + hand._layout.hitRects[rightmost_index].get_center() - Vector2(hand.scroll.scroll_horizontal, 0)
	check(not mvp_view.turn_view.get_global_rect().has_point(rightmost_point), "rightmost card hit center does not intersect EndTurnAndContextActions")
	motion(rightmost_point)
	await settle(6)
	var rightmost_card: Control = hand.card_for(rightmost_id)
	check(hand.interaction.hovered == rightmost_id and hand._slots[rightmost_id].z_index == 100, "hovering rightmost card near EndTurnButton raises it cleanly")
	check(not rightmost_card.get_global_rect().intersects(live_end_btn.get_global_rect()), "raised rightmost card does not collide with EndTurnButton")
	check(not rightmost_card.get_global_rect().intersects(live_energy.get_global_rect()), "raised rightmost card does not collide with EnergyIndicator")

	click(rightmost_point, true)
	click(rightmost_point, false)
	await settle(6)
	check(screen.selected_card == rightmost_id and mvp_view.turn_view.selection_actions.visible, "clicking rightmost card selects it and shows CancelSelection")

	# Escape priority 1: while a card is selected, pressing Escape cancels selection first without opening Pause.
	press_key(KEY_ESCAPE)
	await settle(6)
	check(screen.selected_card.is_empty() and not paused and router.pause_layer == null, "first Escape cancels active card selection without opening Pause")

	# Escape priority 2: with no card selected, pressing Escape opens Pause.
	press_key(KEY_ESCAPE)
	await settle(6)
	check(paused and router.pause_layer != null, "second Escape with no selection opens Pause overlay")
	router.toggle_pause()
	await settle(6)
	check(not paused and router.pause_layer == null, "closing Pause returns to active combat")

	# Remap end_turn to KEY_R and verify button label updates and pressing R triggers END_TURN submission.
	check(prefs.remap_event("end_turn", _key_event(KEY_R), false) == "", "remapping end_turn to KEY_R succeeds")
	screen.refresh_state(router.presentation)
	await settle(6)
	check(not live_end_btn.text.contains("[R]"), "live EndTurnButton omits the remapped shortcut")
	var intents: Array = []
	mvp_view.intent_requested.connect(func(action, _args): intents.append(action))
	press_key(KEY_R)
	await settle(6)
	check("_end_turn" in intents and not fixture.submissions.is_empty(), "pressing remapped end_turn key emits _end_turn intent and submits command")

	# Pause during resolution / presentation frames: verify NextAnimation & Skip are shown, and Escape pauses cleanly during resolution.
	mvp_view.apply_interaction({"synchronized": true, "locked": true, "has_frames": true, "remaining_frames": 3, "selected_card": ""})
	await settle(4)
	check(mvp_view.turn_view.next_frame_button.visible and mvp_view.turn_view.skip_button.visible and live_end_btn.disabled, "during frame resolution NextAnimation and SkipPresentationButton are visible while EndTurnButton is locked")
	press_key(KEY_ESCAPE)
	await settle(6)
	check(paused and router.pause_layer != null and router.pause_canvas.layer > 0, "pressing Escape during frame resolution opens Pause modal above combat")
	router.toggle_pause()
	await settle(6)

	# Restore state and preferences.
	prefs._set_action_key("end_turn", prior.end_key)
	prefs._set_action_key("confirm_action", prior.confirm_key)
	prefs._set_action_key("pause_game", prior.pause_key)
	prefs.text_scale = prior.scale
	prefs.reduced_motion = prior.motion
	i18n.set_locale(prior.locale)
	session._gateway = prior.gateway
	session._run = prior.run
	session._combat = prior.combat
	session._card_zones = prior.zones
	session._legal_actions = prior.actions
	session._character_attributes = prior.attributes
	playback.clear()
	check(profile.presentation_key() == profile_before, "Stage 5 never mutates mvp_combat_theme.tres")

	router.queue_free()
	await settle(4)
	root.get_node("GameAudio").shutdown()
	await create_timer(0.05).timeout
	quit(1 if failures else 0)
