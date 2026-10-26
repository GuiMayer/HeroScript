extends RefCounted
## Drives the production hand gesture; no second hand, layout or animation executor.
static func execute(frame: Dictionary, hand, handle) -> void:
	if hand.order.is_empty(): handle.hit(); handle.finish(); return
	var index := clampi(int(frame.get("fromIndex", 0)), 0, hand.order.size()-1)
	var destination := clampi(int(frame.get("toIndex", hand.order.size()-1)), 0, hand.order.size()-1)
	var id: String = hand.order[index]
	var start: Dictionary = preload("res://scripts/ui/card_motion_pose.gd").read(hand.card_for(id))
	var point: Vector2 = hand.destination_center(hand.order[destination])
	handle.claimed = true
	var completed := func():
		if not handle.done: handle.hit(); handle.finish()
	hand.reorder_settled.connect(completed, CONNECT_ONE_SHOT)
	handle.cleanup(func():
		if not is_instance_valid(hand): return
		if hand.reorder_settled.is_connected(completed): hand.reorder_settled.disconnect(completed)
		if handle.result != "completed": hand.cancel_gesture())
	hand.interaction.pressed = id; hand.interaction.press_position = start.center; hand.interaction.dragging = true
	hand._drag_visual(point)
	if frame.get("cancel", false): hand.interaction.cancel_pointer(); hand._return_ghost(); hand._relayout()
	else: hand.finish_drag(id, point)
	# Observe the current hand's settlement, not stale tweens replaced by reflow.
	hand._check_reorder_settled()
