extends SceneTree
## Never use player saves. This test is deliberately restricted to an isolated QA API.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func idle(screen) -> void:
	var deadline := Time.get_ticks_msec() + 25000
	while (screen.controller.busy or screen.controller._reading or screen.controller._deck_reading or screen.controller.inscriptions == null and not screen.controller.info_ready) and Time.get_ticks_msec() < deadline:
		await process_frame
	for _frame in 6: await process_frame
func _run() -> void:
	if root.get_node("Preferences").api_url != "http://127.0.0.1:61989":
		check(false, "online unified loadout requires isolated API port 61989"); quit(1); return
	var session = root.get_node("GameSession")
	for setting in ["ascendant", "compartment-lab"]:
		check(await session.connect_engine(setting), "connect real setting: " + setting)
		check(await session.start_campaign(80741 if setting == "ascendant" else 80742), "create isolated QA run: " + setting)
		if failures: quit(1); return
		var screen = load("res://scripts/ui/unified_loadout_screen.gd").new(); root.add_child(screen); await idle(screen)
		check(screen.controller.info_ready, "load canonical relic and attribute projections")
		if failures: print(screen.controller.message); quit(1); return
		var sequence: int = int(session.run.sequence)
		for tab in ["inscriptions", "deck", "relics", "companions", "overview"]:
			screen._select_tab(tab); await idle(screen)
		check(int(session.run.sequence) == sequence, "all tab navigation is read-only")
		if setting == "ascendant":
			var workspace = screen.controller.inscriptions
			check(workspace != null and not workspace.source.items.is_empty(), "current character has actual inscription inventory")
			if failures: quit(1); return
			var item: String = workspace.source.items[0].instance.instanceId
			var slot: String = workspace.source.policy.slots[0].slotId
			screen.controller.edit_inscription("stage_drop", [item, slot, ""])
			screen._select_tab("deck"); await idle(screen)
			var read: Dictionary = await session.read_loadout(str(session.run.playerEntityId))
			check(read.get("ok", false) and read.data.assignments.get(slot) == item and int(session.run.sequence) == sequence + 1, "inscription autosaves once while another tab is visible")
		else:
			check(screen.controller.inscriptions == null, "setting without inscriptions has no offline fallback")
			var workspace = screen.controller.compartments
			check(workspace != null and workspace.source.canChange, "load actual configurable compartments")
			if failures: print(screen.controller.message); quit(1); return
			var id: String = workspace.source.cards[0].cardInstanceId
			screen.controller.move_card(id, "ward"); screen._select_tab("companions"); await idle(screen)
			var read: Dictionary = await session.read_deck_compartments()
			check(read.get("ok", false) and read.data.allocations.size() == 1 and int(session.run.sequence) == sequence + 1, "compartment autosaves once without Apply while changing tabs")
		var run_id: String = session.run.runId
		screen.queue_free(); await process_frame
		check(await session.continue_run(run_id), "reopen persisted run")
		screen = load("res://scripts/ui/unified_loadout_screen.gd").new(); root.add_child(screen); await idle(screen)
		check(screen.controller.info_ready and screen.controller.can_leave(), "reentry loads confirmed build without pending draft")
		var verified: Dictionary = await session.verify_run(run_id)
		check(verified.get("ok", false) and verified.data.get("isValid", false), "autosave preserves semantic deterministic replay")
		screen.queue_free(); await process_frame
		var main = load("res://main.tscn").instantiate(); root.add_child(main)
		main.show_run_loadout("deck")
		screen = main.host.get_child(0); await idle(screen)
		check(main.current_screen == "run_loadout" and screen.shell.active_tab == "deck", "real game router opens the unified page in the requested module")
		screen._back(); await process_frame; await process_frame
		check(main.current_screen != "run_loadout", "Back returns through the actual run router")
		main.queue_free(); await process_frame
	quit(1 if failures else 0)
