extends SceneTree
## Real Kestrel only: production router, screens, controllers and published content.
## Run through tools/test-godot-ui.ps1 with an isolated API/store and --layout-smoke.
var failures := 0
var checks := 0
var session
var router
var output := ""
var history: Array = []
var travels := 0
var completed := 0
var returned_service := false
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	checks += 1
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 12: await process_frame
func idle() -> void:
	var deadline := Time.get_ticks_msec() + 15000
	var playback = root.get_node("Playback")
	while (session.busy or playback.has_blocking_work()) and Time.get_ticks_msec() < deadline: await process_frame
	await settle()
func screenshot(label: String) -> void:
	if output.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png(output.path_join(label + ".png"))
func geometry(view) -> Dictionary:
	var rects := {}
	for id in view.route.buttons: rects[id] = view.route.buttons[id].get_global_rect()
	return {"rects":rects, "viewport":view._scroll.get_global_rect(), "extent":view._canvas.custom_minimum_size, "origin":view.route.position,
		"scroll":Vector2(view._scroll.scroll_horizontal,view._scroll.scroll_vertical)}
func audit_map(label: String) -> void:
	if router.current_screen != "map": return
	var view = router.host.get_child(0).view
	var current := str(session.run.currentNodeId)
	var error: float = view.route.buttons[current].get_global_rect().get_center().distance_to(view._scroll.get_global_rect().get_center())
	check(error <= 2., label + ": current node centered (error %.3f px)" % error)
	var sequence: int = session.run.sequence
	view._inspector_toggle.pressed.emit(); await settle()
	var opened: bool = view._inspector_open
	var before := geometry(view)
	view._inspector_toggle.pressed.emit(); await settle()
	check(before == geometry(view), label + ": floating inspector does not move graph, nodes or viewport")
	check(opened != view._inspector_open and int(session.run.sequence) == sequence, label + ": inspector is local/read-only")
	var resolved: Array = session.run.get("map", {}).get("resolvedNodeIds", [])
	for node in view.route.nodes:
		check(bool(node.resolved) == (node.id in resolved), label + ": completion projection matches REST for " + str(node.id))
		if node.resolved and not node.current:
			check(view.route.buttons[node.id].get_node("Caption").text.begins_with("✓"), label + ": completed node has visible checkmark: " + str(node.id))
	await screenshot(label)
