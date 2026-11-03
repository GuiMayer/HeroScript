extends SceneTree
var failures := 0
func _init(): call_deferred("run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func fact(id: String) -> Dictionary:
	return {"identity": {"definitionId": "basic_attack", "cardInstanceId": id}, "tags": [], "costs": [], "effects": [], "conditions": []}
func run() -> void:
	var view = load("res://scenes/ui/overlays/card_payment.tscn").instantiate(); root.add_child(view)
	view.mount(load("res://scripts/ui/visual_context.gd").from_nodes(root.get_node("Preferences"), root.get_node("I18n")))
	view.command = {"actorId": "hero", "actionType": "PLAY_CARD", "cardInstanceId": "one", "targetIds": []}
	var requests: Array = []; var plays: Array = []
	view.preview_requested.connect(func(command, generation): requests.append({"command": command, "generation": generation}))
	view.play_requested.connect(func(preview): plays.append(preview))
	view.refresh_preview()
	check(view.get_ok_button().disabled and requests.size() == 1, "payment editor begins with a read-only preview and no enabled confirmation")
	var data := {"version": {"runSequence": 1}, "command": view.command.duplicate(true), "isLegal": false,
		"evaluation": {"legalTargetIds": ["enemy"], "affordableCostOptionIds": [], "failureReasons": ["Choose payment"], "paymentRequirements": [{"componentId": "cost", "paymentId": "fuel", "count": 1, "sourceZoneId": "custom", "costOptionId": null, "eligibleCardIds": ["fee"]}]},
		"cards": [fact("one"), fact("fee")], "applications": []}
	var original := JSON.stringify(data)
	view.accept({"ok": true, "data": data}, requests[0].generation)
	var card = view._cards["cost:fuel:fee"]
	check(card.scene_file_path == "res://scenes/ui/components/card_view.tscn", "fee selection uses the combat card scene")
	card.pressed.emit()
	check(requests.size() == 2 and requests[1].command.paymentSelections[0].cardInstanceIds == ["fee"] and plays.is_empty(), "picking a fee only changes the local intent and requests validation")
	check(view.get_ok_button().disabled, "every local change invalidates the previously accepted preview")
	var ready := data.duplicate(true); ready.isLegal = true
	view.accept({"ok": true, "data": ready}, requests[0].generation)
	check(view.get_ok_button().disabled, "out-of-order preview cannot enable Play")
	view.accept({"ok": true, "data": ready}, requests[1].generation)
	check(not view.get_ok_button().disabled and view._cards["cost:fuel:fee"] == card, "current preview enables Play without rebuilding cards")
	check(original == JSON.stringify(data), "payment rendering leaves the server snapshot immutable")
	var payload = load("res://scripts/engine/engine_gateway.gd").candidate_payload(requests[1].command)
	check(payload.paymentSelections == requests[1].command.paymentSelections, "command adapter preserves payment identities and chosen order")
	view.confirmed.emit(); check(plays.size() == 1, "only confirmation emits one gameplay intent")
	await process_frame
	var cancelled = load("res://scenes/ui/overlays/card_payment.tscn").instantiate(); root.add_child(cancelled)
	cancelled.mount(load("res://scripts/ui/visual_context.gd").from_nodes(root.get_node("Preferences"), root.get_node("I18n")))
	cancelled.play_requested.connect(func(_preview): plays.append({}))
	cancelled.cancel_selection(); await process_frame
	check(plays.size() == 1, "cancel closes the selector without any gameplay intent")
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
