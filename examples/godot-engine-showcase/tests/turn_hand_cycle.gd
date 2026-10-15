extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run_checks")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 20: await process_frame
func run_checks() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var hand = preload("res://scripts/ui/card_hand_view.gd").new(context)
	hand.size = Vector2(1100, 600); root.add_child(hand)
	var old := {"cardInstanceId": "old", "name": "Outgoing", "summary": "Base effect"}
	var keep := {"cardInstanceId": "keep", "name": "Retained", "summary": "Base effect"}
	var fresh := {"cardInstanceId": "fresh", "name": "Incoming", "summary": "Base effect"}
	hand.sync_models([old, keep], "turn"); await settle()
	var before: Vector2 = hand.card_for("keep").global_position
	hand.begin_presentation([keep, fresh]); await settle()
	check(hand.card_for("keep").global_position.is_equal_approx(before), "staging final hand never moves a retained card before discard")
	check(not hand.card_for("fresh").visible, "next-turn draw stays hidden throughout previous activation")
	var role := func(address): return "hand" if address == "playable" else "pile"
	var discard := [{"sourceAddress": "playable", "targetAddress": "spent", "instanceIds": ["old"]}]
	hand.prepare_zone_beat(discard, role)
	check(hand._destination_order == ["keep"] and hand._presentation_order == ["old", "keep"], "discard reserves intermediate hand without committing it early")
	hand.begin_transit("old"); hand.finish_transit("old", false); hand.settle_zone_beat(); await settle()
	check(not hand.card_for("old").visible and not hand.card_for("fresh").visible and hand.card_for("keep").visible, "enemy activation sees only retained cards, without a premature redraw")
	hand.prepare_zone_beat([], role); hand.settle_zone_beat()
	check(hand._presentation_order == ["keep"], "no zone facts means no artificial hand movement")
	var draw := [{"sourceAddress": "library", "targetAddress": "playable", "instanceIds": ["fresh"]}]
	hand.prepare_zone_beat(draw, role)
	var destination: Vector2 = hand.destination_center("fresh")
	var slot_center: Vector2 = hand._slots.fresh.global_position + hand.card_for("fresh").size * .5
	check(slot_center.is_equal_approx(destination), "incoming hidden slot already matches flight destination, preventing a second jump")
	hand.finish_transit("fresh", true); hand.settle_zone_beat(); await settle()
	check(hand._presentation_order == ["keep", "fresh"] and hand.card_for("fresh").visible and not hand.card_for("old").visible, "new hand becomes visible only on draw arrival")
	hand.end_presentation(); hand.sync_models([keep, fresh], "turn"); await settle()
	check(hand.order == ["keep", "fresh"], "final reconciliation removes outgoing instances")
	hand.begin_presentation([fresh, keep])
	var redraw_discard := [{"sourceAddress": "playable", "targetAddress": "spent", "instanceIds": ["fresh"]}]
	hand.prepare_zone_beat(redraw_discard, role)
	hand.begin_transit("fresh"); hand.finish_transit("fresh", false); hand.settle_zone_beat()
	check(not hand.card_for("fresh").visible, "an instance redrawn later stays absent between discard and draw")
	var redraw := [{"sourceAddress": "library", "targetAddress": "playable", "instanceIds": ["fresh"], "targetOrderAfter": ["fresh", "keep"]}]
	hand.prepare_zone_beat(redraw, role)
	check(hand._destination_order == ["fresh", "keep"], "arrival respects authored insertion order rather than assuming append")
	check((hand._slots.fresh.global_position + hand.card_for("fresh").size * .5).is_equal_approx(hand.destination_center("fresh")), "redrawn instance reserves its arrival slot without a second layout flight")
	hand.finish_transit("fresh", true); hand.settle_zone_beat(); hand.end_presentation()
	hand.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
