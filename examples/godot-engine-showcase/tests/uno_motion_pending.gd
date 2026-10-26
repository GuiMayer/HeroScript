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
	var hand = lab.hand; var id: String = hand.order[0]; var card: Control = hand.card_for(id)
	var model := JSON.stringify(card.model)
	hand.interaction.pressed = id; hand.interaction.press_position = Pose.read(card).center; hand.interaction.dragging = true
	hand._drag_visual(hand.interaction.press_position + Vector2(120, -95))
	var release := Pose.read(card)
	hand.prepare_use(id); hand.set_read_only(true)
	check(hand._ghost == null and Pose.read(card).center.distance_to(release.center) < .01 and hand.is_in_transit(id), "pending submission preserves dragged release pose through input locking")
	await create_timer(.12).timeout
	check(Pose.read(card).center.distance_to(release.center) < .01 and JSON.stringify(card.model) == model, "slow network cannot move a pending drag into a game destination or mutate its model")
	hand.reject_use(id); hand.set_read_only(false); await create_timer(.18).timeout
	check(not hand.is_in_transit(id) and card.visible and card.modulate.a == 1, "rejection restores an interactive visible card without a second command")
	var overlay := Control.new(); root.add_child(overlay)
	hand.prepare_use(id, false)
	var before := Pose.read(card)
	var registry = preload("res://scripts/ui/card_transit_registry.gd").new(hand, overlay, lab.visual_context)
	var borrowed: Control = registry.acquire(id, card.model, true, before.center)
	check(borrowed == card and Pose.read(card).center.distance_to(before.center) < .01, "accepted receipt can take over the parked face without a duplicate departure")
	registry.release(id, borrowed, true); registry.dispose()
	overlay.queue_free(); lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
