extends SceneTree
## Stage 6 verification: floating windows and overlays aligned to the new MVP ReferenceCanvas composition.
## Validates CombatAttributesDrawer, InspectionDialog, CardZoneDialog (including collection reopen & nested card detail),
## CombatLog, CandidatePanel (multi-option play & inline card payment), Reconnect button, Toast notifications,
## Pause layer superiority, and Escape priority chain without displacing any gameplay region.
const Context = preload("res://scripts/ui/visual_context.gd")
var failures := 0

class OverlayFixture extends RefCounted:
	var reconnects := 0
	var inspections := 0
	var collection_requests := 0
	var card_inspections := 0
	var payment_previews := 0
	var submissions: Array = []
	var run_snapshot: Dictionary = {}
	var combat_snapshot: Dictionary = {}
	var zones_snapshot: Dictionary = {}
	var actions_snapshot: Array = []
	var attributes_snapshot: Dictionary = {}

	func connect_engine() -> Dictionary:
		return {"ok": false, "status": 503, "error": "Offline overlay fixture"}

	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		inspections += 1
		return {"ok": true, "data": {"cards": [{
			"version": {
				"runId": str(run_snapshot.get("runId", "stage6-run")),
				"combatId": str(combat_snapshot.get("combatId", "stage6-combat")),
				"runSequence": int(run_snapshot.get("sequence", 1)),
				"combatStep": int(combat_snapshot.get("step", 0)),
				"contentRevision": str(run_snapshot.get("contentRevision", "rev"))
			},
			"evaluation": {
				"cardInstanceId": "fixture-4",
				"paymentRequirements": [{
					"componentId": "cost",
					"paymentId": "fuel",
					"count": 1,
					"sourceZoneId": "hand",
					"eligibleCardIds": ["fixture-1", "fixture-2"]
				}]
			}
		}]}}

	func read_card_presentations(_run_id: String) -> Dictionary:
		collection_requests += 1
		var cards: Array = []
		for zone in zones_snapshot.get("zones", []):
			for card in zone.get("cards", []):
				var cid := str(card.get("cardInstanceId", ""))
				cards.append({
					"identity": {"cardInstanceId": cid, "definitionId": str(card.get("definitionId", "basic_attack"))},
					"tags": ["Fire", "Attack"],
					"hasCardPayments": cid == "fixture-4",
					"costs": [{"resourceId": "energy", "amount": 1}],
					"effects": [],
					"conditions": []
				})
		return {"ok": true, "data": {
			"runId": str(run_snapshot.get("runId", "stage6-run")),
			"runSequence": int(run_snapshot.get("sequence", 1)),
			"contentRevision": str(run_snapshot.get("contentRevision", "rev")),
			"cards": cards
		}}

	func inspect_card(_combat_id: String, _actor_id: String, card_id: String) -> Dictionary:
		card_inspections += 1
		return {"ok": true, "data": {
			"cardInstanceId": card_id,
			"isInPlayableZone": false,
			"baseContainer": {"definitionId": "basic_attack", "tags": ["Fire", "Attack"]},
			"evaluation": {"costs": [{"resourceId": "energy", "amount": 1}]}
		}}

	func replay_context() -> Array:
		return ["stage6-api", "prototype-test"]

	func preview_card_payment(_combat_id: String, _run_sequence: int, _combat_step: int, command_data: Dictionary) -> Dictionary:
		payment_previews += 1
		var chosen_option := str(command_data.get("costOptionId", ""))
		var selections: Array = command_data.get("paymentSelections", [])
		var has_fee: bool = not selections.is_empty() and not (selections[0].get("cardInstanceIds", []) as Array).is_empty()
		return {"ok": true, "data": {
			"version": {
				"runId": str(run_snapshot.get("runId", "stage6-run")),
				"combatId": str(combat_snapshot.get("combatId", "stage6-combat")),
				"settingId": "prototype-test",
				"runSequence": int(run_snapshot.get("sequence", 1)),
				"combatStep": int(combat_snapshot.get("step", 0)),
				"engineVersion": "31",
				"contentRevision": str(run_snapshot.get("contentRevision", "rev")),
				"stateFingerprint": "hash"
			},
			"command": command_data.duplicate(true),
			"isLegal": not chosen_option.is_empty() and has_fee,
			"evaluation": {
				"legalTargetIds": ["wisp"],
				"affordableCostOptionIds": ["exile_spark", "sacrifice_ember"],
				"failureReasons": [] if (not chosen_option.is_empty() and has_fee) else ["Choose payment"],
				"paymentRequirements": [{
					"componentId": "cost",
					"paymentId": "fuel",
					"count": 1,
					"sourceZoneId": "hand",
					"costOptionId": chosen_option if not chosen_option.is_empty() else null,
					"eligibleCardIds": ["fixture-1", "fixture-2"]
				}],
				"costs": [{"resourceId": "energy", "amount": 1}]
			},
			"cards": [],
			"applications": []
		}}

	func candidate_payload(command_data: Dictionary) -> Dictionary:
		return command_data.duplicate(true)

	func prepare_command(scope: String, id: String, type: String, payload: Dictionary, sequence: int, expected_step: int) -> Dictionary:
		return {"scope": scope, "id": id, "type": type, "payload": payload.duplicate(true), "sequence": sequence, "expectedStep": expected_step}

	func send_prepared(pending: Dictionary) -> Dictionary:
		submissions.append(pending.duplicate(true))
		return {"ok": true, "data": {"sequence": 1, "state": {"resolution": null, "combat": combat_snapshot.duplicate(true)}}}

	func read_session(_id: String, _expected := -1) -> Dictionary:
		reconnects += 1
		var version := {
			"runId": str(run_snapshot.get("runId", "stage6-run")),
			"combatId": str(combat_snapshot.get("combatId", "stage6-combat")),
			"settingId": "prototype-test",
			"runSequence": int(run_snapshot.get("sequence", 1)),
			"runStep": int(run_snapshot.get("step", 0)),
			"combatStep": int(combat_snapshot.get("step", 0)),
			"engineVersion": "31",
			"contentRevision": str(run_snapshot.get("contentRevision", "rev")),
			"stateFingerprint": "hash"
		}
		var attrs := attributes_snapshot.duplicate(true)
		attrs["contentRevision"] = str(run_snapshot.get("contentRevision", "rev"))
		attrs["combatId"] = str(combat_snapshot.get("combatId", "stage6-combat"))
		attrs["combatStep"] = int(combat_snapshot.get("step", 0))
		return {"ok": true, "data": {"version": version,
			"run": run_snapshot.duplicate(true),
			"combat": combat_snapshot.duplicate(true),
			"commands": {"commands": []},
			"capabilities": {"granted": []},
			"cardZones": zones_snapshot.duplicate(true),
			"attributes": attrs,
			"cardPresentations": {},
			"modifierItems": {},
			"actions": {"candidates": actions_snapshot.duplicate(true)},
			"evaluations": {"cards": []}}}

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

