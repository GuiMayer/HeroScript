extends "res://tests/loadout_input.gd"
## Explicit isolated API only. Never run against the player's production port.
func _run() -> void:
	var prefs = root.get_node("Preferences")
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-") or prefs.api_url != "http://127.0.0.1:61987":
		check(false, "live equipment test requires the isolated QA API on port 61987"); quit(1); return
	root.size = Vector2i(1920, 1080); root.content_scale_size = root.size
	var session = root.get_node("GameSession")
	check(await session.connect_engine("ascendant"), "connect isolated real REST setting")
	check(await session.start_campaign(int(Time.get_unix_time_from_system())), "create a dedicated QA run, not a player save")
	if failures: quit(1); return
	var screen = load("res://scenes/run_loadout.tscn").instantiate(); root.add_child(screen)
	var deadline := Time.get_ticks_msec() + 15000
	while screen.controller.inscriptions == null and Time.get_ticks_msec() < deadline: await process_frame
	await settle()
	check(screen.controller.inscriptions != null and screen.controller.inscriptions.source.canEquip, "load canonical owned inventory in an allowed equipment window")
	if failures: print(screen.shell.feedback.text); quit(1); return
	var id: String = screen.controller.inscriptions.source.items[0].instance.instanceId
	var slot: String = screen.controller.inscriptions.source.policy.slots[0].slotId
	var tile: Button = screen.inscriptions_view._inventory[id]; var target: Button = screen.inscriptions_view._positions[slot]
	var start := tile.get_global_rect().get_center(); var end := target.get_global_rect().get_center()
	move_mouse(start); await settle(); mouse_button(start, true); await settle(); move_mouse(start + Vector2(30, 0), true); await settle()
	check(root.gui_is_dragging(), "real run inventory supports native drag")
	move_mouse(end, true); await settle(); mouse_button(end, false)
	deadline = Time.get_ticks_msec() + 15000
	while (not screen.controller.editable() or screen.controller.inscriptions.assignments.get(slot) != id) and Time.get_ticks_msec() < deadline: await process_frame
	await settle()
	var response: Dictionary = await session.read_loadout(str(session.run.playerEntityId))
	check(response.ok and response.data.assignments.get(slot) == id and not screen.controller.busy, "native drop persists through actual REST preview and canonical APPLY_LOADOUT")
	if failures: print("LOADOUT_DIAGNOSTIC=", screen.shell.feedback.text, " / ", screen.controller.inscriptions.message, " / assignments=", screen.controller.inscriptions.assignments)
	screen.controller.edit_inscription("clear")
	deadline = Time.get_ticks_msec() + 15000
	while not screen.controller.editable() and Time.get_ticks_msec() < deadline: await process_frame
	await settle()
	response = await session.read_loadout(str(session.run.playerEntityId))
	check(response.ok and response.data.assignments.is_empty(), "clear saves an empty equipment state through the real API")
	move_mouse(start); mouse_button(start, true); mouse_button(start, false); await settle()

	move_mouse(end); mouse_button(end, true); mouse_button(end, false)
	deadline = Time.get_ticks_msec() + 15000
	while not screen.controller.editable() and Time.get_ticks_msec() < deadline: await process_frame
	await settle()
	response = await session.read_loadout(str(session.run.playerEntityId))
	check(response.ok and response.data.assignments.get(slot) == id, "native item-then-slot clicks also persist directly through the real API")
	var verified: Dictionary = await session.verify()
	check(verified.get("ok", false) and verified.data.get("isValid", false), "equipment command preserves semantic deterministic replay")
	screen.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
