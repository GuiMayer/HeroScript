extends SceneTree
## Offline regression: the shared hand must never draw or accept input above pause.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func motion(point: Vector2, held := false) -> void:
	var event := InputEventMouseMotion.new()
	event.position = point
	event.global_position = point
	if held: event.button_mask = MOUSE_BUTTON_MASK_LEFT
	root.push_input(event, true)
func click(point: Vector2, pressed: bool) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = pressed
	event.position = point
	event.global_position = point
	root.push_input(event, true)
func point_for(hand, index: int) -> Vector2:
	return hand.scroll.global_position + hand._layout.hitRects[index].get_center() - Vector2(hand.scroll.scroll_horizontal, 0)
func _run() -> void:
	var prefs = root.get_node("Preferences")
	var previous_motion: bool = prefs.reduced_motion
	prefs.reduced_motion = true
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await settle()
	router._clear_host()
	router.current_screen = "combat"
	var content := Control.new()
	router.host.add_child(content)
	var hand = load("res://scripts/ui/card_hand_view.gd").new()
	hand.position = Vector2(30, 260)
	hand.size = Vector2(1200, 450)
	content.add_child(hand)
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_hand_lab.json"))
	var models: Array = []
	for i in 8:
		var model: Dictionary = fixture.cards.values()[i % fixture.cards.size()].duplicate(true)
		model.cardInstanceId = "pause-%s" % i
		models.append(model)
	hand.sync_models(models, "pause-fixture")
	var intents: Array = []
	hand.card_activated.connect(func(id): intents.append(id))
	hand.drop_requested.connect(func(id, _point): intents.append(id))
	# Even the highest CanvasItem priority in gameplay must remain below pause.
	var marker := ColorRect.new()
	marker.color = Color(1, 0.2, 0.2)
	marker.position = Vector2(24, 24)
	marker.size = Vector2(40, 40)
	marker.z_index = RenderingServer.CANVAS_ITEM_Z_MAX
	marker.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(marker)
	await settle()
	for resolution in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = resolution
		await settle()
		hand.card_for("pause-3").grab_focus()
		await settle()
		check(hand._slots["pause-3"].z_index == 100, "fixture includes a raised shared card at %s" % resolution)
		router.toggle_pause()
		await settle()
		check(router.pause_canvas.layer > 0 and router.pause_layer.get_canvas() != hand.get_canvas() and router.pause_layer.get_parent() == router.pause_canvas,
			"entire pause canvas outranks every gameplay z priority at %s" % resolution)
		check(not router.host.can_process() and router.pause_layer.can_process(), "gameplay stops while pause remains interactive")
		var focus = root.gui_get_focus_owner()
		check(is_instance_valid(focus) and router.pause_layer.is_ancestor_of(focus), "pause owns keyboard/controller focus")
		check(router.pause_layer.theme == router.theme, "modal keeps the shared theme across the canvas boundary")
		var resume: Button = router.pause_layer.find_child("ResumeButton", true, false)
		var closing_canvas = router.pause_canvas
		click(resume.get_global_rect().get_center(), true)
		click(resume.get_global_rect().get_center(), false)
		check(not paused and router.pause_layer == null and closing_canvas.get_parent() == null, "Resume click closes and detaches the modal immediately")
		check(hand.card_for("pause-3").has_focus() and intents.is_empty(), "resume restores card focus without gameplay intent")
		await settle()
	hand.drag_override = true
	var point := point_for(hand, 3)
	motion(point)
	click(point, true)
	motion(Vector2(200, 100), true)
	await settle()
	check(hand.interaction.dragging and is_instance_valid(hand._ghost), "fixture includes an active drag ghost")
	router.toggle_pause()
	await settle()
	check(not hand.interaction.dragging and hand.interaction.pressed.is_empty() and not is_instance_valid(hand._ghost), "pause cancels drag and removes its ghost")
	click(Vector2(200, 100), false)
	if DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		var image := root.get_texture().get_image()
		var point_in_image := marker.get_global_rect().get_center() * Vector2(image.get_size()) / root.get_visible_rect().size
		var pixel := image.get_pixelv(Vector2i(point_in_image))
		check(pixel.r < 0.3, "rendered scrim dims even the maximum-z gameplay marker")
		for argument in OS.get_cmdline_user_args():
			if argument.begins_with("--pause-screenshot="):
				check(image.save_png(argument.trim_prefix("--pause-screenshot=")) == OK, "pause screenshot saved")
	var resume: Button = router.pause_layer.find_child("ResumeButton", true, false)
	click(resume.get_global_rect().get_center(), true)
	click(resume.get_global_rect().get_center(), false)
	click(point, false)
	check(intents.is_empty(), "a cancelled drag cannot play/drop after resume")
	prefs.reduced_motion = previous_motion
	paused = false
	router.queue_free()
	await settle()
	root.get_node("GameAudio").shutdown()
	print("PAUSE LAYERS: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