func capture_layout(mvp_view) -> Dictionary:
	return {
		"canvas": mvp_view.gameplay_column.get_global_rect(),
		"header": mvp_view.header_view.get_global_rect(),
		"battlefield": mvp_view.battlefield_actors.get_global_rect(),
		"piles": mvp_view.pile_controls.get_global_rect(),
		"energy": mvp_view.energy_indicator.get_global_rect(),
		"hand_host": mvp_view.bottom_hud.get_global_rect(),
		"hand": mvp_view.hand.get_global_rect(),
		"end_turn": mvp_view.turn_view.get_node("EndTurnButton").get_global_rect()
	}

func same_layout(before: Dictionary, after: Dictionary) -> bool:
	for key in before.keys():
		if not before[key].is_equal_approx(after[key]):
			return false
	return true

func _actor(id: String, definition: String, side: String) -> Dictionary:
	return {"instanceId": id, "definitionId": definition, "sideId": side, "resources": {
		"health": {"current": 54, "minimum": 0, "maximum": 72}, "block": {"current": 12, "maximum": 72},
		"energy": {"current": 3, "maximum": 3}, "mana": {"current": 4, "maximum": 6}}, "statuses": [{"statusId": "burn", "stacks": 2}]}

func run() -> void:
	var prefs = root.get_node("Preferences")
	var i18n = root.get_node("I18n")
	var session = root.get_node("GameSession")
	var playback = root.get_node("Playback")
	var prior := {"locale": i18n.locale, "scale": prefs.text_scale, "motion": prefs.reduced_motion,
		"run": session._run.duplicate(true), "combat": session._combat.duplicate(true), "zones": session._card_zones.duplicate(true),
		"actions": session._legal_actions.duplicate(true), "gateway": session._gateway, "attributes": session._character_attributes.duplicate(true)}
	var profile = load("res://data/themes/mvp_combat_theme.tres")
	var profile_before: String = profile.presentation_key()

	i18n.set_locale("en")
	prefs.text_scale = 1.0
	prefs.reduced_motion = true

	var fixture := OverlayFixture.new()
	session.configure(fixture)
	session.synchronized = true
	session.content_revision = "rev"
	session._run = {"settingId": "prototype-test", "runId": "stage6-run", "sequence": 1, "step": 0, "playerEntityId": "hero", "lifecycle": "Active", "engineVersion": "31", "contentRevision": "rev", "stateHash": "hash"}
	session._character_attributes = {"settingId": "prototype-test", "runId": "stage6-run", "runSequence": 1, "actorId": "hero", "attributes": [
		{"attributeId": "armor", "displayName": "Armor", "unit": "points", "baseValue": 10, "effectiveValue": 25},
		{"attributeId": "fire_power", "displayName": "Fire Power", "unit": "percent", "baseValue": 0, "effectiveValue": 30}
	], "criticalRules": []}
	session._combat = {"combatId": "stage6-combat", "step": 0, "activeActorId": "hero", "activation": {
		"round": 2, "activeActorId": "hero", "waitingForInput": true,
		"intents": [{"actorId": "wisp", "actionId": "Attack"}, {"actorId": "sentinel", "actionId": "Defend"}]},
		"relationships": {"sameSide": "Ally", "differentSides": "Enemy"},
		"actors": [_actor("hero", "spire_adept", "player"), _actor("wisp", "spire_wisp", "enemy"), _actor("sentinel", "spire_sentinel", "enemy")]}

	var hand_cards: Array = []
	session._legal_actions = [{"source": "System", "command": {"actionType": "END_TURN"}}]
	for i in 5:
		var cid := "fixture-%s" % i
		hand_cards.append({"cardInstanceId": cid, "definitionId": ["basic_attack", "defend", "fireball", "heal", "arcane_bolt"][i % 5]})
		session._legal_actions.append({"source": "Card", "command": {"cardInstanceId": cid, "actionType": "PLAY_CARD", "targetIds": ["wisp"]}, "costs": [{"resourceId": "energy", "amount": 1}]})
		session._legal_actions.append({"source": "Card", "command": {"cardInstanceId": cid, "actionType": "PLAY_CARD", "targetIds": ["wisp"], "costOptionId": "empower"}, "costs": [{"resourceId": "mana", "amount": 2}]})
	# Make fixture-4 require inline card payment so we can test both multi-option targeting and inline card payment.
	session._legal_actions.append({"source": "Card", "command": {"cardInstanceId": "fixture-4", "actionType": "PLAY_CARD", "targetIds": []}, "requiresCardPayment": true, "costs": [{"resourceId": "energy", "amount": 1}]})

	var draw_cards: Array = []
	for i in 8:
		draw_cards.append({"cardInstanceId": "draw-%s" % i, "definitionId": "fireball" if i % 2 == 0 else "defend", "upgrades": []})
	var discard_cards: Array = [
		{"cardInstanceId": "disc-0", "definitionId": "basic_attack", "upgrades": []},
		{"cardInstanceId": "disc-1", "definitionId": "heal", "upgrades": []}
	]
	session._card_zones = {"zones": [
		{"zoneId": "hand", "allowsCardPlay": true, "contentsVisible": true, "cards": hand_cards, "count": hand_cards.size()},
		{"zoneId": "draw", "ownerId": "hero", "scopeId": "stage6-combat", "contentsVisible": true, "orderVisible": false, "cards": draw_cards, "count": draw_cards.size(), "presentation": {"slot": "draw_pile", "labelKey": "Draw pile"}},
		{"zoneId": "banish", "ownerId": "hero", "scopeId": "stage6-combat", "contentsVisible": true, "orderVisible": true, "cards": [{"cardInstanceId": "exile-0", "definitionId": "arcane_bolt", "upgrades": []}], "count": 1, "presentation": {"slot": "exile_pile", "labelKey": "Exile"}},
		{"zoneId": "discard", "ownerId": "hero", "scopeId": "stage6-combat", "contentsVisible": true, "orderVisible": true, "cards": discard_cards, "count": discard_cards.size(), "presentation": {"slot": "discard_pile", "labelKey": "Discard pile"}},
		{"zoneId": "owned", "ownerId": "$run", "scopeId": null, "contentsVisible": true, "orderVisible": true, "cards": draw_cards.duplicate(true), "count": draw_cards.size(), "presentation": {"slot": "owned_deck", "displayName": "Collection / reserve"}}
	]}
	fixture.run_snapshot = session._run.duplicate(true)
	fixture.combat_snapshot = session._combat.duplicate(true)
	fixture.zones_snapshot = session._card_zones.duplicate(true)
	fixture.actions_snapshot = session._legal_actions.duplicate(true)
	fixture.attributes_snapshot = session._character_attributes.duplicate(true)

	root.size = Vector2i(1920, 1080)
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	router.show_combat()
	await settle()

	var screen = router.host.get_child(0)
	var mvp_view = screen.view
	var hand = mvp_view.hand
	var drawer: CombatAttributesDrawer = mvp_view.character_sidebar
	var candidate_panel: PanelContainer = mvp_view.candidate_panel
	var reconnect_btn: Button = mvp_view.reconnect_button
	var log_dialog: AcceptDialog = mvp_view.get_node("CombatLog")

	# 1. Verify overlay layer hierarchy and initial non-intrusive state.
	var baseline := capture_layout(mvp_view)
	check(mvp_view.floating_hud.layer == 5, "FloatingCombatHUD is on layer 5 above gameplay (layer 0) and below modal pause (layer 10)")
	check(drawer.get_parent() == mvp_view.floating_hud.host and candidate_panel.get_parent() == mvp_view.floating_hud.host and reconnect_btn.get_parent() == mvp_view.floating_hud.host,
		"drawer, candidate_panel, and reconnect_button live inside FloatingHUDHost")
	check(not drawer.visible and not candidate_panel.visible and not reconnect_btn.visible and not log_dialog.visible,
		"all floating overlays and dialogs start closed when synchronized and idle")

	# 2. CombatAttributesDrawer: focus restoration, left-edge hover above vs over PileRail, I shortcut, and Escape priority.
	var first_card: Control = hand.card_for("fixture-0")
	first_card.grab_focus()
	await settle(4)
	check(first_card.has_focus(), "first hand card owns focus before opening drawer")

	press_key(KEY_I)
	await settle(6)
	check(drawer.expanded and drawer.visible and is_equal_approx(drawer.global_position.x, 0.0), "pressing I opens CombatAttributesDrawer aligned to left edge")
	check(same_layout(baseline, capture_layout(mvp_view)), "opening CombatAttributesDrawer does not move any gameplay region")

	# Move focus inside drawer, then press Escape to close drawer -> verify Pause does not open and focus returns to first_card.
	drawer.toggle_button.grab_focus()
	await settle(2)
	check(drawer.toggle_button.has_focus(), "drawer toggle button can receive focus while open")
	press_key(KEY_ESCAPE)
	await settle(6)
	check(not drawer.expanded and not drawer.visible and not paused and router.pause_layer == null, "Escape closes CombatAttributesDrawer before opening Pause")
	check(first_card.has_focus(), "closing CombatAttributesDrawer restores focus to the prior gameplay control")
	check(same_layout(baseline, capture_layout(mvp_view)), "closing CombatAttributesDrawer keeps all gameplay regions stationary")

	# Left-edge hover over PileRail must NOT open the drawer; left-edge hover above PileRail MUST open the drawer.
	var pile_rect: Rect2 = mvp_view.pile_controls.get_global_rect()
	motion(Vector2(120, pile_rect.position.y + 30))
	motion(Vector2(2, pile_rect.position.y + 30))
	await settle(4)
	check(not drawer.expanded and not drawer.visible, "left-edge pointer hover inside PileRail vertical zone does not open CombatAttributesDrawer")

	var upper_y := maxf(mvp_view.header_view.get_global_rect().end.y + 10.0, pile_rect.position.y - 40.0)
	motion(Vector2(120, upper_y))
	motion(Vector2(2, upper_y))
	await settle(4)
	check(drawer.expanded and drawer.visible, "left-edge pointer hover above PileRail opens CombatAttributesDrawer")
	motion(Vector2(900, upper_y))
	await settle(4)
	check(not drawer.expanded and not drawer.visible, "leaving hover-opened CombatAttributesDrawer closes it cleanly")

	# 3. Card Inspection (InspectionDialog): via HeaderActions/InspectButton and shortcut V, reading_modal lock, Escape close, focus restoration.
	first_card.grab_focus()
	await settle(4)
	check(mvp_view.header_view.inspect_button.visible and not mvp_view.header_view.inspect_button.disabled, "Header InspectButton is enabled when a hand card is active")
	mvp_view.header_view.inspect_button.pressed.emit()
	await settle(6)
	var inspect_dialog: Window = null
	for child in mvp_view.get_children():
		if child is AcceptDialog and child != log_dialog:
			inspect_dialog = child
			break
	check(is_instance_valid(inspect_dialog) and inspect_dialog.visible, "clicking Header InspectButton opens InspectionDialog")
	check(hand.reading_modal, "InspectionDialog sets hand.reading_modal to true")
	check(same_layout(baseline, capture_layout(mvp_view)), "opening InspectionDialog does not displace any gameplay region")
	var vp_rect: Rect2 = root.get_visible_rect()
	check(inspect_dialog.position.x >= 0 and inspect_dialog.position.y >= 0 and inspect_dialog.position.x + inspect_dialog.size.x <= vp_rect.size.x and inspect_dialog.position.y + inspect_dialog.size.y <= vp_rect.size.y,
		"InspectionDialog fits within the viewport bounds")

	# While InspectionDialog is open, pressing I must not open the drawer.
	mvp_view._input(_action_key(KEY_I))
	await settle(2)
	check(not drawer.expanded, "reading_modal blocks CombatAttributesDrawer shortcut I while InspectionDialog is open")

	# Closing InspectionDialog restores focus to the inspected card and clears reading_modal without opening Pause.
	inspect_dialog.canceled.emit()
	await settle(8)
	check(not is_instance_valid(inspect_dialog) and not hand.reading_modal and not paused, "closing InspectionDialog clears reading_modal without opening Pause")
	check(first_card.has_focus(), "closing InspectionDialog restores focus to the inspected card")

	# Also verify shortcut V (inspect_card) opens InspectionDialog.
	press_key(KEY_V)
	await settle(6)
	for child in mvp_view.get_children():
		if child is AcceptDialog and child != log_dialog:
			inspect_dialog = child
			break
	check(is_instance_valid(inspect_dialog) and inspect_dialog.visible and hand.reading_modal, "pressing V opens InspectionDialog for the active hand card")
	inspect_dialog.confirmed.emit()
	await settle(8)
	check(not hand.reading_modal and first_card.has_focus(), "confirming InspectionDialog closes it and restores card focus")

	# 4. Collection Modal (CardZoneDialog): open from PileRail, search/filter state preservation on reopen, nested card detail inspection, and focus restoration.
	var draw_btn: Button = null
	for btn in mvp_view.pile_buttons():
		if str((btn.get_meta("zone_address", {}) as Dictionary).get("zoneId", "")) == "draw":
			draw_btn = btn
			break
	check(is_instance_valid(draw_btn) and draw_btn.visible, "Draw pile button is visible in PileRail")
	draw_btn.grab_focus()
	draw_btn.pressed.emit()
	await settle(10)
	var col_dialog: Window = screen._collection_dialog
	check(is_instance_valid(col_dialog) and col_dialog.visible and hand.reading_modal, "clicking Pile_draw opens CardZoneDialog and sets reading_modal")
	check(same_layout(baseline, capture_layout(mvp_view)), "opening CardZoneDialog does not move any gameplay region")
	check(col_dialog.size.x <= vp_rect.size.x and col_dialog.size.y <= vp_rect.size.y, "CardZoneDialog clamps cleanly within viewport")

	# Filter inside collection, then close and reopen to verify local reading state is preserved.
	var search_term: String = str(col_dialog.collection.cards.values()[0].model.get("name", "defend")).to_lower()
	var expected_matches := 0
	for card_node in col_dialog.collection.cards.values():
		if str(card_node.model.get("name", "")).to_lower().contains(search_term):
			expected_matches += 1
	col_dialog.collection._search.text = search_term
	col_dialog.collection._filter()
	await settle(4)
	var visible_filtered := 0
	for card_node in col_dialog.collection.cards.values():
		if card_node.visible: visible_filtered += 1
	check(visible_filtered == expected_matches and visible_filtered > 0 and visible_filtered < col_dialog.collection.cards.size(), "collection search filters draw pile cards locally")
	col_dialog.confirmed.emit()
	await settle(8)
	check(not is_instance_valid(screen._collection_dialog) and not hand.reading_modal, "closing CardZoneDialog clears reading_modal")
	check(draw_btn.has_focus(), "closing CardZoneDialog restores focus to the triggering Pile_draw button")

	# Reopen Pile_draw and verify search state was restored, plus test nested card detail inspection.
	draw_btn.pressed.emit()
	await settle(10)
	col_dialog = screen._collection_dialog
	check(is_instance_valid(col_dialog) and col_dialog.visible and col_dialog.collection._search.text == search_term, "reopening the same pile restores its search/reading state")

	var first_col_card_id: String = col_dialog.collection.cards.keys()[0]
	col_dialog.collection.cards[first_col_card_id].pressed.emit()
	await settle(10)
	check(is_instance_valid(col_dialog.detail) and col_dialog.detail.visible and fixture.card_inspections >= 1, "clicking a card inside CardZoneDialog opens nested card detail dialog")
	check(same_layout(baseline, capture_layout(mvp_view)), "nested card detail inside CardZoneDialog does not move any gameplay region")
	col_dialog.detail.confirmed.emit()
	await settle(8)
	check(not is_instance_valid(col_dialog.detail) and col_dialog.visible and col_dialog.collection.cards[first_col_card_id].has_focus(), "closing nested detail returns focus to the collection card while keeping CardZoneDialog open")
	col_dialog.canceled.emit()
	await settle(8)
	check(not hand.reading_modal and draw_btn.has_focus(), "closing CardZoneDialog after nested inspection restores focus to Pile_draw")

	# 5. CombatLog: open via HeaderActions/LogButton, reading_modal tracking, event text rendering, Escape/close focus restoration.
	mvp_view.present_frame({"type": "EffectApplied"}, 0, 1)
	mvp_view._log_entries.append("Hero dealt 12 Fire damage to Spire Wisp.")
	var log_btn: Button = mvp_view.header_view.log_button
	log_btn.grab_focus()
	log_btn.pressed.emit()
	await settle(6)
	check(log_dialog.visible and hand.reading_modal, "clicking Header LogButton opens CombatLog and tracks it as a reading modal")
	check(log_dialog.get_node("Scroll/Events").text.contains("Hero dealt 12 Fire damage"), "CombatLog displays recorded combat events")
	check(log_dialog.ok_button_text == mvp_view.visual_context.translator.text("CLOSE"), "CombatLog uses localized Close button text")
	check(same_layout(baseline, capture_layout(mvp_view)), "opening CombatLog does not displace any gameplay region")
	log_dialog.hide()
	await settle(6)
	check(not log_dialog.visible and not hand.reading_modal and log_btn.has_focus(), "closing CombatLog clears reading_modal and restores focus to LogButton")

	# 6. CandidatePanel (multi-option card play & inline card payment) & Escape priority.
	screen._choose_card("fixture-0")
	check(screen.selected_card == "fixture-0", "selecting fixture-0 enters targeting mode")
	screen._select_target("wisp")
	await settle(6)
	check(candidate_panel.visible and mvp_view.choices.get_child_count() == 2, "targeting wisp with multi-option card opens CandidatePanel with 2 cost choices")
	var cand_rect: Rect2 = candidate_panel.get_global_rect()
	var current_layout := capture_layout(mvp_view)
	check(same_layout(baseline, current_layout), "showing CandidatePanel does not move any gameplay region")
	check(current_layout.canvas.encloses(cand_rect), "CandidatePanel fits inside ReferenceCanvas")
	check(not cand_rect.intersects(current_layout.piles), "CandidatePanel does not overlap PileRail")
	check(not cand_rect.intersects(mvp_view.turn_view.get_global_rect()), "CandidatePanel does not overlap EndTurnAndContextActions")
	check(cand_rect.end.y <= current_layout.energy.position.y + 1.0, "CandidatePanel sits cleanly above EnergyIndicator and HandHost")

	# Pressing Escape cancels multi-option card selection and hides CandidatePanel before opening Pause.
	press_key(KEY_ESCAPE)
	await settle(6)
	check(screen.selected_card.is_empty() and not candidate_panel.visible and not paused and router.pause_layer == null,
		"Escape cancels multi-option card selection and hides CandidatePanel without opening Pause")

	# Now test inline card payment (fixture-4 requires card payment with 2 affordable cost options and eligible fee cards).
	screen._choose_card("fixture-4")
	await settle(8)
	check(screen.inline_selection.active and screen.selected_card == "fixture-4" and fixture.payment_previews == 1,
		"choosing card with requiresCardPayment starts inline card selection")
	check(candidate_panel.visible and mvp_view.choices.get_child_count() == 2,
		"inline selection with multiple affordable cost options displays cost choices in CandidatePanel")
	check(bool(hand.card_for("fixture-1").get_meta("card_selection_eligible", false)) and bool(hand.card_for("fixture-2").get_meta("card_selection_eligible", false)),
		"eligible fee cards in hand are highlighted during inline card payment")
	check(same_layout(baseline, capture_layout(mvp_view)), "inline card payment mode does not displace any gameplay region")

	# Pick a cost option in CandidatePanel, then pick a fee card, then cancel with Escape before completion.
	var first_cost_btn: Button = mvp_view.choices.get_child(0)
	first_cost_btn.pressed.emit()
	await settle(6)
	check(str(screen.inline_selection.command.get("costOptionId", "")) == "exile_spark" and not candidate_panel.visible,
		"choosing a cost option in CandidatePanel records costOptionId and hides cost option buttons")

	press_key(KEY_ESCAPE)
	await settle(6)
	check(not screen.inline_selection.active and screen.selected_card.is_empty() and not paused and router.pause_layer == null,
		"Escape cancels active inline card payment before opening Pause")
	check(not bool(hand.card_for("fixture-1").get_meta("card_selection_eligible", false)),
		"canceling inline card payment clears eligible card highlights on hand cards")

	# 7. Reconnect button & Toast notification positioning and recovery.
	session.synchronized = false
	screen._update_controls()
	await settle(6)
	check(reconnect_btn.visible, "unsynchronized session reveals Reconnect button on FloatingHUDHost")
	var rec_rect: Rect2 = reconnect_btn.get_global_rect()
	var header_identity_rect: Rect2 = mvp_view.header_view.get_node("ActiveIdentity").get_global_rect()
	check(same_layout(baseline, capture_layout(mvp_view)), "showing Reconnect button does not move any gameplay region")
	check(baseline.canvas.encloses(rec_rect), "Reconnect button stays inside ReferenceCanvas bounds")
	check(not rec_rect.intersects(header_identity_rect) and rec_rect.position.y >= baseline.header.end.y,
		"Reconnect button sits below CombatHeader and never covers ActiveIdentity")
	check(not rec_rect.intersects(baseline.piles) and not rec_rect.intersects(baseline.hand_host) and not rec_rect.intersects(mvp_view.turn_view.get_global_rect()),
		"Reconnect button does not cover PileRail, HandHost, or EndTurnAndContextActions")

	# Trigger Toast error notification and verify layer and non-displacement.
	router.show_error("Connection lost; please reconnect.")
	await settle(4)
	check(router.toast.visible and router.toast.get_parent().layer == 20, "Toast notification appears on layer 20 at top-center")
	check(same_layout(baseline, capture_layout(mvp_view)), "Toast notification does not displace any gameplay region")

	# Click Reconnect button and verify recovery.
	var reconnects_before := fixture.reconnects
	reconnect_btn.pressed.emit()
	await settle(10)
	check(fixture.reconnects > reconnects_before and session.synchronized and not reconnect_btn.visible,
		"clicking Reconnect refreshes the session and hides Reconnect button once synchronized")

	# 8. Multi-resolution geometry verification for overlays (1920x1080, 1280x720, 1280x800, 2560x1080) + Pause superiority.
	for resolution in [Vector2i(1920, 1080), Vector2i(1280, 720), Vector2i(1280, 800), Vector2i(2560, 1080)]:
		root.size = resolution
		await settle(10)
		var res_tag := str(resolution)
		var res_base := capture_layout(mvp_view)

		# Show drawer, candidate_panel, and reconnect_button to verify bounds at every resolution.
		drawer.set_open(true)
		session.synchronized = false
		screen._choose_card("fixture-0")
		screen._select_target("wisp")
		screen._update_controls()
		await settle(6)

		var d_rect: Rect2 = drawer.get_global_rect()
		var c_rect: Rect2 = candidate_panel.get_global_rect()
		var r_rect: Rect2 = reconnect_btn.get_global_rect()
		check(is_equal_approx(d_rect.position.x, 0.0) and d_rect.size.y <= root.get_visible_rect().size.y,
			"drawer aligns to left edge and fits viewport height at " + res_tag)
		check(res_base.canvas.encloses(c_rect) and not c_rect.intersects(res_base.piles) and not c_rect.intersects(mvp_view.turn_view.get_global_rect()) and c_rect.end.y <= res_base.energy.position.y + 2.0,
			"CandidatePanel stays inside ReferenceCanvas above EnergyIndicator without overlapping PileRail or EndTurn dock at " + res_tag)
		check(res_base.canvas.encloses(r_rect) and r_rect.position.y >= res_base.header.end.y and not r_rect.intersects(res_base.piles) and not r_rect.intersects(mvp_view.turn_view.get_global_rect()),
			"Reconnect button stays below CombatHeader at top-right of arena without overlapping piles or turn dock at " + res_tag)
		check(same_layout(res_base, capture_layout(mvp_view)),
			"simultaneous floating overlays never displace any gameplay region at " + res_tag)

		# Verify Pause layer (10) outranks FloatingCombatHUD (5) and raised cards (z_index = 100).
		router.toggle_pause()
		await settle(6)
		check(paused and is_instance_valid(router.pause_canvas) and router.pause_canvas.layer == 10 and router.pause_canvas.layer > mvp_view.floating_hud.layer,
			"PauseCanvas (layer 10) outranks FloatingCombatHUD (layer 5) and gameplay (layer 0) at " + res_tag)
		router.toggle_pause()
		await settle(6)

		# Clean up overlays for next resolution iteration.
		screen._cancel_selection()
		drawer.set_open(false)
		session.synchronized = true
		screen._update_controls()
		await settle(6)

	# Save optional visual capture if --lab-output is passed.
	if DisplayServer.get_name() != "headless":
		for argument in OS.get_cmdline_user_args():
			if argument.begins_with("--lab-output="):
				var out_dir := argument.trim_prefix("--lab-output=")
				root.size = Vector2i(1920, 1080)
				await settle(8)
				drawer.set_open(true)
				screen._choose_card("fixture-0")
				screen._select_target("wisp")
				await settle(6)
				await RenderingServer.frame_post_draw
				root.get_texture().get_image().save_png(out_dir.path_join("mvp-stage6-overlays-1920x1080.png"))
				screen._cancel_selection()
				drawer.set_open(false)
				await settle(4)

	# Restore preferences and session state.
	prefs.text_scale = prior.scale
	prefs.reduced_motion = prior.motion
	i18n.set_locale(prior.locale)
	session._gateway = prior.gateway
	session._run = prior.run
	session._combat = prior.combat
	session._card_zones = prior.zones
	session._legal_actions = prior.actions
	session._character_attributes = prior.attributes
	session.synchronized = true
	playback.clear()
	check(profile.presentation_key() == profile_before, "Stage 6 never mutates mvp_combat_theme.tres")

	router.queue_free()
	await settle(4)
	root.get_node("GameAudio").shutdown()
	await create_timer(0.05).timeout
	quit(1 if failures else 0)

func _action_key(keycode: int) -> InputEventKey:
	var ev := InputEventKey.new()
	ev.pressed = true
	ev.keycode = keycode
	ev.physical_keycode = keycode
	return ev
