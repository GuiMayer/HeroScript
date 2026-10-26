extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func start_drag(hand, id: String, point: Vector2) -> void:
	var center: Vector2 = preload("res://scripts/ui/card_motion_pose.gd").read(hand.card_for(id)).center
	hand.interaction.pressed = id; hand.interaction.press_position = center; hand.interaction.dragging = true
	hand._drag_visual(point)
func pointer(hand, point: Vector2, pressed: bool) -> void:
	var event := InputEventMouseButton.new(); event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed
	event.global_position = point; event.position = hand.hit_surface.get_global_transform().affine_inverse() * point
	hand.pointer_input(event)
func _run() -> void:
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 8: await process_frame
	var hand = lab.hand; var models: Array = lab.models.slice(0, 3).duplicate(true)
	for preset in ["normal", "instant", "reduced"]:
		hand.sync_models(models, "events:" + preset)
		lab.visual_context.motion = preload("res://scripts/presentation/motion_policy.gd").capture("default", {"animation_preset": "instant" if preset == "instant" else "normal", "reduced_motion": preset == "reduced"})
		await create_timer(.16).timeout
		var ids: Array = hand.order.duplicate(); var id: String = ids[0]
		var center: Vector2 = preload("res://scripts/ui/card_motion_pose.gd").read(hand.card_for(id)).center
		var end: Vector2 = hand.destination_center(str(ids[-1]))
		pointer(hand, center, true)
		var motion := InputEventMouseMotion.new(); motion.global_position = end; motion.position = hand.hit_surface.get_global_transform().affine_inverse() * end
		hand.pointer_input(motion); pointer(hand, end, false)
		await create_timer(.2).timeout
		check(hand.order == [ids[1], ids[2], ids[0]] and hand._ghost == null and hand._transit.is_empty(), preset + ": complete pointer event path inserts and releases all owners")
		var confirmed: Array = hand.order.duplicate()
		start_drag(hand, id, hand.destination_center(str(ids[1])))
		check(not hand.move_visual_card(id, 0), "another input cannot mutate a live drag transaction")
		var escape := InputEventAction.new(); escape.action = "ui_cancel"; escape.pressed = true; hand._input(escape)
		await create_timer(.2).timeout
		check(hand.order == confirmed and hand._ghost == null, preset + ": Escape rolls back preview and gesture")
	hand.sync_models(models, "refresh-during-drag"); await create_timer(.15).timeout
	var id: String = hand.order[0]
	start_drag(hand, id, hand.destination_center(hand.order[-1]))
	hand.sync_models(models.slice(1), "refresh-during-drag")
	check(id not in hand.order and hand.card_for(id) == null and hand._ghost == null and hand._transit.is_empty(), "updated collection cancels and removes a dragged instance atomically")
	hand.sync_models(models, "modal"); await create_timer(.15).timeout
	id = hand.order[0]; var before: Array = hand.order.duplicate()
	start_drag(hand, id, hand.destination_center(hand.order[-1])); hand.reading_modal = true; hand.cancel_gesture()
	check(hand.order == before and hand._ghost == null, "modal interruption cancels preview without committing it")
	hand.reading_modal = false; hand.reorder_enabled = false
	check(not hand.move_visual_card(id, 2), "component policy disables every local reorder entry point")
	hand.reorder_enabled = true; hand.sync_models([], "empty")
	check(not hand.move_visual_card(id, 0), "empty hand cannot create an instance")
	hand.sync_models(models.slice(0,1), "single"); check(not hand.move_visual_card(str(models[0].cardInstanceId), 0), "single-card no-op does not invent a reordered event")
	await create_timer(.15).timeout
	id = hand.order[0]; var same: Vector2 = hand.destination_center(id)
	start_drag(hand, id, same); hand.finish_drag(id, same)
	check(hand._ghost == null and hand._return_tween == null, "an exact same-pose drop has no empty animation delay")
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
