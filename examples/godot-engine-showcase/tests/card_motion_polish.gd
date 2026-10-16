extends SceneTree
var failures := 0
var output_dir := ""
var visual := false
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _index in 5: await process_frame
func settle_fixture_entries(hand) -> void:
	# Preset setup has its own cosmetic fade. Finish that fixture animation before
	# measuring a scenario such as reorder, whose director completes immediately.
	var started := Time.get_ticks_msec()
	while hand._entries.values().any(func(tween): return tween != null and tween.is_valid() and tween.is_running()) and Time.get_ticks_msec() - started < 2000:
		await process_frame
func capture(name: String) -> void:
	if not visual: return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output_dir.path_join(name + ".png")) == OK, "rendered " + name)
func _run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output_dir = arg.trim_prefix("--lab-output=")
		if arg.begins_with("--capture-resolution="):
			var dimensions := arg.trim_prefix("--capture-resolution=").split("x")
			root.size = Vector2i(int(dimensions[0]), int(dimensions[1]))
	root.content_scale_size = Vector2i(1440, 900)
	root.content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	root.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_EXPAND
	visual = not output_dir.is_empty() and DisplayServer.get_name() != "headless"
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab); await settle()
	lab.visual_context._options["animation_speed"] = 1.0; lab.visual_context._options["reduced_motion"] = false
	var outcomes: Array = []
	lab.motion_lab.finished.connect(func(_models, metrics): outcomes.append(metrics))
	for preset in ["normal", "fast", "instant"]:
		for scenario in lab.motion_scenarios:
			lab.set_preset(0); await settle()
			await settle_fixture_entries(lab.hand)
			lab.motion_lab.preset = preset
			lab.motion_lab.play(scenario, lab.models)
			if visual and preset == "normal" and scenario.id == "draw_five":
				await create_timer(.075).timeout; await capture("draw-in-flight")
			var start := Time.get_ticks_msec()
			while lab.motion_lab.playback.has_blocking_work() and Time.get_ticks_msec() - start < 2000: await process_frame
			await settle()
			check(not lab.motion_lab.playback.has_blocking_work() and lab.hand.order.size() == lab.motion_lab.final_models.size() and lab.hand.ordered_cards().all(func(card): return card.visible and card.modulate.a == 1.0), preset + ": " + str(scenario.id) + " delivers exactly the final visible hand")
			check(lab.motion_lab.overlay.get_child_count() == 0, preset + ": " + str(scenario.id) + " leaves no transit nodes")
			if scenario.id in ["draw_one", "draw_five", "draw_ten", "use", "empty"]: print("MOTION_METRIC=", preset, " ", scenario.id, " ", JSON.stringify(outcomes[-1]))
	lab.set_preset(0); await settle(); lab.motion_lab.preset = "normal"
	lab.motion_lab.options.auto_animations = false
	var empty: Dictionary = lab.motion_scenarios.filter(func(item): return item.id == "empty")[0]
	lab.motion_lab.play(empty, lab.models)
	var idle_ticks := 0
	while lab.motion_lab.playback.has_blocking_work() and idle_ticks < 10:
		check(lab.motion_lab.playback.active_handle == null, "CPU budget yields do not create an animation wait")
		await process_frame; idle_ticks += 1
	check(not lab.motion_lab.playback.has_blocking_work(), "manual mode drains 100 absent/zero-delta effects without confirmation")
	var draw: Dictionary = lab.motion_scenarios.filter(func(item): return item.id == "draw_five")[0]
	lab.motion_lab.play(draw, lab.models)
	check(lab.motion_lab.playback.index == 0, "manual mode waits for a real purchase animation")
	lab.motion_lab.director.advance()
	check(lab.motion_lab.overlay.get_children().all(func(card): return not card.visible), "staggered backs stay hidden until their own flight starts")
	await create_timer(.035).timeout
	var ghost: Control = lab.motion_lab.overlay.get_child(0); var position: Vector2 = ghost.position
	paused = true; await create_timer(.04).timeout
	check(ghost.position == position, "pause freezes the shared batch choreography")
	await capture("paused-draw"); paused = false
	lab.motion_lab.skip(); await settle()
	check(lab.hand.order.size() == 10 and lab.hand.ordered_cards().all(func(card): return card.visible) and lab.motion_lab.overlay.get_child_count() == 0, "skip settles reserved cards with no invisible hand or duplicated flight")
	await capture("arrived-hand")
	lab.set_preset(0); await settle(); lab.visual_context._options["reduced_motion"] = true
	lab.motion_lab.play(draw, lab.models)
	check(not lab.motion_lab.playback.has_blocking_work() and lab.hand.order.size() == 10, "reduced movement in manual mode has no fictitious purchase wait")
	lab.visual_context._options["reduced_motion"] = false
	lab.motion_lab.options.auto_animations = true
	lab.set_preset(0); await settle(); lab.motion_lab.play(draw, lab.models)
	await create_timer(.035).timeout
	var original_size := root.size; root.size += Vector2i(160, 90)
	while lab.motion_lab.playback.has_blocking_work(): await process_frame
	await settle()
	check(lab.hand.ordered_cards().all(func(card): return card.visible) and lab.motion_lab.overlay.get_child_count() == 0, "resizing in flight recomputes arrival anchors and settles cleanly")
	root.size = original_size
	lab.set_preset(0); await settle(); lab.motion_lab.play(draw, lab.models)
	lab.queue_free(); await settle()
	check(not is_instance_valid(lab), "destroying a batch cleans the director and transit owners")
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
