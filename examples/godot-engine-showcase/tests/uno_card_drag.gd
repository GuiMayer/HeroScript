extends SceneTree
const Pose = preload("res://scripts/ui/card_motion_pose.gd")
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 8: await process_frame
	lab.visual_context.motion = preload("res://scripts/presentation/motion_policy.gd").capture("default", {"animation_speed": 1.0, "reduced_motion": false})
	lab.hand.sync_models(lab.models.slice(0, 2), "drag-fixture")
	await create_timer(.15).timeout
	var hand = lab.hand; var id: String = hand.order[0]; var card: Control = hand.card_for(id)
	var pose := Pose.read(card)
	var previews: Array = []; var endings: Array = []
	hand.drag_previewed.connect(func(instance, point): previews.append([instance, point]))
	hand.drag_ended.connect(func(): endings.append(true))
	hand.interaction.pressed = id; hand.interaction.press_position = pose.center + Vector2(31, -52); hand.interaction.dragging = true
	var point: Vector2 = hand.interaction.press_position + Vector2(18, -9)
	hand._drag_visual(point)
	check(previews.size() == 1 and previews[0] == [id, point], "drag publishes visual-only target feedback using the current pointer")
	check(hand._ghost == card and card.get_parent() == hand and hand.is_in_transit(id), "drag borrows the real face with a single transform owner")
	check(Pose.read(card).center.distance_to(pose.center + Vector2(18, -9)) < .1, "drag keeps grab offset rather than centering on the pointer")
	hand.interaction.cancel_pointer(); hand._return_ghost()
	check(is_instance_valid(hand._ghost), "invalid drop keeps a visible return instead of deleting the face")
	await create_timer(.2).timeout
	check(hand._ghost == null and not hand.is_in_transit(id) and card.get_parent() == hand._slots[id], "return restores ownership and unlocks the original slot")
	var target: Dictionary = hand.destination_pose(id)
	check(Pose.read(card).center.distance_to(target.center) < 1 and absf(Pose.read(card).rotation-target.rotation) < .01, "two-card return ends at the complete adaptive fan pose")
	hand.interaction.pressed = id; hand.interaction.press_position = target.center; hand.interaction.dragging = true; hand._drag_visual(target.center + Vector2(100, 0))
	hand.cancel_gesture()
	check(hand._ghost == null and card.visible and not hand.is_in_transit(id), "navigation cancellation cleans ownership immediately")
	check(endings.size() == 2, "target feedback clears exactly once after each gesture")
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
