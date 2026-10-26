extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 6: await process_frame
	var hand = lab.hand; var models: Array = lab.models.slice(0, 3).duplicate(true)
	hand.sync_models(models, "flow-test")
	var ids: Array = hand.order.duplicate(); hand.move_visual_card(str(ids[0]), 2)
	var new_model: Dictionary = models[0].duplicate(true); new_model.cardInstanceId = "incoming"
	var final: Array = models + [new_model]; var before := JSON.stringify(final)
	hand.begin_presentation(final)
	var steps := [{"sourceAddress": "deck", "targetAddress": "hand", "instanceIds": ["incoming"], "targetOrderAfter": ["incoming", ids[0], ids[1], ids[2]]}]
	hand.prepare_zone_beat(steps, func(address): return "hand" if address == "hand" else "pile")
	check(hand._destination_order == [ids[1], ids[2], ids[0], "incoming"], "published insertion projects onto local order and appends new arrivals")
	check(not hand.move_visual_card(str(ids[0]), 0), "canonical presentation disables local reorganizing")
	hand.settle_zone_beat(); hand.end_presentation(); hand.sync_models(final, "flow-test")
	check(hand.order == [ids[1], ids[2], ids[0], "incoming"] and JSON.stringify(final) == before, "settlement and canonical refresh preserve arrangement without mutating published models")
	var removed: Array = final.filter(func(model): return model.cardInstanceId != ids[1])
	hand.begin_presentation(removed); hand.end_presentation(); hand.sync_models(removed, "flow-test")
	check(hand.order == [ids[2], ids[0], "incoming"], "a used card disappears without resetting survivors")
	hand.begin_presentation(removed); hand.end_presentation(); hand.sync_models(removed, "flow-test")
	check(hand.order == [ids[2], ids[0], "incoming"], "retained cards keep their visual slot")
	hand.set_read_only(true)
	check(not hand.move_visual_card(str(ids[0]), 0), "busy/read-only collections cannot reorder")
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
