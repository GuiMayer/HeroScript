extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 8: await process_frame
	var policy = preload("res://scripts/presentation/motion_policy.gd").capture("default", {"animation_speed": 1.0, "reduced_motion": false})
	lab.visual_context.motion = policy
	var hand = lab.hand; var id: String = hand.order[0]
	var original: Array = hand.order.duplicate()
	var overlay := Control.new(); root.add_child(overlay)
	var registry = preload("res://scripts/ui/card_transit_registry.gd").new(hand, overlay, lab.visual_context)
	var handle = preload("res://scripts/presentation/presentation_handle.gd").new(); handle.begin()
	preload("res://scripts/ui/card_use_motion.gd").execute(id, false, false, hand, registry, lab.visual_context, func(): return Vector2(500, 300), handle)
	await create_timer(.03).timeout
	handle.finish("cancelled")
	check(hand.order == original and hand.card_for(id).visible and not hand.is_in_transit(id) and registry._records.is_empty(), "cancelling departure restores its source instead of removing an uncommitted hand instance")
	registry.dispose()
	var card: Control = hand.card_for(id)
	var center: Vector2 = preload("res://scripts/ui/card_motion_pose.gd").read(card).center
	hand.interaction.pressed = id; hand.interaction.press_position = center; hand.interaction.dragging = true
	hand._drag_visual(center + Vector2(75, -40)); hand.interaction.cancel_pointer(); hand._return_ghost()
	var event := InputEventMouseButton.new(); event.button_index = MOUSE_BUTTON_LEFT; event.pressed = true; event.position = Vector2.ZERO
	hand.pointer_input(event)
	check(hand._ghost == null and not hand.is_in_transit(id), "a new press safely finishes a previous return before another gesture")
	var panel := Control.new(); root.add_child(panel); panel.position = Vector2(90, 70); panel.size = Vector2(300, 180)
	var rect := panel.get_rect()
	var panel_motion = preload("res://scripts/ui/panel_motion.gd")
	panel_motion.enter(panel, lab.visual_context)
	await create_timer(.13).timeout
	check(panel.modulate.a == 1 and panel.get_rect() == rect, "panel fade has no effect on layout or dimensions")
	policy.reduced = true
	check(panel_motion.enter(panel, lab.visual_context) == null and panel.modulate.a == 1, "reduced motion opens panels immediately without a hidden timer")
	panel.queue_free(); overlay.queue_free(); lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
