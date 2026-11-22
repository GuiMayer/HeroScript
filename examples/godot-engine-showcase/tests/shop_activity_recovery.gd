extends SceneTree
## Exercise the actual activity adapter and session protocol with a rejected shop write.
var failures := 0
class Gateway extends RefCounted:
	var writes: Array[String] = []
	var reads := 0
	var state := {"runId":"shop-qa", "settingId":"alisyum", "sequence":1, "step":7, "engineVersion":"31", "contentRevision":"revision", "stateHash":"hash", "lifecycle":"Active", "currentNodeId":"bazaar", "map":{"nodes":[{"nodeId":"bazaar", "activity":{"type":"Shop"}, "nextNodeIds":["forge", "camp"]}, {"nodeId":"forge", "activity":{"type":"CardUpgrade"}, "nextNodeIds":[]}, {"nodeId":"camp", "activity":{"type":"Preparation"}, "nextNodeIds":[]}], "resolvedNodeIds":[]}, "resources":{"gold":{"current":17}}, "shops":[{"shopId":"bazaar", "shopInstanceId":"shop", "nodeId":"bazaar", "rerollCosts":[{"resourceId":"gold", "amount":20}], "items":[{"itemId":"cheap", "cardId":"defend", "costs":[{"resourceId":"gold", "amount":10}]}, {"itemId":"expensive", "cardId":"strike", "costs":[{"resourceId":"gold", "amount":25}]}]}]}
	func replay_context() -> Array: return ["shop-fixture", 0]
	func prepare_command(_scope, _id, type, payload, _sequence, _step) -> Dictionary:
		return {"commandId":"command-" + type, "type":type, "payload":payload}
	func send_prepared(command: Dictionary) -> Dictionary:
		writes.append(str(command.type))
		if command.type == "BUY_SHOP_ITEM": return {"ok":false,"status":400,"error":"Insufficient gold"}
		if command.type == "RESOLVE_NODE":
			state.sequence += 1; state.step += 1; state.map.resolvedNodeIds.append("bazaar")
			return {"ok":true,"data":{"sequence":state.sequence,"state":{"resolution":null,"combat":null}}}
		return {"ok":false,"status":400,"error":"Unexpected command"}
	func read_session(_id: String, _expected := -1) -> Dictionary:
		reads += 1
		var commands := [{"type":"BUY_SHOP_ITEM", "validPayload":{"shopInstanceId":"shop","itemIds":["cheap", "expensive"]}}, {"type":"REROLL_SHOP","validPayload":{"shopInstanceId":"shop"}}, {"type":"RESOLVE_NODE","validPayload":{"currentNodeId":"bazaar"}}]
		if "bazaar" in state.map.resolvedNodeIds: commands = [{"type":"ADVANCE_NODE","targetNodeIds":["forge","camp"],"validPayload":{}}]
		var version := {"runId":state.runId,"settingId":state.settingId,"runSequence":state.sequence,"runStep":state.step,"engineVersion":state.engineVersion,"contentRevision":state.contentRevision,"stateFingerprint":state.stateHash}
		return {"ok":true,"data":{"version":version,"run":state.duplicate(true),"combat":null,"commands":{"commands":commands},"capabilities":{"granted":[]},"cardZones":{"zones":[]},"attributes":{},"cardPresentations":{},"modifierItems":{},"actions":{"candidates":[]},"evaluations":{"cards":[]}}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 8: await process_frame
func _run() -> void:
	var session = root.get_node("GameSession")
	root.get_node("I18n").set_locale("en")
	var gateway := Gateway.new(); session.configure(gateway); session._run = {"runId":"shop-qa"}
	check(await session.refresh(), "shop snapshot loads")
	var router = load("res://main.tscn").instantiate(); root.add_child(router); router.open_game()
	await settle()
	var screen = router.host.get_child(0)
	var blocked: Array = screen.view.choice_buttons.filter(func(b): return b.disabled)
	check(blocked.size() == 2 and blocked.any(func(b): return b.text.begins_with("REFRESH STOCK")) and blocked.any(func(b): return not b.text.begins_with("REFRESH STOCK")), "unaffordable offer and reroll are disabled")
	var cheap: Dictionary = session.activity_choices().filter(func(c): return str(c.get("subjectId")) == "cheap")[0]
	await screen._execute(cheap)
	await settle()
	check(session.synchronized and not session.busy and gateway.writes == ["BUY_SHOP_ITEM"] and gateway.reads == 2, "definitive rejection refreshes once and never resends purchase")
	check(router.current_screen == "activity" and session.run.resources.gold.current == 17, "failed purchase leaves the shop and balance intact")
	screen = router.host.get_child(0)
	var complete: Button = screen.view.choice_buttons.filter(func(b): return b.text == "COMPLETE STAGE")[0]
	check(complete.get_parent().get_parent().name == "FixedFooter", "completion is pinned outside the scrolling offers")
	if DisplayServer.get_name() != "headless":
		for arg in OS.get_cmdline_user_args():
			if arg.begins_with("--capture="):
				await RenderingServer.frame_post_draw
				check(root.get_texture().get_image().save_png(arg.trim_prefix("--capture=")) == OK, "captured pinned shop footer")
	check(not complete.disabled, "completion is usable after rejection")
	complete.pressed.emit(); await settle()
	check(gateway.writes == ["BUY_SHOP_ITEM", "RESOLVE_NODE"] and "bazaar" in session.run.map.resolvedNodeIds and router.current_screen == "map", "completion commits once and returns to the map")
	check(session.run.resources.gold.current == 17, "leaving spends no gold")
	router.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
