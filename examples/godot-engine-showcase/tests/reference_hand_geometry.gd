extends SceneTree
var failures := 0
var output_dir := ""
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 12: await process_frame
func hand_center(hand) -> float:
	var left := INF
	var right := -INF
	for id in hand.order:
		var card: Control = hand.card_for(id)
		for corner in [Vector2.ZERO, Vector2(card.size.x, 0), card.size, Vector2(0, card.size.y)]:
			var point: Vector2 = card.get_global_transform() * corner
			left = minf(left, point.x); right = maxf(right, point.x)
	return (left + right) * .5
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	var layout = load("res://scripts/ui/hand_layout.gd")
	var style = load("res://data/default_hand_visual_style.tres")
	var heights: Array = []
	for count in [0, 1, 2, 3, 5, 10, 20]:
		var model: Dictionary = layout.calculate(count, 1800., Vector2(248, 360), style, "adaptive")
		heights.append(model.height)
		check(model.rotations.size() == count and model.hitRects.size() == count, "one geometry per instance: %s" % count)
		if count >= 2: check(model.rotations[0] < 0 and model.rotations[-1] > 0 and is_equal_approx(-model.rotations[0], model.rotations[-1]), "fan remains symmetric even with ample space: %s" % count)
		if count == 1: check(model.rotations[0] == 0, "single card is upright")
		var reading: Dictionary = layout.calculate(count, 1000., Vector2(248, 360), style, "reading")
		check(reading.rotations.all(func(angle): return angle == 0), "accessible reading remains flat: %s" % count)
	check(heights.all(func(height): return is_equal_approx(height, heights[0])), "card quantity does not move the actor HUD or dock")
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	lab.visual_context._options["reduced_motion"] = true; lab.visual_context.capture_motion()
	await settle()
	var hand = lab.hand
	for resolution in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = resolution; await settle()
		for mode in ["adaptive", "reading"]:
			hand.mode_override = mode
			lab.set_count(40); await settle()
			hand.card_for(hand.order[-1]).grab_focus(); await settle()
			check(hand.scroll.scroll_horizontal > 0, "fixture scrolled to a large hand")
			hand.clear_choice()
			for count in [3, 2, 1, 0, 2, 5]:
				lab.set_count(count); hand.clear_choice(); await settle()
				if count == 0: continue
				var delta: float = hand_center(hand) - hand.scroll.get_global_rect().get_center().x
				check(absf(delta) <= 1.0 and hand.scroll.scroll_horizontal == 0, "visible hand centered after removal: %s %s %s" % [resolution, mode, count])
				if not output_dir.is_empty() and DisplayServer.get_name() != "headless" and count in [1, 2, 3]:
					await RenderingServer.frame_post_draw
					root.get_texture().get_image().save_png(output_dir.path_join("hand-%s-%s-%s.png" % [resolution.x, mode, count]))
	hand.mode_override = "adaptive"
	lab.visual_context._options["reduced_motion"] = false
	lab.visual_context.capture_motion()
	# Two projections can land before a layout tween receives its first tick.
	# A stale larger-hand tween must not overwrite the restored small layout.
	for count in [1, 2, 3]:
		lab.set_count(count); hand.clear_choice(); await create_timer(.3).timeout; await settle()
		lab.set_count(count + 1); hand._relayout()
		lab.set_count(count); hand._relayout()
		await create_timer(.3).timeout; await settle()
		check(absf(hand_center(hand) - hand.scroll.get_global_rect().get_center().x) <= 1.0, "superseded tween cannot move a small hand back to obsolete positions: %s" % count)
		var exact := true
		for i in hand.order.size():
			if hand._slots[hand.order[i]].position.distance_to(hand._layout.positions[i]) > .5: exact = false
		check(exact, "all slots settle at the current layout, not the abandoned tween target")
		if not output_dir.is_empty() and DisplayServer.get_name() != "headless":
			await RenderingServer.frame_post_draw
			root.get_texture().get_image().save_png(output_dir.path_join("hand-superseded-%s.png" % count))
	for preset in ["normal", "fast", "instant"]:
		lab.set_count(3); hand.clear_choice(); await settle()
		var initial: Array = lab.models.duplicate(true)
		lab.motion_lab.preset = preset
		for remaining in [2, 1]:
			lab.motion_lab.play({"operation": "use"}, initial)
			var started := Time.get_ticks_msec()
			while lab.motion_lab.playback.has_blocking_work() and Time.get_ticks_msec() - started < 2000: await process_frame
			await create_timer(.3).timeout; await settle()
			check(hand.order.size() == remaining and absf(hand_center(hand) - hand.scroll.get_global_rect().get_center().x) <= 1.0, "animated use centers surviving cards: %s %s" % [preset, remaining])
			initial = lab.motion_lab.final_models.duplicate(true)
		lab.motion_lab.play({"operation": "draw", "count": 1}, initial)
		lab.motion_lab.skip(); await create_timer(.3).timeout; await settle()
		check(hand.order.size() == 2 and absf(hand_center(hand) - hand.scroll.get_global_rect().get_center().x) <= 1.0, "skipped draw centers two arrived cards: " + preset)
	lab.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout; quit(0 if failures == 0 else 1)