func combat_score(candidate: Dictionary) -> float:
	var player := str(session.run.playerEntityId)
	var result := 0.
	for app in candidate.get("applications", []):
		if app.get("resourceId") not in ["health", "block"]: continue
		var delta := float(app.get("currentValue", 0)) - float(app.get("previousValue", 0))
		if app.get("resourceId") == "health": result += maxf(0, delta) * 2 if app.get("targetEntityId") == player else maxf(0, -delta)
		if app.get("resourceId") == "block" and app.get("targetEntityId") == player: result += minf(6, maxf(0, delta)) * .8
	return result
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output = argument.trim_prefix("--lab-output=")
	if not output.is_empty(): DirAccess.make_dir_recursive_absolute(output)
	session = root.get_node("GameSession")
	var prefs = root.get_node("Preferences")
	prefs.reduced_motion = true; prefs.auto_animations = true; prefs.animation_speed = 4.
	router = load("res://main.tscn").instantiate(); root.add_child(router); await settle()
	check(await session.connect_engine("ascendant"), "connect production gateway to isolated real REST")
	check(await session.start_campaign(154), "create published Ascendant World One campaign, seed 154")
	root.mode = Window.MODE_WINDOWED; root.size = Vector2i(1920,1080)
	if failures == 0:
		router.open_game(); await idle()
		for iteration in 180:
			await idle()
			if failures or returned_service: break
			if session.run.get("lifecycle") != "Active":
				check(false, "run must remain alive until service/map round-trip"); break
			var screen = router.host.get_child(0)
			var node_id := str(session.run.currentNodeId)
			var previous: Dictionary = session.run.duplicate(true)
			var retained_json := JSON.stringify(previous)
			var choices: Array = session.activity_choices()
			print("FLOW=", JSON.stringify({"iteration":iteration,"screen":router.current_screen,"node":node_id,"sequence":session.run.sequence,"types":choices.map(func(c): return c.type)}))
			if router.current_screen == "combat" and session.combat.get("status") == "ACTIVE":
				var deadline := Time.get_ticks_msec() + 15000
				while screen.controller.locked() and Time.get_ticks_msec() < deadline: await process_frame
				check(not screen.controller.locked(), "combat production input unlocked")
				if failures: break
				var cards: Array = session.legal_actions.filter(func(c): return c.get("source") == "Card")
				cards.sort_custom(func(a,b): return combat_score(a) > combat_score(b))
				if not cards.is_empty(): await screen._play_candidate(cards[0])
				else: await screen._execute_system(screen.view._system_candidate(["END_TURN"]))
			elif router.current_screen == "map":
				await audit_map("map-%s" % iteration)
				var travel: Array = choices.filter(func(c): return c.type == "ADVANCE_NODE")
				if not travel.is_empty():
					# Prefer a service over another fight at the first real branch.
					var chosen: Dictionary = travel[0]
					for c in travel:
						if "forge" in str(c.subjectId) or "shop" in str(c.subjectId): chosen = c; break
					var target := str(chosen.subjectId)
					screen.view.route.buttons[target].pressed.emit(); await settle()
					check(screen.view._selected == target and screen.view._inspector_open, "node button selects destination and opens inspector")
					await screenshot("chosen-" + target)
					var action: Button = screen.view._inspector.find_child("MapAction_ADVANCE_NODE", true, false)
					check(action != null and not action.disabled, "selected destination exposes actual travel button")
					if failures: break
					action.pressed.emit(); await idle(); travels += 1
					check(str(session.run.currentNodeId) == target, "real REST travel reaches selected destination " + target)
				else:
					var actionable: Array = choices.filter(func(c): return c.type not in ["ABANDON_RUN","APPLY_CARD_MODIFIER_ITEM"])
					check(not actionable.is_empty(), "map has published entry action")
					if actionable.is_empty(): break
					await screen._intent("command", [screen.controller.option_reference(actionable[0])])
			elif router.current_screen == "activity":
				var resolutions: Array = choices.filter(func(c): return c.type == "RESOLVE_NODE")
				var rewards: Array = choices.filter(func(c): return c.type == "PICK_CARD_REWARD")
				if not rewards.is_empty():
					await screenshot("activity-" + node_id)
					await screen._execute(rewards[0]); await idle()
					check(node_id in session.run.map.resolvedNodeIds, "chosen reward auto-completes visited node via real REST: " + node_id)
					completed += 1
				elif not resolutions.is_empty():
					await screenshot("activity-" + node_id)
					await screen._return_to_map(); await idle()
					check(node_id in session.run.map.resolvedNodeIds, "return-to-map canonically completes service: " + node_id)
					completed += 1; returned_service = travels > 0
				else:
					var actionable: Array = choices.filter(func(c): return c.type not in ["ABANDON_RUN","APPLY_CARD_MODIFIER_ITEM"])
					check(not actionable.is_empty(), "activity has legal choices")
					if actionable.is_empty(): break
					await screen._execute(actionable[0])
					await idle()
					if node_id in session.run.map.resolvedNodeIds:
						check(true, "activity choice canonically completes selected node: " + node_id)
						completed += 1; returned_service = travels > 0
			else:
				var resolve: Array = choices.filter(func(c): return c.type == "RESOLVE_COMBAT")
				if not resolve.is_empty():
					check(await session.submit_activity(resolve[0]), "resolve actual combat through canonical gateway")
					router.open_game()
				else:
					check(false, "unexpected route " + router.current_screen); break
			await idle()
			history.append({"from":node_id,"to":session.run.currentNodeId,"screen":router.current_screen,"sequence":session.run.sequence,"resolved":session.run.map.resolvedNodeIds.duplicate()})
			check(JSON.stringify(previous) == retained_json, "retained pre-command snapshot remains unchanged")
		await audit_map("returned-map")
		check(travels > 0 and completed > 0 and returned_service, "real node selection → service → completion → map flow exercised")
		var verified: Dictionary = await session.verify()
		check(verified.get("ok", false) and verified.get("data", {}).get("isValid", false), "semantic replay verifies real run")
	print("MAP_REST_RESULT=", JSON.stringify({"checks":checks,"failures":failures,"travels":travels,"completed":completed,"history":history,"runId":session.run.get("runId")}))
	router.queue_free(); await settle()
	var transport = root.get_node("GameServices")._transport
	var drain := Time.get_ticks_msec() + 5000
	while (not transport._get_flights.is_empty() or not transport._queue.is_empty() or transport._idle.size() != transport._workers.size()) and Time.get_ticks_msec() < drain: await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
