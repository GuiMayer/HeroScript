extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var layout = preload("res://scripts/ui/hand_layout.gd")
	check(layout.insertion_index([100, 200, 300], 151, 0, 4) == 0 and layout.insertion_index([100, 200, 300], 155, 0, 4) == 1, "stable slot boundaries have hysteresis")
	check(layout.insertion_index([100, 200, 300], 290, 0, 4) == 2, "a fast move crosses multiple slots without adjacent-swap accumulation")
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 8: await process_frame
	var hand = lab.hand; hand.sync_models(lab.models.slice(0, 3), "preview-test")
	await create_timer(.18).timeout
	var ids: Array = hand.order.duplicate(); var id: String = ids[0]
	var pose: Dictionary = preload("res://scripts/ui/card_motion_pose.gd").read(hand.card_for(id))
	hand.interaction.pressed = id; hand.interaction.press_position = pose.center; hand.interaction.dragging = true
	var target: Vector2 = hand.destination_center(str(ids[2]))
	hand._drag_visual(target)
	check(hand.order == [ids[1], ids[2], ids[0]] and hand.arrangement.confirmed == ids, "neighbors preview the insertion without committing order")
	for _i in 3: await process_frame
	var layouts: int = hand.relayout_count
	for _i in 5: hand._drag_visual(target)
	for _i in 2: await process_frame
	check(hand.relayout_count == layouts, "stationary pointer does not rebuild or oscillate the slots")
	hand._drag_visual(Vector2(-200, -200))
	check(hand.order == ids and hand.arrangement.dragged.is_empty(), "leaving the hand retracts preview to confirmed order")
	hand.cancel_gesture(); lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
