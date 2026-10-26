extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 6: await process_frame
	var models: Array = lab.models.slice(0, 3).duplicate(true)
	var original := JSON.stringify(models)
	var hand = lab.hand; hand.sync_models(models, "scope:one")
	var ids: Array = hand.order.duplicate()
	check(hand.move_visual_card(str(ids[0]), 2) and hand.order == [ids[1], ids[2], ids[0]], "visual insertion changes displayed identities only")
	hand.sync_models(models, "scope:one")
	check(hand.order == [ids[1], ids[2], ids[0]] and JSON.stringify(models) == original, "refresh preserves local order and immutable models")
	var replacement = preload("res://scripts/ui/card_hand_view.gd").new(lab.visual_context); root.add_child(replacement)
	replacement.sync_models(models, "scope:one")
	check(replacement.order == hand.order, "remount of the same collection restores UI-session order")
	replacement.sync_models(models, "scope:other-branch")
	check(replacement.order == ids, "another branch/collection starts with canonical order")
	var store = preload("res://scripts/ui/hand_arrangement_store.gd").new()
	for i in 40: store.state_for(str(i))
	check(store._states.size() == store.LIMIT, "session cache is bounded")
	store.clear(); check(store._states.is_empty(), "session reset discards only cosmetic preferences")
	replacement.queue_free(); lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
