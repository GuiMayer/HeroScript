extends SceneTree
## Real session/UI command flow with an injected gateway, no API or preference writes.
var failures := 0
var output_dir := ""
class GatewayFixture extends RefCounted:
	var consume := true
	var reject := false
	var state := {"runId": "selection-run", "sequence": 1, "activeEncounterId": "selection-combat"}
	var combat := {"combatId": "selection-combat", "status": "ACTIVE", "step": 1,
		"activeActorId": "hero", "actors": [{"instanceId": "hero", "sideId": "player", "resources": {}}]}
	var zones := {"zones": [{"zoneId": "arbitrary-play-zone", "allowsCardPlay": true, "contentsVisible": true,
		"count": 3, "cards": [{"cardInstanceId": "first", "definitionId": "basic_attack"},
			{"cardInstanceId": "second", "definitionId": "defend"}, {"cardInstanceId": "third", "definitionId": "fireball"}]}]}
	func candidate_payload(command: Dictionary) -> Dictionary: return command.duplicate(true)
	func prepare_command(_scope: String, _id: String, _type: String, payload: Dictionary, _sequence: int, _step: int) -> Dictionary:
		return {"payload": payload.duplicate(true)}
	func send_prepared(command: Dictionary) -> Dictionary:
		if reject: return {"ok": false, "status": 422, "error": "Rejected fixture command"}
		state.sequence += 1
		combat.step += 1
		if consume:
			zones.zones[0].cards = zones.zones[0].cards.filter(func(card): return card.cardInstanceId != command.payload.cardInstanceId)
			zones.zones[0].count = zones.zones[0].cards.size()
		return {"ok": true, "data": {"sequence": state.sequence, "state": {"combat": combat.duplicate(true),
			"resolution": {"frames": [{"transitionType": "effect", "applications": []}]}}}}
	func read_session(id: String, expected := -1) -> Dictionary:
		return await preload("res://tests/session_fixture_adapter.gd").read(self, id, expected)
	func read_run(_id: String) -> Dictionary: return {"ok": true, "data": state.duplicate(true)}
	func read_projections(_run: String, _encounter: String, include_combat: bool) -> Dictionary:
		var result := {"cardZones": {"ok": true, "data": zones.duplicate(true)},
			"commands": {"ok": true, "data": {"commands": []}}, "capabilities": {"ok": true, "data": {"granted": []}}}
		if include_combat: result["combat"] = {"ok": true, "data": combat.duplicate(true)}
		return result
	func read_legal_actions(_combat: String, _actor: String) -> Dictionary:
		return {"ok": true, "data": {"candidates": zones.zones[0].cards.map(func(card): return {
			"source": "Card", "command": {"cardInstanceId": card.cardInstanceId, "actionType": "PLAY_CARD", "targetIds": ["hero"]}})}}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		return {"ok": true, "data": {"cards": []}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 10: await process_frame
func capture(name: String) -> void:
	if output_dir.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output_dir.path_join(name + ".png")) == OK, "rendered focus outline: " + name)
func hover_card(hand, id: String) -> void:
	var card: Control = hand.card_for(id)
	for y in [card.size.y * .5, card.size.y - 20, 20.0]:
		for x in [card.size.x * .5, 20.0, card.size.x - 20]:
			var point: Vector2 = card.get_global_transform() * Vector2(x, y)
			if hand.pick_global(point, false) != id: continue
			var event := InputEventMouseMotion.new()
			event.position = point; event.global_position = point
			root.push_input(event, true)
			return
	check(false, "visible pointer surface exists for " + id)
