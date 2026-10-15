extends SceneTree
## Viewport input against actual painted faces, not layout partition centers.
var failures := 0
var activated: Array = []
var output_dir := ""
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 10: await process_frame
func motion(point: Vector2) -> void:
	var event := InputEventMouseMotion.new(); event.position = point; event.global_position = point
	root.push_input(event, true)
func click(point: Vector2, pressed: bool) -> void:
	var event := InputEventMouseButton.new(); event.position = point; event.global_position = point
	event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed; root.push_input(event, true)
func top_face(hand, point: Vector2) -> String:
	var sorted: Array = hand.order.duplicate()
	sorted.sort_custom(func(a, b): return hand._slots[a].z_index > hand._slots[b].z_index or (hand._slots[a].z_index == hand._slots[b].z_index and hand._slots[a].get_index() > hand._slots[b].get_index()))
	for id in sorted:
		var card: Control = hand.card_for(id)
		if card.visible and card.get_parent() == hand._slots[id] and Rect2(Vector2.ZERO, card.size).has_point(card.get_global_transform().affine_inverse() * point): return str(id)
	return ""
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	root.size = Vector2i(1920, 1080)
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	lab.visual_context._options["reduced_motion"] = true; lab.visual_context.capture_motion()
	lab.set_count(8); await settle()
	var hand = lab.hand
	hand.drag_override = false
	hand.card_activated.connect(func(id): activated.append(id))
	var rect: Rect2 = hand._layout.hitRects[2]
	var start: Vector2 = hand.scroll.global_position + rect.get_center() - Vector2(hand.scroll.scroll_horizontal, 0)
	motion(start); await settle()
	var id: String = hand.interaction.hovered
	var seam: Vector2 = hand.scroll.global_position + Vector2(rect.end.x + 2, rect.get_center().y) - Vector2(hand.scroll.scroll_horizontal, 0)
	motion(seam); await settle()
	check(hand.interaction.hovered == id and top_face(hand, seam) == id, "raised face stays under the pointer across the former partition seam")
	if not output_dir.is_empty() and DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		check(root.get_texture().get_image().save_png(output_dir.path_join("hover-seam.png")) == OK, "rendered actual highlighted face at the former seam")
	var text: String = hand.hit_surface._get_tooltip(hand.hit_surface.get_global_transform().affine_inverse() * seam)
	check(hand.hit_surface._tooltip_id == id and text == hand.hit_surface.TOOLTIP_TOKEN, "tooltip resolves the same visible instance with a stable native token")
	var popup = hand.hit_surface._make_custom_tooltip(text)
	check(popup != null, "tooltip factory keeps the resolved instance")
	if popup != null:
		root.add_child(popup)
		var other_id: String = hand.order.filter(func(candidate): return candidate != id)[0]
		hand.set_selection(other_id)
		check(hand.hit_surface._tooltip_id == other_id and hand.hit_surface._tooltip_model == hand.card_for(other_id).model, "selection updates the existing tooltip immediately, without waiting a frame or hover timer")
		check(popup.get_child_count() == 1 and popup.visible, "tooltip refresh replaces its content without duplicate detail sections")
		hand._focus(id)
		check(hand.hit_surface._tooltip_id == id and hand.hit_surface._tooltip_model == hand.card_for(id).model, "keyboard focus refreshes an already open tooltip immediately")
		var updated: Dictionary = hand.card_for(id).model.duplicate(true)
		updated["summary"] = "Updated tooltip facts"
		hand.card_for(id).configure(updated)
		hand.hit_surface.follow_card(id)
		check(hand.hit_surface._tooltip_model.summary == "Updated tooltip facts" and popup.get_child_count() == 1, "updated facts refresh the same card tooltip without accumulating content")
		hand.hit_surface.follow_card("")
		check(not popup.visible, "leaving cards hides stale tooltip content immediately")
		popup.free()
		hand.set_selection("")
	click(seam, true); click(seam, false); await settle()
	check(activated == [id], "click on highlighted Fireball cannot select adjacent Heal")
	for resolution in [Vector2i(1280, 720), Vector2i(2560, 1080)]:
		root.size = resolution; await settle()
		for mode in ["adaptive", "reading"]:
			hand.mode_override = mode; hand.request_layout(); await settle()
			var mismatches := 0; var samples := 0
			for instance in hand.order:
				var card: Control = hand.card_for(instance)
				for y in [40.0, card.size.y * .5, card.size.y - 16]:
					for x in range(8, int(card.size.x), 24):
						var point: Vector2 = card.get_global_transform() * Vector2(x, y)
						if not hand.scroll.get_global_rect().has_point(point): continue
						var visible: String = top_face(hand, point)
						if visible.is_empty(): continue
						samples += 1
						if hand.pick_global(point, false) != visible: mismatches += 1
			check(samples > 0 and mismatches == 0, "painted-face picking respects rotation and overlap: %s %s" % [resolution, mode])
			var blank: Vector2 = hand.scroll.global_position + Vector2(2, 2)
			check(hand.pick_global(blank).is_empty(), "empty hand padding cannot select a card")
	hand.mode_override = "adaptive"; hand.request_layout(); await settle()
	var actor_card: Control = hand.card_for(id)
	hand.begin_transit(id)
	check(hand.pick_global(actor_card.get_global_transform() * (actor_card.size * .5), false) != id, "hidden or in-transit cards cannot capture input")
	hand.finish_transit(id); await settle()
	lab.visual_context._options["reduced_motion"] = false; lab.visual_context.capture_motion()
	var draw: Dictionary = lab.motion_scenarios.filter(func(item): return item.id == "draw_five")[0]
	lab.motion_lab.play(draw, lab.models); lab.motion_lab.skip(); await settle()
	var final_mismatches := 0
	for instance in hand.order:
		var card: Control = hand.card_for(instance)
		var point: Vector2 = card.get_global_transform() * Vector2(16, card.size.y - 16)
		if hand.scroll.get_global_rect().has_point(point) and hand.pick_global(point, false) != top_face(hand, point): final_mismatches += 1
	check(final_mismatches == 0, "purchase and skip reconcile clickable faces without stale hit partitions")
	lab.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
