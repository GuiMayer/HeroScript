extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 8: await process_frame
	var hand = lab.hand; hand.sync_models(lab.models.slice(0, 3), "drop-test")
	lab.visual_context.motion = preload("res://scripts/presentation/motion_policy.gd").capture("default", {"animation_speed": 1.0})
	await create_timer(.18).timeout
	var ids: Array = hand.order.duplicate(); var id: String = ids[0]
	var drops: Array = []; var changes: Array = []; var activations: Array = []
	hand.drop_requested.connect(func(instance, _point): drops.append(instance))
	hand.visual_order_changed.connect(func(value): changes.append(value))
	hand.card_activated.connect(func(instance): activations.append(instance))
	var start: Dictionary = preload("res://scripts/ui/card_motion_pose.gd").read(hand.card_for(id))
	hand.interaction.pressed = id; hand.interaction.press_position = start.center; hand.interaction.dragging = true
	var end: Vector2 = hand.destination_center(str(ids[2])); hand._drag_visual(end)
	hand.finish_drag(id, end)
	check(hand.arrangement.confirmed == [ids[1], ids[2], ids[0]] and changes.size() == 1, "drop commits insertion instead of swapping neighbors")
	check(drops.is_empty() and activations.is_empty() and hand.interaction.selected.is_empty(), "hand drop never emits a gameplay drop, synthesized click or default selection")
	await create_timer(.2).timeout
	var pose: Dictionary = preload("res://scripts/ui/card_motion_pose.gd").read(hand.card_for(id))
	var target: Dictionary = hand.destination_pose(id)
	check(hand._ghost == null and not hand.is_in_transit(id) and pose.center.distance_to(target.center) < 1 and absf(pose.rotation-target.rotation) < .01, "shared return executor snaps into the full destination fan pose")
	hand.interaction.pressed = id; hand.interaction.press_position = pose.center; hand.interaction.dragging = true; hand._drag_visual(Vector2(-200,-200))
	hand.finish_drag(id, Vector2(-200,-200)); await create_timer(.2).timeout
	check(drops == [id] and hand.order == [ids[1], ids[2], ids[0]], "outside drop keeps gameplay routing and returns to the committed visual slot")
	hand.reorder_drop_filter = func(_point): return false
	check(not hand.contains_reorder_point(end), "overlaid controls can exclude their area from local reorder")
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