func check_neutral(screen, label: String) -> void:
	check(screen.selected_card.is_empty() and screen.selected_target.is_empty() and screen.view.hand.interaction.selected.is_empty(), label + ": no selected card/target")
	check(screen.view.hand.interaction.active_id().is_empty() and screen.view.hand.active_card_id().is_empty(), label + ": no automatic hover, focus or inspection anchor")
	check(screen.view.hand.cards().values().all(func(card): return not card.chosen and not card.button_pressed and not card.has_focus()), label + ": no raised/focused/pressed card")
	var hand = screen.view.hand
	if not hand.order.is_empty():
		var left := INF; var right := -INF
		for id in hand.order:
			var card: Control = hand.card_for(id)
			for corner in [Vector2.ZERO, Vector2(card.size.x, 0), card.size, Vector2(0, card.size.y)]:
				var point: Vector2 = card.get_global_transform() * corner
				left = minf(left, point.x); right = maxf(right, point.x)
		var delta: float = (left + right) * .5 - screen.view.gameplay_column.get_global_rect().get_center().x
		check(absf(delta) <= 1.0, label + ": remaining hand centers on combat")
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	var session = root.get_node("GameSession")
	var prefs = root.get_node("Preferences")
	var playback = root.get_node("Playback")
	var prior := {"gateway": session._gateway, "run": session._run, "combat": session._combat,
		"zones": session._card_zones, "actions": session._legal_actions, "commands": session._available_commands,
		"capabilities": session._tool_capabilities, "synchronized": session.synchronized,
		"motion": prefs.reduced_motion, "auto": prefs.auto_animations}
	prefs.reduced_motion = true
	prefs.auto_animations = false
	var gateway := GatewayFixture.new()
	session.configure(gateway)
	session._run = gateway.state.duplicate(true)
	session._combat = gateway.combat.duplicate(true)
	session._card_zones = gateway.zones.duplicate(true)
	session._legal_actions = gateway.read_legal_actions("", "").data.candidates
	session.synchronized = true
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	router.show_combat()
	await settle()
	var screen = router.host.get_child(0)
	var hand = screen.view.hand
	check_neutral(screen, "combat entry")
	var survivor = hand.card_for("first")
	# Hover only cancels a previous choice; focus navigation must never cancel it.
	hand.mode_override = "reading"; hand.request_layout(); await settle()
	hand.card_for("first").grab_focus()
	screen._choose_card("first")
	screen.controller.target_options("hero")
	hover_card(hand, "first"); await settle()
	check(screen.selected_card == "first" and screen.selected_target == "hero", "hovering the selected card preserves targeting")
	await capture("selected-card")
	var padding_motion := InputEventMouseMotion.new()
	padding_motion.position = hand.hit_surface.global_position + Vector2(2, 2)
	padding_motion.global_position = padding_motion.position
	root.push_input(padding_motion, true); await settle()
	check(hand.interaction.hovered.is_empty() and screen.selected_card == "first", "empty hand padding does not cancel selection")
	hover_card(hand, "second"); await settle()
	check(screen.selected_card.is_empty() and screen.selected_target.is_empty() and hand.interaction.selected.is_empty(), "mouse hover on another card clears card and target selection")
	check(hand.cards().values().all(func(card): return not card.chosen and not card.button_pressed), "hover cancellation removes all selected card borders")
	check(hand.cards().values().all(func(card): return not card.has_focus()) and hand.interaction.focused.is_empty(), "hover cancellation also removes native focus outlines and printed keyline emphasis")
	await capture("deselected-card")
	check(hand.interaction.hovered == "second" and hand.active_card_id() == "second", "hovered card remains available for preview without being selected")
	check(not screen.view.selection_actions.visible and screen.view.choices.get_child_count() == 0 and screen.view.target_buttons.values().all(func(button): return button.disabled), "hover cancellation removes old action choices and target affordances")
	check(gateway.state.sequence == 1 and gateway.combat.step == 1, "hovering never submits an engine command")
	screen._choose_card("first")
	hand.card_for("first").grab_focus()
	var right := InputEventKey.new()
	right.keycode = KEY_RIGHT; right.physical_keycode = KEY_RIGHT; right.pressed = true
	root.push_input(right, true); await settle()
	check(root.gui_get_focus_owner() == hand.card_for("second") and screen.selected_card == "first", "keyboard focus on another card preserves the selected card")
	right.pressed = false; root.push_input(right, true)
	var joy := InputEventJoypadButton.new()
	joy.button_index = JOY_BUTTON_DPAD_RIGHT; joy.pressed = true
	root.push_input(joy, true); await settle()
	check(root.gui_get_focus_owner() == hand.card_for("third") and screen.selected_card == "first", "controller focus on another card preserves the selected card")
	joy.pressed = false; root.push_input(joy, true)
	# The last hovered id can still be cached when returning from keyboard focus.
	hover_card(hand, "second"); await settle()
	check(screen.selected_card.is_empty(), "resuming mouse over the cached hover cancels keyboard-preserved selection")
	check(hand.cards().values().all(func(card): return not card.has_focus()), "returning to mouse also clears focus on a different keyboard-focused card")
	screen._choose_card("first")
	screen.controller.state.submitting = true; screen._update_controls()
	hover_card(hand, "third"); await settle()
	check(screen.selected_card == "first", "mouse hover cannot cancel a pending submission")
	screen.controller.state.submitting = false; screen._update_controls()
	hand.interaction.dragging = true
	hover_card(hand, "second"); await settle()
	check(screen.selected_card == "first", "dragging across another card preserves the ongoing choice")
	hand.interaction.dragging = false
	paused = true
	hover_card(hand, "third"); await settle()
	check(screen.selected_card == "first", "paused combat cannot cancel selection through mouse hover")
	paused = false
	hand.reading_modal = true
	var overlay_motion := InputEventMouseMotion.new()
	var overlay_card: Control = hand.card_for("second")
	overlay_motion.position = overlay_card.get_global_transform() * (overlay_card.size * .5)
	overlay_motion.global_position = overlay_motion.position
	root.push_input(overlay_motion, true); await settle()
	check(screen.selected_card == "first", "reading overlays do not cancel the underlying selection")
	hand.reading_modal = false
	screen._cancel_selection(); hand.clear_choice()
	hand.mode_override = ""; hand.request_layout(); await settle()
	prefs.reduced_motion = false
	var point: Vector2 = hand.scroll.global_position + hand._layout.hitRects[1].get_center() - Vector2(hand.scroll.scroll_horizontal, 0)
	var motion := InputEventMouseMotion.new()
	motion.position = point; motion.global_position = point
	root.push_input(motion, true)
	for pressed in [true, false]:
		var event := InputEventMouseButton.new()
		event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed
		event.position = point; event.global_position = point
		root.push_input(event, true)
	check(screen.selected_card == "second", "explicit card input still selects a card")
	check(hand.interaction.pointer_active and hand.card_for("second").has_focus(), "mouse click remains pointer-driven despite retaining navigation focus")
	var cancel_click := InputEventMouseButton.new()
	cancel_click.button_index = MOUSE_BUTTON_RIGHT; cancel_click.pressed = true
	cancel_click.position = point; cancel_click.global_position = point
	root.push_input(cancel_click, true); await settle()
	check(screen.selected_card.is_empty() and hand.cards().values().all(func(card): return not card.chosen and not card.has_focus()), "right-click mouse cancellation also removes selection and focus outlines")
	cancel_click.pressed = false; root.push_input(cancel_click, true)
	for pressed in [true, false]:
		var event := InputEventMouseButton.new()
		event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed
		event.position = point; event.global_position = point
		root.push_input(event, true)
	check(screen.selected_card == "second", "mouse can explicitly select the card again after cancellation")
	await screen._play_candidate(screen.view._candidate_for_card("second"))
	await settle()
	check(screen.view.hand == hand and hand.card_for("first") == survivor and "second" not in hand.order, "accepted use reconciles instances without rebuilding survivors")
	check_neutral(screen, "consumed card")
	check(not playback.has_blocking_work(), "an empty effect receipt requires no manual confirmation or delay")
	screen._next_frame()
	await settle()
	check_neutral(screen, "after animation confirmation")
	# Focus is valid and outside the cards; controller Up can deliberately return.
	var neutral = root.gui_get_focus_owner()
	check(is_instance_valid(neutral) and neutral.name == "HandInspectButton", "post-play focus is on the neutral inspect tool, not removed overview")
	var up := InputEventKey.new()
	up.keycode = KEY_UP; up.physical_keycode = KEY_UP; up.pressed = true
	root.push_input(up, true)
	await settle()
	check(root.gui_get_focus_owner() in hand.cards().values(), "keyboard/controller navigation can explicitly focus cards again")
	up.pressed = false
	root.push_input(up, true)
	# Retained cards must also end their choice after an accepted play.
	prefs.reduced_motion = true
	gateway.consume = false
	hand.card_for("first").grab_focus()
	screen._choose_card("first")
	await screen._play_candidate(screen.view._candidate_for_card("first"))
	await settle()
	check(hand.card_for("first") == survivor, "engine-retained card keeps its shared component")
	check_neutral(screen, "retained card")
	screen._next_frame()
	# Enrichment and navigation rewiring must not select the first card later.
	var refreshed: Dictionary = screen.presentation.duplicate(true)
	refreshed["inspection_fixture"] = true
	screen.refresh_state(refreshed)
	await settle()
	load("res://scripts/ui/focus_navigation.gd").wire(screen, true)
	await settle()
	check_neutral(screen, "refresh and initial focus rewiring")
	# A rejected command does not pretend the card was used or drop its choice.
	gateway.reject = true
	hand.card_for("first").grab_focus()
	screen._choose_card("first")
	await screen._play_candidate(screen.view._candidate_for_card("first"))
	await settle()
	check(screen.selected_card == "first" and hand.card_for("first").chosen, "rejected play preserves its explicit selection for recovery")
	router.queue_free()
	await settle()
	playback.clear()
	session.configure(prior.gateway)
	session._run = prior.run; session._combat = prior.combat; session._card_zones = prior.zones
	session._legal_actions = prior.actions; session._available_commands = prior.commands
	session._tool_capabilities = prior.capabilities; session.synchronized = prior.synchronized
	prefs.reduced_motion = prior.motion; prefs.auto_animations = prior.auto
	root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	print("CARD PLAY SELECTION: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
