extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func pointer(point: Vector2) -> void:
	var event := InputEventMouseMotion.new()
	event.position = point; event.global_position = point
	root.push_input(event, true)
func capture(label: String) -> void:
	if DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="):
			root.get_texture().get_image().save_png(argument.trim_prefix("--lab-output=").path_join(label + ".png"))
func _run() -> void:
	root.size = Vector2i(1920, 1080)
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate()
	root.add_child(lab)
	lab.visual_context._options["reduced_motion"] = false
	lab.visual_context._options["animation_preset"] = "normal"
	lab.visual_context.capture_motion()
	lab.set_count(3)
	await create_timer(.5).timeout
	var hand = lab.hand
	var id: String = hand.order[1]
	var card: Control = hand.card_for(id)
	var start := card.position
	pointer(card.get_global_transform() * (card.size * .5))
	check(hand.interaction.hovered == id, "actual viewport mouse motion enters the card")
	var target: Vector2 = hand._emphasis[id].position
	check(card.position.is_equal_approx(start), "hover starts from the current pose without teleporting")
	await create_timer(.06).timeout
	check(card.position.y < start.y and card.position.y > target.y, "hover interpolates through an intermediate height")
	var midway := card.position
	pointer(Vector2(2, 2))
	await process_frame
	check(hand.interaction.hovered.is_empty(), "actual mouse exit clears hover")
	check(card.position.distance_to(midway) < 4, "reversing hover preserves the current pose without a jump")
	await create_timer(.08).timeout
	check(card.position.y > midway.y and card.position.y < start.y, "exit reverses from the intermediate pose toward rest")
	await capture("hover-returning")
	await create_timer(.5).timeout
	check(card.position.is_equal_approx(start), "leaving hover settles at the original position")
	lab.visual_context._options["reduced_motion"] = true
	lab.visual_context.capture_motion()
	hand.interaction.hovered = id
	hand._apply_emphasis()
	check(card.position.is_equal_approx(target), "reduced motion applies hover immediately")
	lab.queue_free()
	await process_frame
	print("HAND HOVER MOTION: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
