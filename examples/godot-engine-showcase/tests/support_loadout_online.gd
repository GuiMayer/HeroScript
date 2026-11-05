extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func wait_until(predicate: Callable, timeout := 15000) -> bool:
	var deadline := Time.get_ticks_msec() + timeout
	while not predicate.call() and Time.get_ticks_msec() < deadline: await process_frame
	return predicate.call()
func run() -> void:
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-"):
		check(false, "real API test requires an isolated UI user directory"); quit(1); return
	var prefs = root.get_node("Preferences"); var previous: String = prefs.api_url
	prefs.api_url = "http://127.0.0.1:61989"; prefs.reduced_motion = true; prefs.auto_animations = true; prefs.changed.emit()
	var session = root.get_node("GameSession")
	check(await session.connect_engine("support-lab"), "connect dedicated Support Workshop")
	check(await session.start_campaign(2026110310), "create support proof run in disposable store")
	if failures: quit(1); return
	var router = load("res://main.tscn").instantiate(); root.add_child(router)
	await process_frame; router.show_run_loadout("companions")
	var page = router.host.get_child(0)
	check(await wait_until(func(): return page.controller.support.get("enabled", false) and page.controller.editable()), "live companion tab receives current actor inventory")
	if failures: quit(1); return
	var before: int = session.run.sequence
	page.controller.edit_support("support_sentinel", "support_defense")
	check(await wait_until(func(): return not page.controller.busy and page.controller.support.get("selection", {}).get("routineId") == "support_defense"), "Godot validates and autosaves the selected routine against real REST")
	check(int(session.run.sequence) == before + 1 and not session.has_pending_command, "autosave commits exactly one command")
	check(page.controller.can_leave(), "confirmed synchronized equipment permits returning to the run")
	var start: Dictionary = session.command("START_ENCOUNTER")
	check(await session.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})), "start encounter with captured equipped support")
	router.open_game()
	check(await wait_until(func(): return router.current_screen == "combat" and not router.host.get_child(0).controller.locked()), "combat uses normal presentation executor and releases input")
	if failures: quit(1); return
	var screen = router.host.get_child(0)
	var label: Label = screen.view.header_view.support_label
	check(label.visible and label.text.contains("3"), "floating header status displays engine-owned cadence without shifting controls")
	for activation in 3:
		check(await session.execute_combat_command("END_TURN", {"actorId": "player"}), "canonical actor activation %s completes through REST" % str(activation + 1))
		check(await wait_until(func(): return not session.busy and session.synchronized and not router.host.get_child(0).controller.locked()), "shared animation playback settles for activation %s" % str(activation + 1))
	var snapshot: Dictionary = await session.read_support_loadout()
	check(snapshot.get("ok", false) and snapshot.data.get("combat", {}).get("remainingOwnerActivations") == 3, "real support acts on third activation and consumes its cadence")
	check(not snapshot.data.canChange, "equipment is read-only during the active combat")
	root.get_node("Playback").clear(); router.queue_free()
	for _frame in 8: await process_frame
	prefs.api_url = previous; prefs.changed.emit(); root.get_node("GameAudio").shutdown()
	await create_timer(0.2).timeout
	quit(1 if failures else 0)
