extends SceneTree
## Actual viewport input plus identity/geometry regressions. No API, run or saved preferences.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func motion(point: Vector2, held := false) -> void:
	var event := InputEventMouseMotion.new()
	event.position = point
	event.global_position = point
	if held: event.button_mask = MOUSE_BUTTON_MASK_LEFT
	root.push_input(event, true)
func click(point: Vector2, pressed: bool) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = pressed
	event.position = point
	event.global_position = point
	root.push_input(event, true)
func point_for(hand, index: int) -> Vector2:
	return hand.scroll.global_position + hand._layout.hitRects[index].get_center() - Vector2(hand.scroll.scroll_horizontal, 0)

func _run() -> void:
	var prefs = root.get_node("Preferences")
	var session = root.get_node("GameSession")
	var before_state := JSON.stringify([session.run, session.combat])
	var prior := {"motion": prefs.reduced_motion, "mode": prefs.hand_layout_mode, "emphasis": prefs.hand_emphasis}
	prefs.reduced_motion = true
	prefs.hand_layout_mode = "adaptive"
	prefs.hand_emphasis = 1.0
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_hand_lab.json"))
	var models: Array = []
	for i in 8:
		var model: Dictionary = fixture.cards.volatile.duplicate(true) if fixture.cards.has("volatile") else fixture.cards.values()[0].duplicate(true)
		model["cardInstanceId"] = "instance-%s" % i
		model["rarityId"] = "rare"
		models.append(model)
	var hand = load("res://scripts/ui/card_hand_view.gd").new()
	hand.position = Vector2(20, 180)
	hand.size = Vector2(900, 500)
	root.add_child(hand)
	hand.sync_models(models, "scope-one")
	await settle()
	var activated: Array = []
	var drops: Array = []
	hand.card_activated.connect(func(id): activated.append(id); hand.set_selection(id))
	hand.drop_requested.connect(func(id, point): drops.append([id, point]))
	var card = hand.card_for("instance-2")
	var face = card.find_child("Rules", true, false)
	var layouts: int = hand.relayout_count
	var configs: int = card.configuration_count
	hand.sync_models(models.duplicate(true), "scope-one")
	await settle()
	check(hand.card_for("instance-2") == card and card.find_child("Rules", true, false) == face and card.configuration_count == configs,
		"identical snapshot preserves instance, face and configuration")
	check(hand.relayout_count == layouts, "identical snapshot causes zero relayouts")
	check(hand._layout.step < card.size.x and hand._layout.step >= hand.visual_style.minimum_step, "adaptive hand overlaps with a minimum selectable strip")
	var font: int = face.get_theme_font_size("normal_font_size")
	var neighbors: Array = hand.order.map(func(id): return hand._slots[id].position)
	var point := point_for(hand, 2)
	motion(point)
	await settle()
	check(hand.interaction.hovered == "instance-2" and card.position.y < 0 and is_zero_approx(card.rotation), "real pointer elevates and straightens the chosen card")
	for _i in 12: motion(point); await process_frame
	check(hand.interaction.hovered == "instance-2" and hand.order.map(func(id): return hand._slots[id].position) == neighbors,
		"stationary pointer never oscillates or moves neighboring slots")
	check(face.get_theme_font_size("normal_font_size") == font, "hover leaves effect fonts unchanged")
	card.visual_style = card.visual_style.duplicate()
	card.configure(card.model)
	var style_configs: int = card.configuration_count
	card.visual_style.rules_font_size += 1
	card.configure(card.model)
	check(card.configuration_count == style_configs + 1, "editing the same style resource invalidates only the affected face")
	hand.drag_override = false
	click(point, true)
	click(point, false)
	await settle()
	check(activated == ["instance-2"] and hand.interaction.selected == "instance-2", "press and release emit one instance intent")
	click(point, true)
	var other := point_for(hand, 3)
	motion(other, true)
	click(other, false)
	check(activated.size() == 1, "release over another card cancels rather than playing the wrong instance")
	hand.drag_override = true
	motion(point)
	click(point, true)
	var outside := Vector2(500, 80)
	motion(outside, true)
	await settle()
	check(hand.interaction.dragging, "actual pointer capture continues a drag outside the hand")
	click(outside, false)
	check(drops.size() == 1 and drops[0][0] == "instance-2" and activated.size() == 1, "drop emits one intent, never a duplicate click or REST command")
	motion(point); click(point, true); motion(outside, true)
	var cancel := InputEventAction.new()
	cancel.action = "ui_cancel"
	cancel.pressed = true
	root.push_input(cancel, true)
	click(outside, false)
	check(drops.size() == 1 and not hand.interaction.dragging, "Escape cancels drag without a drop")
	motion(point); click(point, true)
	hand.set_read_only(true)
	click(point, false)
	check(activated.size() == 1, "session lock cancels a pending pointer gesture")
	hand.set_read_only(false)
	card.grab_focus()
	load("res://scripts/ui/focus_navigation.gd").wire(hand)
	check(card.get_node(card.focus_neighbor_left) == hand.card_for("instance-1") and card.get_node(card.focus_neighbor_right) == hand.card_for("instance-3"), "focus neighbors follow instance order despite elevation")
	var inspections: Array = []
	hand.inspection_requested.connect(func(id): inspections.append(id))
	var inspect_button: Button = hand.find_child("HandInspectButton", true, false)
	click(inspect_button.get_global_rect().get_center(), true)
	click(inspect_button.get_global_rect().get_center(), false)
	check(inspections == ["instance-2"], "moving focus to the toolbar still inspects the last active instance")
	card.grab_focus()
	var enriched: Array = models.duplicate(true)
	enriched[2]["previewNote"] = "New authoritative facts"
	hand.sync_models(enriched, "scope-one")
	await settle()
	check(hand.card_for("instance-2") == card and card.has_focus() and hand.card_for("instance-1").configuration_count == 1, "enrichment preserves focus and leaves unrelated faces intact")
	enriched.remove_at(2)
	hand.sync_models(enriched, "scope-one")
	await settle()
	check(hand.interaction.focused == "instance-3", "removing the focused instance reveals its surviving neighbor")
	var remaining = hand.card_for("instance-1")
	hand.sync_models(models, "scope-two")
	await settle()
	check(hand.card_for("instance-1") != remaining and hand.interaction.selected.is_empty(), "different run/branch/actor scope never reuses stale interaction")
	for amount in [0, 1, 2, 5, 12, 40, 100]:
		var many: Array = []
		for i in amount:
			var model: Dictionary = models[0].duplicate(true)
			model.cardInstanceId = "large-%s" % i
			many.append(model)
		hand.sync_models(many, "large")
		await settle()
		check(hand.order.size() == amount and (amount < 2 or hand._layout.step >= hand.visual_style.minimum_step), "all instances reachable without a gameplay hand cap: %s" % amount)
		if amount > 1:
			hand.card_for(hand.order[-1]).grab_focus()
			await settle()
			check(hand.pick(point_for(hand, amount - 1) - hand.global_position) == hand.order[-1], "offscreen focus reveals the final instance: %s" % amount)
	hand.mode_override = "reading"
	hand.request_layout()
	await settle()
	check(hand._layout.step >= hand.card_for(hand.order[0]).size.x and hand._layout.rotations.all(func(angle): return is_zero_approx(angle)), "reading mode removes overlap and rotation without shrinking text")
	hand.scroll.scroll_horizontal = int(hand._layout.positions[20].x + 80)
	motion(point_for(hand, 20))
	await settle()
	check(is_instance_valid(hand._reading_proxy) and hand._reading_proxy.visible
		and hand._reading_proxy.mouse_filter == Control.MOUSE_FILTER_IGNORE and hand._reading_proxy.focus_mode == Control.FOCUS_NONE,
		"clipped hover uses a passive shared CardView without stealing focus or input")
	check(hand._reading_proxy.get_global_rect().position.x >= hand.scroll.get_global_rect().position.x
		and hand._reading_proxy.get_global_rect().end.x <= hand.scroll.get_global_rect().end.x,
		"overflow highlight shows the complete card without automatic hover scrolling")
	hand.show_overview()
	await settle()
	var overview = hand.get_child(hand.get_child_count() - 1)
	check(overview is AcceptDialog and overview.find_child("CardHandView", true, false).order.size() == 100,
		"overview is persistent and uses the same cards/hand for every instance")
	check(not hand._reading_proxy.visible, "opening a modal cancels transient hand highlights")
	overview.hand.card_for(overview.hand.order[4]).grab_focus()
	var inspect_action := InputEventAction.new()
	inspect_action.action = "inspect_card"
	inspect_action.pressed = true
	root.push_input(inspect_action, true)
	await settle()
	check(overview.get_children().any(func(child): return child is AcceptDialog), "inspection shortcut opens a child modal in the persistent overview")
	overview.hide(); overview.queue_free()
	var style = load("res://data/default_hand_visual_style.tres")
	var started := Time.get_ticks_usec()
	for _i in 1000: load("res://scripts/ui/hand_layout.gd").calculate(40, 900, Vector2(248, 328), style, "adaptive")
	print("HAND_LAYOUT_AVERAGE_US=", (Time.get_ticks_usec() - started) / 1000.0)
	hand.sync_models(models.map(func(model): return model.duplicate(true)), "benchmark")
	await settle()
	# Measure actual slot updates separately from pure math; not a GPU/frame benchmark.
	var benchmark_models: Array = []
	for i in 40:
		var model: Dictionary = models[0].duplicate(true)
		model.cardInstanceId = "bench-%s" % i
		benchmark_models.append(model)
	hand.sync_models(benchmark_models, "benchmark")
	await settle()
	started = Time.get_ticks_usec()
	for _i in 100: hand._relayout()
	print("HAND_SLOT_RELAYOUT_AVERAGE_US=", (Time.get_ticks_usec() - started) / 100.0)
	check(JSON.stringify([session.run, session.combat]) == before_state, "hand UX does not mutate any authoritative session snapshot")
	hand.queue_free()
	prefs.reduced_motion = prior.motion
	prefs.hand_layout_mode = prior.mode
	prefs.hand_emphasis = prior.emphasis
	root.get_node("GameAudio").shutdown()
	await settle()
	print("SHOWCASE_HAND_UX failures=", failures)
	quit(0 if failures == 0 else 1)
