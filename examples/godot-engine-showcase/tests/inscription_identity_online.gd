extends SceneTree
## Integration through the production Godot session and an isolated API; never player saves.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func idle(screen) -> void:
	var deadline := Time.get_ticks_msec() + 25000
	while (screen.controller.busy or screen.controller._reading) and Time.get_ticks_msec() < deadline: await process_frame
	for _frame in 6: await process_frame
func _run() -> void:
	if root.get_node("Preferences").api_url != "http://127.0.0.1:61989":
		check(false, "requires isolated API port 61989"); quit(1); return
	root.size = Vector2i(1920, 1080); root.content_scale_size = root.size
	var session = root.get_node("GameSession")
	check(await session.connect_engine("prototype-test"), "connect actual MVP content")
	check(await session.start_campaign(80749), "create isolated MVP run")
	if failures: quit(1); return
	var screen = load("res://scenes/ui/screens/unified_loadout.tscn").instantiate()
	screen.initial_tab = "inscriptions"; root.add_child(screen); await idle(screen)
	var workspace = screen.controller.inscriptions
	check(workspace != null and workspace.source.canEquip, "current character can equip outside combat")
	if failures: quit(1); return
	check(screen.inscriptions_view.identity_slots.get_child_count() == 1 and screen.inscriptions_view.slot_grid.get_child_count() == 6, "live API policy mounts one identity and six common positions")
	var rows: Array = workspace.source.items
	var focus: String = rows.filter(func(row): return row.definition.inscriptionId == "prototype_identity_focus")[0].instance.instanceId
	var ward: String = rows.filter(func(row): return row.definition.inscriptionId == "prototype_identity_ward")[0].instance.instanceId
	var common: String = rows.filter(func(row): return row.definition.inscriptionId == "prototype_edge")[0].instance.instanceId
	var sequence := int(session.run.sequence)
	for id in [focus, ward]:
		screen.inscriptions_view._inventory[id].pressed.emit()
		screen.inscriptions_view._positions.identity.pressed.emit(); await idle(screen)
		var read: Dictionary = await session.read_loadout(str(session.run.playerEntityId))
		sequence += 1
		check(read.get("ok", false) and read.data.assignments.get("identity") == id and int(session.run.sequence) == sequence, "identity choice saves once through preview and canonical command")
		if failures: print(screen.controller.message); quit(1); return
	screen.inscriptions_view.drop_requested.emit(common, "capacity_6", ""); await idle(screen)
	var read: Dictionary = await session.read_loadout(str(session.run.playerEntityId))
	check(read.get("ok", false) and read.data.assignments.get("capacity_6") == common and read.data.assignments.get("identity") == ward, "common drop saves without displacing identity")
	var verified: Dictionary = await session.verify_run(str(session.run.runId))
	check(verified.get("ok", false) and verified.data.get("isValid", false), "real Godot autosave replays deterministically")
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="):
			await process_frame; await RenderingServer.frame_post_draw
			root.get_texture().get_image().save_png(arg.trim_prefix("--lab-output=").path_join("identity-loadout-online.png"))
	screen.queue_free(); await process_frame; root.get_node("GameAudio").shutdown()
	quit(1 if failures else 0)
