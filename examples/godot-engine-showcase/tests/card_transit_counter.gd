extends SceneTree
const Motion = preload("res://scripts/ui/card_zone_motion.gd")
const Registry = preload("res://scripts/ui/card_transit_registry.gd")
const Handle = preload("res://scripts/presentation/presentation_handle.gd")
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func _run() -> void:
	for outcome in ["completed", "cancelled_before_start", "cancelled_midway", "skipped"]:
		var context = preload("res://scripts/ui/visual_context.gd").new(null, {"reduced_motion": true})
		var hand = preload("res://scripts/ui/card_hand_view.gd").create(context)
		hand.size = Vector2(1200, 520); root.add_child(hand)
		var models := [{"cardInstanceId": "bash", "definitionId": "w1_shield_bash", "name": "Shield Bash", "effects": ["Gain 5 Block."], "costs": []}]
		hand.sync_models(models, "counter-regression")
		for _frame in 6: await process_frame
		context._options.reduced_motion = false
		context.motion = preload("res://scripts/presentation/motion_policy.gd").capture("default", {})
		context.motion._values.draw = .1
		hand.begin_presentation(models)
		var card: Control = hand.card_for("bash")
		# A legitimate label with the same text must survive cleanup.
		var printed := Label.new(); printed.text = "×5"; card.add_child(printed)
		var original_children: int = card.get_child_count()
		var overlay := Control.new(); root.add_child(overlay)
		var owner = Registry.new(hand, overlay, context)
		var handle = Handle.new(); handle.begin()
		var steps := [{"sourceAddress": "$run::hero::stock", "targetAddress": "$run::hero::hand", "instanceIds": ["bash", "hidden-1", "hidden-2", "hidden-3", "hidden-4", "hidden-5"]}]
		var snapshots := [{"zones": [{"zoneId": "stock", "ownerId": "hero", "contentsVisible": true, "cards": [{"cardInstanceId": "bash"}]}]}]
		var before := JSON.stringify([steps, snapshots, models])
		Motion.execute(steps, snapshots, {}, func(address, id): return hand.destination_center(id) if address.ends_with("hand") else Vector2(80, 80),
			overlay, context, handle, func(address): return "hand" if address.ends_with("hand") else "pile", owner)
		check(owner._records.get("bash") == card and card.get_parent() == overlay, "executor borrows the actual hand card: " + outcome)
		check(card.get_child_count() == original_children + 1, "executor adds the aggregated counter during transit: " + outcome)
		if outcome == "completed":
			for tween in handle._tweens.duplicate():
				if tween.is_valid(): tween.pause(); tween.custom_step(1)
		else:
			if outcome == "cancelled_midway":
				for tween in handle._tweens.duplicate(): tween.pause(); tween.custom_step(.04)
			handle.finish("skipped" if outcome == "skipped" else "cancelled")
		check(handle.done and card.get_parent() != overlay and card.visible and hand.card_for("bash") == card,
			"same real card is returned and revealed: " + outcome)
		check(card.get_child_count() == original_children and printed.get_parent() == card,
			"counter is detached immediately while printed content survives: " + outcome)
		handle.finish("cancelled"); owner.dispose()
		await process_frame
		check(card.get_child_count() == original_children and overlay.get_child_count() == 0,
			"repeated cleanup leaves no counter or orphan transit: " + outcome)
		check(JSON.stringify([steps, snapshots, models]) == before and is_equal_approx(context.motion.duration("draw", true), .1),
			"published data and configured duration remain unchanged: " + outcome)
		hand.end_presentation(); hand.queue_free(); overlay.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
