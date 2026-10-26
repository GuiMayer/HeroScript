extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 8: await process_frame
	var hand = lab.hand; var models: Array = []
	for i in 30:
		var model: Dictionary = lab.models[i % lab.models.size()].duplicate(true)
		model.cardInstanceId = "overflow:%s" % i; models.append(model)
	hand.mode_override = "reading"; hand.sync_models(models, "overflow-test")
	lab.visual_context.motion = preload("res://scripts/presentation/motion_policy.gd").capture("default", {"animation_speed": 1.0})
	await create_timer(.2).timeout
	var bar: HScrollBar = hand.scroll.get_h_scroll_bar()
	check(bar.max_value - bar.page > 0, "fixture has a scrollable hand without changing card size or capacity")
	var id: String = hand.order[0]
	var center: Vector2 = preload("res://scripts/ui/card_motion_pose.gd").read(hand.card_for(id)).center
	hand.interaction.pressed = id; hand.interaction.press_position = center; hand.interaction.dragging = true
	var point: Vector2 = hand.hit_surface.global_position + Vector2(hand.hit_surface.size.x - 1, hand.hit_surface.size.y * .5)
	hand._drag_visual(point); hand.set_process(false)
	for _i in 120: hand._auto_scroll_reorder(1.0/120.0)
	check(hand.scroll.scroll_horizontal > 400 and hand.scroll.scroll_horizontal < 500, "auto-scroll is driven by seconds rather than event or frame count")
	hand._auto_scroll_reorder(100)
	for _i in 3: await process_frame
	hand._update_reorder_preview(point); hand.finish_drag(id, point)
	await create_timer(.2).timeout
	check(hand.order[-1] == id and hand.order.size() == 30, "first instance can be inserted at the global end beyond the initial viewport")
	var card: Control = hand.card_for(id); card.grab_focus()
	check(card.get_node_or_null(card.focus_neighbor_left) == hand.card_for(hand.order[-2]), "focus paths follow visual neighbors after the borrowed face returns to its slot")
	var before: int = hand.scroll.scroll_horizontal; hand._process(.5)
	check(hand.scroll.scroll_horizontal == before, "auto-scroll stops with the gesture and never runs at rest")
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
