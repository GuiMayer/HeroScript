extends SceneTree
var failures := 0
var output := ""
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> bool:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
	return ok
func settle() -> void:
	for _i in 8: await process_frame
func capture(label: String) -> void:
	if output.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output.path_join(label + ".png")) == OK, "rendered " + label)
func wait_session(session, cursor) -> void:
	var start := Time.get_ticks_msec()
	while (session.busy or cursor.has_blocking_work()) and Time.get_ticks_msec()-start < 12000: await process_frame
	await settle()
func _run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output = arg.trim_prefix("--lab-output=")
		if arg.begins_with("--capture-resolution="):
			var parts := arg.trim_prefix("--capture-resolution=").split("x"); root.size = Vector2i(int(parts[0]), int(parts[1]))
	var session = root.get_node("GameSession"); var cursor = root.get_node("Playback")
	var preferences = root.get_node("Preferences")
	preferences.auto_animations = true; preferences.animation_speed = 1; preferences.reduced_motion = false; preferences.animation_preset = "normal"
	if not check(await session.connect_engine(), "connect isolated real API"): quit(1); return
	session.select_setting("default")
	var content = JSON.parse_string(FileAccess.get_file_as_string("res://data/presentation.json"))
	var seed := int(Time.get_unix_time_from_system()) % 1000000000
	if not check(await session.start_sandbox("development_lab", content.default_scenario, seed), "create dedicated QA sandbox"): quit(1); return
	cursor.clear()
	var router = load("res://main.tscn").instantiate(); root.add_child(router); router.show_combat(); await settle()
	cursor.skip(); await settle()
	var screen = router.host.get_child(0); var hand = screen.view.hand
	var ids: Array = hand.order.duplicate(); var id: String = ids[0]
	var canonical := JSON.stringify(session.capture_view_snapshot())
	var baseline: Dictionary = await session.simulate_end_turn()
	if not check(baseline.get("ok", false), "simulate from the unchanged source revision"): router.queue_free(); quit(1); return
	var transport = root.get_node("GameServices")._transport
	var serial: int = transport._serial
	var point: Vector2 = hand.destination_center(str(ids[-1]))
	var start: Dictionary = preload("res://scripts/ui/card_motion_pose.gd").read(hand.card_for(id))
	await capture("canonical-hand")
	hand.interaction.pressed = id; hand.interaction.press_position = start.center; hand.interaction.dragging = true
	hand._drag_visual(point); await capture("insertion-preview")
	check(hand.contains_reorder_point(point), "combat hand accepts its slot while excluding other HUD controls")
	hand.finish_drag(id, point); await create_timer(.25).timeout
	var expected: Array = ids.slice(1); expected.append(id)
	check(hand.order == expected and hand._ghost == null, "real combat commits visual insertion and settles the borrowed face")
	check(transport._serial == serial and JSON.stringify(session.capture_view_snapshot()) == canonical, "gesture sends zero REST requests and leaves every authoritative snapshot unchanged")
	await capture("reordered-hand")
	var comparison: Dictionary = await session.simulate_end_turn()
	check(comparison.get("ok", false) and comparison.data.finalStateHash == baseline.data.finalStateHash, "same source revision and command retain the identical simulation hash after local rearrangement")
	router._clear_host(); router.current_screen = ""; router.show_combat(); await settle()
	screen = router.host.get_child(0); hand = screen.view.hand
	check(hand.order == expected, "same UI session restores arrangement after combat screen remount")
	var candidate := {}
	for option in session.legal_actions:
		if str(option.get("command", {}).get("cardInstanceId", "")) == id: candidate = option; break
	var sequence := int(session.run.sequence)
	if check(not candidate.is_empty(), "reordered instance retains its published legal command"):
		await screen._play_candidate(candidate); await wait_session(session, cursor)
		check(int(session.run.sequence) == sequence+1, "playing the moved instance commits exactly one gameplay command")
		screen = router.host.get_child(0); hand = screen.view.hand
		var survivors: Array = expected.filter(func(instance): return instance in hand.order)
		check(hand.order.filter(func(instance): return instance in expected) == survivors, "using a moved card preserves the surviving visual order")
		await capture("after-play")
		var before_turn: Array = hand.order.duplicate()
		screen._end_turn(); await wait_session(session, cursor)
		screen = router.host.get_child(0); hand = screen.view.hand
		check(hand.order.filter(func(instance): return instance in before_turn) == before_turn.filter(func(instance): return instance in hand.order), "real turn transition preserves relative survivor order and reconciles new arrivals")
		await capture("after-turn")
	var verification: Dictionary = await session.verify()
	check(verification.get("ok", false) and verification.data.get("isValid", false), "canonical replay remains valid after local reordering, play and end turn")
	check(hand._ghost == null and hand._transit.is_empty() and not cursor.has_blocking_work(), "real cycle leaves no stuck visual owner or input gate")
	router.queue_free(); await settle(); cursor.clear()
	root.get_node("GameAudio").shutdown(); await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
