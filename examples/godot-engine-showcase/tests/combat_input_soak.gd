extends SceneTree
## Repeated production-screen interaction without recreating combat between commands.
var failures := 0
var done := false
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 6: await process_frame
func play(screen, candidate: Dictionary) -> void:
	await screen._play_candidate(candidate); done = true
func release_read(fixture) -> void:
	await create_timer(.035).timeout
	fixture.release.emit()
func _run() -> void:
	var prefs = root.get_node("Preferences")
	prefs.animation_speed = 4.; prefs.auto_animations = true
	var session = root.get_node("GameSession")
	var cursor = root.get_node("Playback")
	var fixture = preload("res://tests/animation_live.gd").Fixture.new()
	fixture.retained = true
	session.configure(fixture)
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	var router = load("res://main.tscn").instantiate(); root.add_child(router); router.show_combat(); await settle()
	var screen = router.host.get_child(0)
	var screen_id: int = screen.get_instance_id()
	var switch_zone := {"enabled": false}
	var on_receipt := func(_receipt):
		if switch_zone.enabled: fixture.zones.zones[0].zoneId = "next-playable-" + str(fixture.state.sequence)
	session.receipt_received.connect(on_receipt)
	for iteration in 40:
		prefs.reduced_motion = iteration % 4 == 0
		prefs.auto_animations = iteration % 9 != 0
		fixture.null_resolution = iteration % 3 == 0
		fixture.null_receipt_combat = iteration % 6 == 0
		fixture.held = iteration % 7 == 0
		fixture.reject = iteration % 11 == 0
		fixture.fail_refresh = iteration % 13 == 0 and not fixture.reject
		switch_zone.enabled = iteration % 5 == 0
		var before: int = fixture.sends
		done = false
		play(screen, screen.presenter.candidate_for_card("played"))
		if fixture.held: release_read(fixture)
		var deadline := Time.get_ticks_msec() + 5000
		while not done and Time.get_ticks_msec() < deadline: await process_frame
		fixture.held = false; fixture.reject = false; fixture.fail_refresh = false
		if not session.synchronized: await session.refresh()
		while screen.controller.locked() and Time.get_ticks_msec() < deadline:
			if not session.busy and cursor.has_frames() and cursor.active_handle == null: screen._next_frame()
			await process_frame
		await settle()
		check(done and fixture.sends == before + 1 and not session.busy and not session.has_pending_command and not screen.controller.locked(), "cycle %s completes/rejects/recovers with exactly one write and no orphan gate" % iteration)
		check(router.host.get_child(0).get_instance_id() == screen_id and screen.view.hand.card_for("played") != null, "cycle %s preserves the same mounted combat and retained card" % iteration)
		if failures: break
	session.receipt_received.disconnect(on_receipt)
	router.queue_free(); await settle(); cursor.clear()
	session.configure(null); on_receipt = Callable(); fixture = null
	await settle(); root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout # Allow the audio mixer to release its final Ogg voice.
	quit(1 if failures else 0)
