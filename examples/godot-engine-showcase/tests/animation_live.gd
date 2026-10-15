extends SceneTree
## Real adapters and production views; delayed fixture reads, no network/game writes.
var failures := 0
var done := false
var output_dir := ""
var capture_resolution := Vector2i.ZERO
class Fixture extends RefCounted:
	signal release
	signal command_release
	var held := false
	var hold_command := false
	var reject := false
	var fail_refresh := false
	var sends := 0
	var command_received_ms := 0
	var terminal := false
	var retained := false
	var exile := false
	var superseded := false
	var state := {"runId": "animation-fixture", "sequence": 1, "activeEncounterId": "combat"}
	var combat := {"combatId": "combat", "step": 1, "status": "ACTIVE", "activeActorId": "hero", "actors": [{"instanceId": "hero", "definitionId": "spire_adept", "sideId": "player", "resources": {}}]}
	var zones := {"zones": [{"zoneId": "custom", "allowsCardPlay": true, "contentsVisible": true, "count": 2, "cards": [
		{"cardInstanceId": "played", "definitionId": "basic_attack"}, {"cardInstanceId": "remaining", "definitionId": "defend"}]}]}
	func candidate_payload(command: Dictionary) -> Dictionary: return command.duplicate(true)
	func prepare_command(_scope: String, _id: String, _type: String, payload: Dictionary, _sequence: int, _step: int) -> Dictionary: return {"payload": payload.duplicate(true)}
	func send_prepared(_command: Dictionary) -> Dictionary:
		sends += 1
		if hold_command: await command_release
		if reject: return {"ok": false, "status": 422, "error": "Rejected fixture"}
		command_received_ms = Time.get_ticks_msec()
		state.sequence += 1; combat.step += 1
		if terminal: combat.status = "WON"
		if not retained:
			zones.zones[0].cards = zones.zones[0].cards.filter(func(card): return card.cardInstanceId != "played"); zones.zones[0].count = 1
		if exile:
			zones.zones[1] = {"zoneId": "unrelated-name", "contentsVisible": true, "allowsCardPlay": false, "count": 1, "presentation": {"slot": "exile_pile", "labelKey": "Exile"}, "cards": [{"cardInstanceId": "played", "definitionId": "basic_attack"}]}
			return {"ok": true, "data": {"sequence": state.sequence, "state": {"combat": combat.duplicate(true), "resolution": {"frames": [{"transitionType": "effect", "cardZoneSteps": [{"flowId": "card.played.to-exhaust", "sourceAddress": "$run::::custom", "targetAddress": "$run::::unrelated-name", "instanceIds": ["played"]}]}]}}}}
		return {"ok": true, "data": {"sequence": state.sequence, "state": {"combat": combat.duplicate(true), "resolution": {"frames": [
			{"transitionType": "effect", "applications": [{"effectType": "DAMAGE", "resourceId": "mana", "targetEntityId": "hero", "previousValue": 8, "currentValue": 6}]},
			{"transitionType": "effect", "applications": [{"effectType": "HEAL", "resourceId": "mana", "targetEntityId": "hero", "previousValue": 6, "currentValue": 7}]}]}}}}
	func read_session(id: String, expected := -1) -> Dictionary:
		return await preload("res://tests/session_fixture_adapter.gd").read(self, id, expected)
	func read_run(_id: String) -> Dictionary:
		if superseded: state.sequence += 1; superseded = false
		return {"ok": true, "data": state.duplicate(true)}
	func read_projections(_run: String, _combat: String, include: bool) -> Dictionary:
		var results := {"cardZones": {"ok": true, "data": zones.duplicate(true)}, "commands": {"ok": true, "data": {"commands": []}}, "capabilities": {"ok": true, "data": {}}}
		if include: results.combat = {"ok": true, "data": combat.duplicate(true)}
		return results
	func read_legal_actions(_combat: String, _actor: String) -> Dictionary:
		return {"ok": true, "data": {"candidates": zones.zones[0].cards.map(func(card): return {"source": "Card", "command": {"cardInstanceId": card.cardInstanceId, "actionType": "PLAY_CARD", "targetIds": ["hero"]}})}}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		if held: await release
		if fail_refresh: return {"ok": false, "status": 503, "error": "Held refresh failed"}
		return {"ok": true, "data": {"cards": []}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func capture(name: String) -> void:
	if output_dir.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output_dir.path_join(name + ".png")) == OK, "rendered capture: " + name)
