extends SceneTree
## New MVP composition: real production views, no HTTP or player save writes.
var failures := 0
func _init() -> void: run.call_deferred()
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func settle() -> void:
	for frame in 18: await process_frame
func run() -> void:
	var before: Dictionary = root.get_node("GameSession").run.duplicate(true)
	var view = load("res://scenes/ui/components/combat_mvp_view.tscn").instantiate()
	view.size = Vector2(1920, 1080)
	root.add_child(view)
	view.show_editor_preview()
	await settle()
	view.apply_interaction({"synchronized": true})
	var hand = view.hand
	var source_models: Array = hand.ordered_cards().map(func(card): return card.model.duplicate(true))
	var context = view.visual_context
	var expected_size: Vector2 = context.card_style.effective_size(context.text_scale)
	var expected_style: String = context.card_style.presentation_key()
	var duration_keys := ["hover", "draw", "card_flight", "discard", "exile", "hand_transition"]
	var expected_durations: Array = duration_keys.map(func(key): return context.capture_motion().duration(key, true))
	check(not view.has_node("CombatBody") and not view.has_node("Balance"), "new MVP tree has no old composition or spacer ancestors")
	check(view.bottom_hud.get_parent() == view.gameplay_column and view.turn_view.get_parent() == view.gameplay_column, "hand host and provisional controls are independent siblings")
	for bounds in [Vector2(1920,1080), Vector2(1280,720), Vector2(1280,800), Vector2(2560,1080)]:
		view.size = bounds
		await settle()
		var canvas: Rect2 = view.gameplay_column.get_global_rect()
		check(is_equal_approx(canvas.size.x / canvas.size.y, 16.0 / 9.0) and view.get_global_rect().encloses(canvas), "16:9 composition fits its available bounds " + str(bounds))
		check(absf(canvas.get_center().x - view.get_global_rect().get_center().x) < 1, "composition stays centered " + str(bounds))
		check(canvas.encloses(view.header_view.get_global_rect()), "provisional header stays accessible " + str(bounds))
		for count in [0,1,2,5,12]:
			var models: Array = []
			for index in count:
				var model: Dictionary = source_models[index % source_models.size()].duplicate(true)
				model.cardInstanceId = "base-%s" % index
				models.append(model)
			hand.sync_models(models, "base-%s" % count, "Your hand is empty.")
			await settle()
			check(hand.order.size() == count and absf(hand.scroll.get_global_rect().get_center().x - canvas.get_center().x) < 1, "hand stays centered with %s cards at %s" % [count, bounds])
			check(hand.get_global_rect().end.y <= canvas.end.y + 1, "hand reserves its bottom edge with %s cards at %s" % [count, bounds])
			check(hand.ordered_cards().all(func(card): return card.size.is_equal_approx(expected_size)), "card dimensions remain unchanged with %s cards at %s" % [count, bounds])
			if count == 2:
				var first: Vector2 = hand.destination_center(hand.order.front())
				var last: Vector2 = hand.destination_center(hand.order.back())
				check(absf((first.x + last.x) * .5 - canvas.get_center().x) < 1, "two-card fan is centered by its actual destinations " + str(bounds))
				var selected: String = hand.order.back()
				var point: Vector2 = hand.card_for(selected).get_global_transform() * (hand.card_for(selected).size * .5)
				check(hand.pick_global(point, false) == selected, "hit surface resolves the visible card after canvas fitting " + str(bounds))
		var position: Vector2 = hand.global_position
		view._build_hand()
		view._layout_gameplay()
		await settle()
		check(hand.get_parent() == view.hand_section and hand.global_position.is_equal_approx(position), "refresh does not move or reparent the hand " + str(bounds))
	check(context.card_style.presentation_key() == expected_style, "composition does not mutate card style")
	check(duration_keys.map(func(key): return context.capture_motion().duration(key, true)) == expected_durations, "composition preserves all sampled motion durations")
	check(root.get_node("GameSession").run == before, "local composition test does not mutate the session")
	view.queue_free()
	await process_frame
	quit(1 if failures else 0)
