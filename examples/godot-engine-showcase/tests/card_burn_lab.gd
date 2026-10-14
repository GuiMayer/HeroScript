extends SceneTree
var failures := 0
var output_dir := ""
func _init() -> void: call_deferred("run_checks")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 10: await process_frame
func play_burn(lab, direction: String) -> void:
	lab.animation_picker.select_animation("burn_" + direction)
	lab.animation_cycle.value = 1
	lab.play_animation_button.pressed.emit()
func capture(name: String) -> void:
	if output_dir.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output_dir.path_join(name + ".png")) == OK, "rendered " + name)
func run_checks() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output_dir = arg.trim_prefix("--lab-output=")
	root.size = Vector2i(1440, 900)
	root.get_node("Preferences").animation_preset = "normal"; root.get_node("Preferences").animation_speed = 1.
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	lab.visual_context._options["reduced_motion"] = false; lab.visual_context.capture_motion()
	await settle()
	check(lab.animation_picker.selected_id == "burn_center" and lab.animation_cycle.value == .5, "lab defaults to the center burn and production half-second cycle")
	lab.animation_cycle.value = 1
	check(lab.find_child("BurnCenterButton", true, false) == null and lab.find_child("BurnEdgesButton", true, false) == null, "one cascading selector replaces individual animation buttons")
	var results: Array = []
	lab.motion_lab.finished.connect(func(_models, metrics): results.append(metrics))
	for direction in ["center", "edges"]:
		lab.set_preset(0); await settle()
		lab.visual_context._options["reduced_motion"] = false
		lab.motion_lab.preset = "fast" if direction == "edges" else "normal"
		var chosen: String = lab.models[2].cardInstanceId
		var original_model: Dictionary = lab.hand.card_for(chosen).model.duplicate(true)
		lab.select_card(chosen)
		var expected_count: int = lab.models.size() - 1
		play_burn(lab, direction)
		var handle = lab.motion_lab.playback.active_handle
		check(handle != null and handle.claimed, direction + ": existing playback owns the burn handle")
		var animation: Tween = handle._tweens[0]
		var burn = lab.motion_lab.overlay.get_node("CardBurnView")
		var timing := {"started": 0, "finished": 0}
		burn.snapshot_ready.connect(func(): timing.started = Time.get_ticks_usec(), CONNECT_ONE_SHOT)
		handle.completed.connect(func(_result): timing.finished = Time.get_ticks_usec(), CONNECT_ONE_SHOT)
		var face: CardView = burn.capture_viewport.get_node("CapturedCardFace")
		check(face.model == original_model and not face.animate_entry and face.visual_style == lab.hand.card_for(chosen).visual_style, direction + ": snapshot uses the same complete CardView model/style without intro animation")
		check(burn.capture_viewport.transparent_bg and burn.capture_viewport.disable_3d and face.focus_mode == Control.FOCUS_NONE, direction + ": isolated transparent input-free 2D snapshot")
		check(burn.burn_material.get_shader_parameter("direction") == (1 if direction == "edges" else 0), direction + ": shader receives the selected direction")
		check(burn.get_global_transform().is_equal_approx(lab.hand.card_for(chosen).get_global_transform()), direction + ": snapshot preserves the original face transform without jumping")
		var independent = preload("res://scripts/ui/card_burn_view.gd").new()
		lab.motion_lab.overlay.add_child(independent)
		independent.setup(lab.hand.card_for(str(lab.models[0].cardInstanceId)), "edges")
		independent.set_progress(.7)
		check(independent.burn_material != burn.burn_material and burn.burn_material.get_shader_parameter("progress") == 0.0, direction + ": another material's progress cannot burn this card")
		independent.queue_free()
		while not burn.output.visible: await process_frame
		check(not lab.hand.card_for(chosen).visible and burn.progress < .1, direction + ": snapshot replaces the original without duplicate faces")
		await create_timer(.35).timeout
		await capture("burn-" + direction + "-middle")
		var paused_usec := 0
		if direction == "center":
			var before: float = burn.progress
			var pause_start := Time.get_ticks_usec()
			paused = true; await create_timer(.12).timeout
			check(is_equal_approx(burn.progress, before), "pause freezes shader progress, not global TIME")
			paused_usec = Time.get_ticks_usec() - pause_start
			paused = false
		while lab.motion_lab._running: await process_frame
		var active_seconds: float = (timing.finished - timing.started - paused_usec) / 1000000.0
		print("BURN_DURATION=", direction, " ", active_seconds)
		# Wall time varies with rendering and pause at frame boundaries; the actual
		# animation clock must complete the entire configured one-second interval.
		check(animation.get_total_elapsed_time() >= 1.0 and animation.get_total_elapsed_time() <= 1.15, direction + ": animation clock completes one second, including fast preset")
		check(active_seconds >= .90 and active_seconds <= 1.15, direction + ": measured rendered duration stays within frame-level tolerance")
		await settle()
		check(lab.hand.order.size() == expected_count and chosen not in lab.hand.order, direction + ": selected instance alone is consumed in the visual fixture")
		check(lab.motion_lab.overlay.get_child_count() == 0 and lab.hand.cards().values().all(func(card): return card.visible and not card.has_focus()), direction + ": final state has no burn nodes or focus outline leftovers")
		await capture("burn-" + direction + "-finished")
	# Instant/reduced/no-card cases must not allocate a viewport or create a delay.
	for mode in ["instant", "reduced"]:
		lab.set_preset(0); await settle()
		lab.motion_lab.preset = "instant" if mode == "instant" else "normal"
		lab.visual_context._options["reduced_motion"] = mode == "reduced"
		play_burn(lab, "center")
		check(not lab.motion_lab._running and not lab.motion_lab.playback.has_blocking_work() and lab.motion_lab.overlay.get_child_count() == 0, mode + ": no GPU snapshot, blocking handle or artificial wait")
	lab.set_count(0); play_burn(lab, "edges")
	check(not lab.motion_lab._running, "empty hand has no burn wait")
	lab.set_preset(0); await settle(); lab.motion_lab.preset = "normal"
	lab.visual_context._options["reduced_motion"] = false
	lab.motion_lab.options.auto_animations = false
	play_burn(lab, "center")
	check(lab.motion_lab.playback.active_handle == null and lab.motion_lab.overlay.get_child_count() == 0, "manual mode waits for explicit Next before allocating burn")
	lab.motion_lab.director.advance(); lab.motion_lab.skip(); await settle()
	check(not lab.motion_lab._running and lab.motion_lab.overlay.get_child_count() == 0 and lab.hand.order.size() == lab.models.size(), "skip during snapshot preparation disposes nodes and settles exactly once")
	lab.set_preset(0); await settle(); lab.motion_lab.options.auto_animations = true
	play_burn(lab, "edges"); await create_timer(.2).timeout
	lab.set_count(2); await settle()
	check(not lab.motion_lab._running and lab.motion_lab.overlay.get_child_count() == 0 and lab.hand.order.size() == 2, "changing fixtures safely cancels an active burn")
	play_burn(lab, "center"); lab.queue_free(); await settle()
	check(not is_instance_valid(lab), "closing the lab during capture disposes the viewport and director")
	root.get_node("GameAudio").shutdown()
	# The real audio backend releases Ogg playback on its mixer thread. Allow
	# shutdown to drain before exiting, as the integrated card-input harness does.
	await create_timer(.12).timeout
	await settle()
	print("CARD BURN LAB: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