func launch(screen, candidate: Dictionary) -> void:
	await screen._play_candidate(candidate); done = true
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
		if argument.begins_with("--capture-resolution="):
			var parts := argument.trim_prefix("--capture-resolution=").split("x")
			if parts.size() == 2: capture_resolution = Vector2i(int(parts[0]), int(parts[1]))
	var prefs = root.get_node("Preferences")
	prefs.reduced_motion = false; prefs.auto_animations = false; prefs.animation_speed = 1.0; prefs.animation_preset = "normal"
	var session = root.get_node("GameSession"); var cursor = root.get_node("Playback")
	var fixture := Fixture.new(); session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	var router = load("res://main.tscn").instantiate(); root.add_child(router); router.show_combat(); await settle()
	if capture_resolution != Vector2i.ZERO:
		root.mode = Window.MODE_WINDOWED; root.size = capture_resolution; await settle()
	var screen = router.host.get_child(0)
	var seen: Array = []
	cursor.frame_presented.connect(func(frame, _index, _total): seen.append(frame.get("presentationOnly", frame.get("transitionType", ""))))
	fixture.held = true
	fixture.hold_command = true
	launch(screen, screen.presenter.candidate_for_card("played")); await create_timer(.1).timeout
	check(session.busy and seen.is_empty() and cursor.index == 0, "pending command cannot present unconfirmed impacts")
	check(screen.view.hand.card_for("played").get_parent() != screen.view.feedback.effects, "before acceptance, the face stays in the hand without a separate preparation animation")
	fixture.command_release.emit(); await create_timer(.04).timeout
	check(screen._receipt_motion_started >= fixture.command_received_ms and screen._receipt_motion_started - fixture.command_received_ms < 50,
		"accepted card flight begins immediately, without waiting for inspection")
	check(seen.is_empty() and cursor.index == 0 and session.run.sequence == 1 and session.busy and screen.controller.locked(),
		"early flight cannot publish effects, costs, final facts or new commands")
	check(screen.view.hand.card_for("played").get_parent() == screen.view.feedback.effects, "early flight borrows the existing card face")
	var pending_position: Vector2 = screen.view.hand.card_for("played").position
	await create_timer(.04).timeout
	check(screen.view.hand.card_for("played").position != pending_position, "flight moves while the post-command inspection is still held")
	await capture("accepted-flight-before-refresh")
	var ghost: Control = screen.view.hand.card_for("played")
	router.toggle_pause(); var position: Vector2 = ghost.position
	var art: Control = screen.view.actor_portraits.hero; art.flash_hit(); var tint: Color = art.modulate
	await create_timer(.25).timeout
	check(ghost.position == position and art.modulate == tint and not art.can_process(), "pause freezes accepted flight and scenery while inspection is pending")
	await capture("paused-flight")
	fixture.release.emit(); await settle()
	check(session.synchronized and cursor.index == 0 and seen.is_empty() and ghost.position == position,
		"synchronization during pause cannot advance any queued cue or the accepted flight")
	router.toggle_pause(); await create_timer(.025).timeout
	check(seen == ["card_departure"] and screen.controller.locked(), "card departure starts first and keeps commands locked")
	var played = screen.view.hand.card_for("played")
	check((played == null or played.get_parent() == screen.view.feedback.effects) and screen.view.feedback.effects.get_children().filter(func(node): return node is CardView).size() <= 1, "synchronization never creates a duplicate departure face")
	await capture("card-departure")
	await create_timer(.6).timeout
	check(done and screen.view.hand.card_for("played") == null and not cursor.has_blocking_work(), "consumed card leaves hand and cosmetic effects require no extra manual confirmation")
	screen._next_frame(); screen._next_frame()
	check(cursor.active_handle == null and not screen.controller.locked(), "repeated confirmation cannot create a blocking cosmetic impact")
	await create_timer(.2).timeout
	screen.presentation_driver.skip(); await settle()
	check(not cursor.has_blocking_work() and session.run.sequence == 2 and not screen.controller.locked(), "skip settles visuals and reconciles authoritative state without another command")
	await capture("settled")
	# A complete early flight is reused instead of flying to the stage twice.
	fixture = Fixture.new(); fixture.held = true; session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	router.show_combat(); await settle(); screen = router.host.get_child(0); done = false
	launch(screen, screen.presenter.candidate_for_card("played")); await create_timer(.6).timeout
	check(screen._receipt_departure.done and not cursor.has_blocking_work() and session.busy, "completed early departure waits for synchronization without impacts")
	fixture.release.emit(); await settle()
	check(screen.view.hand.card_for("played") == null and cursor.active_handle == null and not cursor.has_frames(),
		"synchronization consumes the completed departure and cosmetic facts without manual waits")
	screen.presentation_driver.skip(); await settle()
	# A published exile flow replaces departure with one short burn.
	fixture = Fixture.new(); fixture.exile = true
	fixture.zones.zones.append({"zoneId": "unrelated-name", "contentsVisible": true, "allowsCardPlay": false, "count": 0, "presentation": {"slot": "exile_pile", "labelKey": "Exile"}, "cards": []})
	session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	prefs.auto_animations = true; prefs.animation_speed = 1.0
	router.show_combat(); await settle(); screen = router.host.get_child(0)
	await screen._play_candidate(screen.presenter.candidate_for_card("played"))
	await process_frame
	var burn = screen.view.feedback.effects.find_child("CardBurnView", true, false)
	check(burn != null and screen._receipt_motion_started == 0, "published exile flow starts a center burn without an extra departure flight")
	if burn != null:
		check(burn.burn_material.get_shader_parameter("direction") == 0, "live exile uses the center variant")
		var started := Time.get_ticks_usec()
		while cursor.has_blocking_work(): await process_frame
		var duration := (Time.get_ticks_usec() - started) / 1000000.0
		print("EXILE_METRIC=", duration)
		check(duration >= .35 and duration <= .75, "live exile burn finishes in a half-second cycle")
	check(screen.view.hand.card_for("played") == null and not screen.controller.locked() and fixture.sends == 1 and screen.view.feedback.effects.find_child("CardBurnView", true, false) == null, "exile settles the authoritative hand once and cleans the burn")
	prefs.auto_animations = false
	# Rejection never authorizes departure, even with animations enabled.
	fixture = Fixture.new(); fixture.reject = true; session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	router.show_combat(); await settle(); screen = router.host.get_child(0)
	await screen._play_candidate(screen.presenter.candidate_for_card("played")); await settle()
	check(screen._receipt_departure == null and not screen.view.hand.is_in_transit("played") and screen.view.hand.card_for("played").get_parent() != screen.view.feedback.effects and fixture.sends == 1 and session.run.sequence == 1,
		"rejected command restores the hand and never departs or resubmits")
	# Accepted writes survive a failed refresh; recovery is read-only and reuses the flight.
	fixture = Fixture.new(); fixture.held = true; fixture.fail_refresh = true; session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	router.show_combat(); await settle(); screen = router.host.get_child(0); done = false
	launch(screen, screen.presenter.candidate_for_card("played")); await create_timer(.6).timeout
	fixture.release.emit(); await settle()
	check(not session.synchronized and screen.controller.locked() and session.run.sequence == 1 and screen._receipt_departure.done,
		"failed refresh keeps the accepted staged card locked without publishing partial facts")
	fixture.held = false; fixture.fail_refresh = false
	await screen._reconnect(); await settle()
	check(session.synchronized and session.run.sequence == 2 and fixture.sends == 1 and screen.view.hand.card_for("played") == null,
		"read-only recovery settles the same accepted command without another flight/send")
	screen.presentation_driver.skip(); await settle()
	fixture = Fixture.new(); fixture.held = true; fixture.retained = true; session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	router.show_combat(); await settle(); screen = router.host.get_child(0); done = false
	var original_face = screen.view.hand.card_for("played")
	launch(screen, screen.presenter.candidate_for_card("played")); await create_timer(.6).timeout
	fixture.release.emit(); await create_timer(.6).timeout; await settle()
	check(screen.view.hand.card_for("played") == original_face and original_face.get_parent() == screen.view.hand._slots.played and original_face.scale == Vector2.ONE,
		"retained card returns the same accepted face to its final hand slot")
	check(screen.view.hand.interaction.selected.is_empty() and fixture.sends == 1, "retained return has no automatic selection or second command")
	screen.presentation_driver.skip(); await settle()
	fixture = Fixture.new(); fixture.superseded = true; session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	router.show_combat(); await settle(); screen = router.host.get_child(0)
	await screen._play_candidate(screen.presenter.candidate_for_card("played")); await settle()
	check(screen._receipt_departure == null and screen._transits == null and not cursor.has_blocking_work() and not screen.controller.locked(),
		"superseded receipt cancels its early handle and displays the newer revision without orphan motion")
	await screen._play_candidate(screen.presenter.candidate_for_card("remaining")); await settle()
	check(fixture.sends == 2 and session.synchronized and not cursor.has_blocking_work(), "the next valid command completes its accepted flight after supersession without orphan locks")
	screen.presentation_driver.skip(); await settle()
	# Terminal outcomes must keep the combat view until its beats are settled.
	fixture = Fixture.new(); fixture.terminal = true; session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	prefs.reduced_motion = true
	router.show_combat(); await settle(); screen = router.host.get_child(0)
	await screen._play_candidate(screen.presenter.candidate_for_card("played")); await settle()
	check(not cursor.has_blocking_work(), "reduced motion does not require manual clicks for absent animations")
	await settle()
	check(router.current_screen != "combat" and not cursor.has_blocking_work(), "terminal navigation occurs only after presentation settles")
	# Destroying a view during a flight must not execute its deferred navigation.
	fixture = Fixture.new(); session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	prefs.reduced_motion = false
	router.show_combat(); await settle(); screen = router.host.get_child(0)
	launch(screen, screen.presenter.candidate_for_card("played")); await create_timer(.025).timeout
	router.back_to_menu(); await create_timer(.3).timeout
	check(router.current_screen == "menu" and not cursor.has_blocking_work(), "destroyed flight cannot navigate or release another scene")
	router.queue_free(); await settle(); cursor.clear(); root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
