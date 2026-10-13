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
	var overlay := Control.new(); root.add_child(overlay)
	var handle = preload("res://scripts/presentation/presentation_handle.gd").new()
	var zones := {"zones": [{"zoneId": "arbitrary", "ownerId": "owner", "contentsVisible": false, "cards": []}]}
	executor.execute([{ "sourceAddress": "$run::owner::arbitrary", "targetAddress": "unknown", "instanceIds": ["secret"]}], [zones, zones], {}, func(_address, _id): return Vector2(300, 100), overlay, context, handle)
	check(overlay.get_child_count() == 1 and not overlay.get_child(0) is CardView, "hidden transfer uses a back without a secret identity or description")
	handle.finish("cancelled")
	for _frame in 4: await process_frame
	check(overlay.get_child_count() == 0 and handle.result == "cancelled", "cancel removes all transfer ghosts")
	check(not zones.zones[0].contentsVisible, "presentation never changes visibility policy")
	overlay.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
