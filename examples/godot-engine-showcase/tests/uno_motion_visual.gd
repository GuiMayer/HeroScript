extends SceneTree
var failures := 0
var output := ""
var lab
var samples: Array[float] = []
var last_tick := 0
var fps_cap := 60
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func capture(label: String) -> void:
	if output.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output.path_join(label + ".png")) == OK, "rendered " + label)
func wait_cycle() -> void:
	var started := Time.get_ticks_msec()
	while lab.motion_lab._running and Time.get_ticks_msec()-started < 6000:
		await process_frame
		var now := Time.get_ticks_usec()
		if last_tick > 0: samples.append((now-last_tick) / 1000.0)
		last_tick = now
	check(not lab.motion_lab._running and lab.motion_lab.overlay.get_child_count() == 0, "rendered cycle settles without orphan visuals")
	last_tick = 0
func _run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output = arg.trim_prefix("--lab-output=")
		if arg.begins_with("--capture-phase=fps"):
			var requested: int = arg.trim_prefix("--capture-phase=fps").to_int()
			if requested in [30, 60, 120]: fps_cap = requested
		if arg.begins_with("--capture-resolution="):
			var parts := arg.trim_prefix("--capture-resolution=").split("x"); root.size = Vector2i(int(parts[0]), int(parts[1]))
	Engine.max_fps = fps_cap
	var prefs = root.get_node("Preferences")
	prefs.reduced_motion = false; prefs.animation_speed = 1.0; prefs.animation_preset = "normal"
	lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 12: await process_frame
	lab.visual_context._options["reduced_motion"] = false; lab.visual_context._options["animation_speed"] = 1.0
	lab.visual_context.motion = preload("res://scripts/presentation/motion_policy.gd").capture("default", {"animation_speed": 1.0})
	lab.hand.mode_override = "adaptive"
	lab.hand.sync_models(lab.models.slice(0, 2), "visual-motion"); lab.hand.request_layout()
	await create_timer(.2).timeout; await capture("two-card-fan")
	var id: String = lab.hand.order[0]
	var pose: Dictionary = preload("res://scripts/ui/card_motion_pose.gd").read(lab.hand.card_for(id))
	lab.hand.interaction.pressed = id; lab.hand.interaction.press_position = pose.center + Vector2(30, -35); lab.hand.interaction.dragging = true
	lab.hand._drag_visual(lab.hand.interaction.press_position + Vector2(120, -65))
	await capture("drag-grip")
	lab.hand.interaction.cancel_pointer(); lab.hand._return_ghost(); await create_timer(.18).timeout
	await capture("returned-fan")
	for operation in ["draw", "use", "retained", "burn"]:
		lab.hand.clear_choice(); lab.hand.sync_models(lab.models, "visual-motion")
		await create_timer(.2).timeout
		lab.motion_lab.play({"operation": operation, "count": 5, "direction": "center", "cycleSeconds": .5 if operation == "burn" else 1.0}, lab.models)
		await create_timer(.15).timeout; await capture(operation + "-in-flight")
		await wait_cycle(); await capture(operation + "-settled")
	samples.sort()
	print("UNO_RENDER_METRIC=", JSON.stringify({"frames": samples.size(), "p95_frame_ms": samples[int((samples.size()-1)*.95)] if not samples.is_empty() else 0, "resolution": root.size, "fps_cap": Engine.max_fps}))
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
