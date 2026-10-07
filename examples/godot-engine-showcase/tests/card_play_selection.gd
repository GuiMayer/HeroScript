extends SceneTree
## Real session/UI command flow with an injected gateway, no API or preference writes.
var failures := 0
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
func check_neutral(screen, label: String) -> void:
	check(screen.selected_card.is_empty() and screen.selected_target.is_empty() and screen.hand.interaction.selected.is_empty(), label + ": no selected card/target")
	check(screen.hand.interaction.active_id().is_empty() and screen.hand.active_card_id().is_empty(), label + ": no automatic hover, focus or inspection anchor")
	check(screen.hand.cards().values().all(func(card): return not card.chosen and not card.button_pressed and not card.has_focus()), label + ": no raised/focused/pressed card")
func _run() -> void:
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
	var hand = screen.hand
	check_neutral(screen, "combat entry")
	var survivor = hand.card_for("first")
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
	await screen._play_candidate(screen._candidate_for_card("second"))
	await settle()
	check(screen.hand == hand and hand.card_for("first") == survivor and "second" not in hand.order, "accepted use reconciles instances without rebuilding survivors")
	check_neutral(screen, "consumed card")
	check(playback.has_frames(), "manual presentation queue still waits for confirmation independently of selection")
	screen._next_frame()
	await settle()
	check_neutral(screen, "after animation confirmation")
	# Focus is valid and outside the cards; controller Up can deliberately return.
	var neutral = root.gui_get_focus_owner()
	check(is_instance_valid(neutral) and neutral.name == "HandOverviewButton", "post-play focus is on a neutral hand tool")
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
	await screen._play_candidate(screen._candidate_for_card("first"))
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
	await screen._play_candidate(screen._candidate_for_card("first"))
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
	print("CARD PLAY SELECTION: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
