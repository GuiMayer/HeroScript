extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var executor = preload("res://scripts/ui/card_zone_motion.gd")
	check(executor.address_key({"zoneId": "arbitrary", "ownerId": "owner", "scopeId": null}) == "$run::owner::arbitrary", "published address identity is independent of pile purpose")
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var policy = preload("res://scripts/presentation/motion_policy.gd").capture("default", {})
	var transfers := [{"id": "first", "operation": "draw"}, {"id": "first", "operation": "discard"}, {"id": "other", "operation": "discard"}]
	var scheduled: Array = executor.schedule(transfers, policy, func(id): return id == "first")
	check(is_equal_approx(scheduled[0].duration, policy.duration("draw", true)) and is_equal_approx(scheduled[1].duration, policy.duration("staged_discard", true)), "schedule shares the production durations including staged discard")
	check(scheduled[0].following and not scheduled[1].following and scheduled[1].delay >= scheduled[0].duration, "same-instance passages are serial while other cards remain concurrent")
	check(is_equal_approx(scheduled[2].delay, minf(2 * policy.duration("stagger", true), policy.duration("batch_span", true))) and not transfers[0].has("duration"), "schedule caps stagger and never mutates transfer facts")
	policy._values.max_transfers = 2
	check(executor.schedule(transfers, policy).size() == 2, "schedule respects the published transfer animation budget")
	var burn_steps := [{"flowId": "card.played.to-exhaust", "sourceAddress": "visible-source", "targetAddress": "unmounted-pile", "instanceIds": ["burned"]}]
	var burn_snapshots := [{"zones": [{"contentsVisible": true, "cards": [{"cardInstanceId": "burned"}]}]}]
	check(executor.resolve(burn_steps, burn_snapshots, func(address, _id): return Vector2(100, 100) if address == "visible-source" else Vector2.INF, policy).size() == 1, "published burn needs no destination pile anchor")
	var overlay := Control.new(); root.add_child(overlay)
	var handle = preload("res://scripts/presentation/presentation_handle.gd").new()
	var zones := {"zones": [{"zoneId": "arbitrary", "ownerId": "owner", "contentsVisible": false, "cards": []}]}
	executor.execute([{ "sourceAddress": "$run::owner::arbitrary", "targetAddress": "unknown", "instanceIds": ["secret"]}], [zones, zones], {}, func(address, _id): return Vector2(100, 100) if address == "$run::owner::arbitrary" else Vector2(500, 100), overlay, context, handle)
	check(overlay.get_child_count() == 1 and not overlay.get_child(0) is CardView, "hidden transfer uses a back without a secret identity or description")
	handle.finish("cancelled")
	for _frame in 4: await process_frame
	check(overlay.get_child_count() == 0 and handle.result == "cancelled", "cancel removes all transfer ghosts")
	check(not zones.zones[0].contentsVisible, "presentation never changes visibility policy")
	var instant = preload("res://scripts/presentation/presentation_handle.gd").new()
	executor.execute([{ "sourceAddress": "same", "targetAddress": "same", "instanceIds": ["secret"]}], [zones, zones], {}, func(_address, _id): return Vector2(300, 100), overlay, context, instant)
	check(instant.done and overlay.get_child_count() == 0, "zero displacement never starts an artificial animation")
	overlay.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
