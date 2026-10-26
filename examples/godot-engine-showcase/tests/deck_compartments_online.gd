extends SceneTree
## Hard guard: never sends a command to the user's API or existing run.
var failures := 0
var output := ""
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func settle() -> void:
	for _frame in 12: await process_frame
func move_mouse(point: Vector2, held := false) -> void:
	var event := InputEventMouseMotion.new(); event.position = point; event.global_position = point; event.relative = Vector2(30, 0)
	event.button_mask = MOUSE_BUTTON_MASK_LEFT if held else 0; root.push_input(event)
func mouse_button(point: Vector2, pressed: bool) -> void:
	var event := InputEventMouseButton.new(); event.position = point; event.global_position = point
	event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed; root.push_input(event)
func _run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output = arg.trim_prefix("--lab-output=")
	if root.get_node("Preferences").api_url != "http://127.0.0.1:61988":
		check(false, "online compartment test requires the isolated API on port 61988"); quit(1); return
	root.size = Vector2i(1920, 1080); root.content_scale_size = root.size
	var session = root.get_node("GameSession")
	check(await session.connect_engine("compartment-lab"), "connect isolated real Deck Workshop setting")
	check(await session.start_campaign(int(Time.get_unix_time_from_system())), "create a dedicated laboratory run")
	if failures: quit(1); return
	check(not session.command("APPLY_DECK_COMPARTMENTS").is_empty(), "engine advertises the optional build command outside combat")
	var screen = load("res://scripts/ui/run_deck_compartments.gd").new(); screen.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT); root.add_child(screen)
	var deadline := Time.get_ticks_msec() + 15000
	while screen.workspace.source.is_empty() and Time.get_ticks_msec() < deadline: await process_frame
	await settle()
	check(screen.workspace.source.get("canChange", false), "editor loads actual character profile without offline fallback")
	if failures: print(screen._status.text); quit(1); return
	var cards: Array = screen.find_children("*", "Button", true, false).filter(func(button): return button.get_script() == load("res://scripts/ui/card_view.gd"))
	var first = cards[0]
	var id := str(first.model.cardInstanceId)
	var start: Vector2 = first.get_global_rect().get_center()
	var target: PanelContainer = screen._body.get_child(1)
	var end := target.global_position + Vector2(120, 140)
	print("DRAG_GEOMETRY=", first.get_global_rect(), " target=", target.get_global_rect(), " editor=", screen.get_global_rect())
	if not output.is_empty() and DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		root.get_texture().get_image().save_png(output.path_join("deck-compartments-before.png"))
	move_mouse(start); await settle(); mouse_button(start, true); await settle(); move_mouse(start + Vector2(30, 0), true); await settle()
	check(root.gui_is_dragging(), "native pointer starts a card drag from the shared CardView")
	move_mouse(end, true); await settle(); mouse_button(end, false); await settle()
	deadline = Time.get_ticks_msec() + 15000
	while (screen.workspace.preview.is_empty() or not screen.workspace.dirty()) and Time.get_ticks_msec() < deadline: await process_frame
	check(screen.workspace.draft.get(id) == "ward", "native drop stages the intended instance, not a neighboring card")
	check(not screen.workspace.preview.is_empty(), "real API validates the staged distribution")
	check(session.run.get("sequence") == screen.workspace.source.get("runSequence"), "preview does not advance the live run")
	if failures: print(screen._status.text); quit(1); return
	await screen._commit(); await settle()
	var confirmed: Dictionary = await session.read_deck_compartments()
	check(confirmed.get("ok", false) and confirmed.data.allocations.size() == 1, "Apply persists the allocation through the canonical session command")
	check(not screen.workspace.dirty() and not screen.busy, "editor returns to confirmed real state without refresh button")
	var second := ""
	for entry in confirmed.data.cards:
		if str(entry.cardInstanceId) != id and str(entry.definitionId) == "core_strike": second = str(entry.cardInstanceId); break
	screen._stage(second, "recovery")
	deadline = Time.get_ticks_msec() + 15000
	while screen.workspace.preview.is_empty() and Time.get_ticks_msec() < deadline: await process_frame
	check(not screen.workspace.preview.is_empty(), "mixed cost, condition and healing contribution previews over REST")
	await settle()
	if not output.is_empty() and DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		root.get_texture().get_image().save_png(output.path_join("deck-compartments.png"))
	var unchanged: Dictionary = await session.read_deck_compartments()
	check(unchanged.data.allocations.size() == 1, "unconfirmed second allocation is never shown as persisted")
	screen.queue_free(); await settle()
	var reopened = load("res://scripts/ui/run_deck_compartments.gd").new(); reopened.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT); root.add_child(reopened)
	deadline = Time.get_ticks_msec() + 15000
	while reopened.workspace.source.is_empty() and Time.get_ticks_msec() < deadline: await process_frame
	check(reopened.workspace.draft.size() == 1 and not reopened.workspace.dirty(), "reentry discards old draft and reloads the confirmed build")
	reopened.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
