extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").new(null, {}, load("res://data/themes/ivory_run.tres"))
	var zone := {"zoneId": "custom", "ownerId": "hero", "scopeId": "combat", "contentsVisible": true, "orderVisible": true, "count": 2, "cards": [{"cardInstanceId": "z", "definitionId": "defend", "upgrades": []}, {"cardInstanceId": "a", "definitionId": "basic_attack", "upgrades": [{"upgradeId": "different"}]}]}
	var presenter = load("res://scripts/presentation/combat_presenter.gd").new({"runId": "r", "sequence": 1}, {}, [], context.translator, {}, {"zones": [zone]})
	var projection = load("res://scripts/presentation/card_collection_presenter.gd")
	var model: Dictionary = projection.build(zone, presenter, context.translator)
	check(model.cards.map(func(card): return card.cardInstanceId) == ["z", "a"], "published order is preserved")
	check(model.cards.all(func(card): return card.availability == "Inspect card" and not card.costsKnown), "collection cards cannot pretend to be playable or free")
	zone.orderVisible = false; model = projection.build(zone, presenter, context.translator)
	check(not model.ordered and model.cards[0].cardInstanceId == "a", "hidden draw order uses a neutral catalog")
	zone.contentsVisible = false; model = projection.build(zone, presenter, context.translator)
	check(not model.visible and model.cards.is_empty() and model.count == 2, "hidden contents are distinct from empty and never projected")
	check(projection.find({"zones": [zone]}, {"zoneId": "custom", "ownerId": "other", "scopeId": "combat"}).is_empty(), "full zone address prevents crossing owners")
	zone.contentsVisible = true; model = projection.build(zone, presenter, context.translator)
	var dialog = load("res://scripts/ui/card_zone_dialog.gd").new(context); root.add_child(dialog); dialog.setup(model)
	dialog.popup_centered_clamped(Vector2i(1000, 700), .9)
	for _i in 10: await process_frame
	var requests: Array = []; dialog.card_requested.connect(func(id): requests.append(id))
	dialog.collection.cards.a.pressed.emit()
	check(requests == ["a"] and dialog.collection.cards.size() == 2, "shared card faces emit only a read request")
	dialog.collection._search.text = "defend"; dialog.collection._filter(); await process_frame
	check(dialog.collection.cards.z.visible and not dialog.collection.cards.a.visible, "collection search is local")
	check(root.get_node("GameServices").diagnostics().is_empty(), "opening/filtering a collection needs no HTTP or command")
	dialog.queue_free(); for _i in 8: await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout; quit(0 if failures == 0 else 1)
