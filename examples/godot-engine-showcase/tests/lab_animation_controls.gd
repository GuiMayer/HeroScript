extends SceneTree
var failures := 0
var output_dir := ""
func _init() -> void: call_deferred("run_checks")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 10: await process_frame
func type_seconds(lab, value: String) -> void:
	var field: LineEdit = lab.animation_cycle.get_line_edit()
	field.grab_focus(); field.select_all()
	for index in value.length():
		var event := InputEventKey.new()
		event.unicode = value.unicode_at(index); event.pressed = true
		root.push_input(event, true)
	var enter := InputEventKey.new(); enter.keycode = KEY_ENTER; enter.physical_keycode = KEY_ENTER; enter.pressed = true
	root.push_input(enter, true)
	enter.pressed = false; root.push_input(enter, true)
func capture(name: String) -> void:
	if output_dir.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output_dir.path_join(name + ".png")) == OK, "rendered " + name)
func run_checks() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output_dir = arg.trim_prefix("--lab-output=")
	root.size = Vector2i(1440, 900)
	root.gui_embed_subwindows = true
	root.get_node("Preferences").animation_preset = "normal"; root.get_node("Preferences").animation_speed = 1.
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab); await settle()
	lab.visual_context._options["reduced_motion"] = false
	var picker = lab.animation_picker
	check(picker._entries.size() == lab.motion_scenarios.size() + 2, "catalog includes every existing scenario and both burn variants")
	check(picker.get_popup().item_count == 3 and picker.get_popup().get_item_submenu_node(0).get_item_submenu_node(0).item_count == 2, "native recursive popup has categories and burn variants")
	check(picker.selected_id == "burn_center" and lab.animation_cycle.value == .5 and lab.animation_cycle.min_value == 0 and lab.animation_cycle.max_value == 30, "seconds field starts at the production cycle and has bounded zero-to-thirty range")
	var entry: Dictionary = picker._entries.burn_edges
	entry.popup.id_pressed.emit(entry.menu_id)
	check(picker.selected_id == "burn_edges" and picker.selected_scenario().direction == "edges" and not lab.motion_lab._running, "choosing a submenu leaf changes only selection, not playback")
	check(not picker.select_animation("missing") and picker.selected_id == "burn_edges", "unknown selection cannot replace a valid animation")
	lab.set_language("pt_BR")
	check(picker.selected_id == "burn_edges" and picker.text.contains("Queima") and picker.text.contains("Bordas"), "locale refresh preserves stable animation id and translates breadcrumbs")
	lab.animation_cycle.value = 2.5; lab.set_count(4)
	check(picker.selected_id == "burn_edges" and lab.animation_cycle.value == 2.5, "fixture refresh preserves animation and cycle duration")
	type_seconds(lab, "0.6"); await settle()
	check(is_equal_approx(lab.animation_cycle.value, .6) and picker.selected_id == "burn_edges" and not lab.motion_lab._running, "actual keyboard input commits seconds without resetting animation or starting playback")
	await capture("animation-controls-portuguese")
	if DisplayServer.get_name() != "headless":
		picker.show_popup(); await settle()
		var popup: PopupMenu = picker.get_popup()
		popup.set_focused_item(0)
		var right := InputEventKey.new(); right.keycode = KEY_RIGHT; right.physical_keycode = KEY_RIGHT; right.pressed = true
		# Feed the embedder, which routes native window input into the focused popup.
		# Emitting window_input or calling popup.push_input bypasses PopupMenu's handler.
		root.push_input(right, true); await settle()
		var category: PopupMenu = popup.get_item_submenu_node(0)
		check(category.visible, "keyboard opens the category submenu")
		category.set_focused_item(0); root.push_input(right, true); await settle()
		var variants: PopupMenu = category.get_item_submenu_node(0)
		check(variants.visible, "keyboard opens the nested burn variants")
		await capture("animation-cascade")
		variants.set_focused_item(0)
		var accept := InputEventKey.new(); accept.keycode = KEY_ENTER; accept.physical_keycode = KEY_ENTER; accept.pressed = true
		root.push_input(accept, true); await settle()
		check(picker.selected_id == "burn_center" and not lab.motion_lab._running, "keyboard chooses a leaf without starting playback")
		popup.hide()
	lab.set_language("en")
	# The catalog can grow a deeper branch without adding new UI controls.
	var extended: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_animation_catalog.json"))
	extended.categories.append({"label": "Future category", "items": [{"label": "Future subgroup", "items": [{"id": "future_draw", "label": "Future variant", "scenarioId": "draw_one"}]}]})
	picker.setup(extended, lab.motion_scenarios, root.get_node("I18n"))
	check(picker.select_animation("future_draw") and picker.selected_scenario().count == 1 and picker._entries.future_draw.path.size() == 3, "JSON extension adds a nested future animation without buttons or duplicate scenario definitions")
	picker.setup(JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_animation_catalog.json")), lab.motion_scenarios, root.get_node("I18n"))
	var measured: Array = []
	lab.motion_lab.finished.connect(func(_models, metrics): measured.append(metrics))
	for id in ["burn_center", "burn_edges", "use", "retained", "draw_five", "discard", "draw_discard", "create", "destroy"]:
		lab.set_preset(0); await settle(); lab.visual_context._options["reduced_motion"] = false
		picker.select_animation(id); lab.animation_cycle.value = .4
		var first: String = lab.models[0].cardInstanceId
		var selected: String = lab.models[2].cardInstanceId
		lab.select_card(selected)
		lab.play_animation_button.pressed.emit()
		check(lab.motion_lab._running, id + ": Play button starts the selected animation")
		# Editing the field while active must apply only to the next play.
		lab.animation_cycle.value = 2
		while lab.motion_lab._running: await process_frame
		var elapsed: float = measured[-1].elapsed_ms / 1000.0
		print("CYCLE_DURATION=", id, " ", elapsed)
		check(elapsed >= .32 and elapsed <= .65, id + ": full cycle fits 0.4 seconds instead of per-stage delays or the next field value")
		if id in ["burn_center", "burn_edges", "use"]: check(first in lab.hand.order and selected not in lab.hand.order, id + ": single-card animation uses the deliberately selected instance")
		if id == "retained": check(selected in lab.hand.order, "retained animation returns the selected instance")
		await settle()
		check(lab.motion_lab.overlay.get_child_count() == 0, id + ": cycle leaves no transit nodes")
	for id in ["burn_center", "draw_discard"]:
		lab.set_preset(0); await settle()
		picker.select_animation(id); lab.animation_cycle.value = 0
		lab.play_animation_button.pressed.emit()
		check(not lab.motion_lab._running and not lab.motion_lab.playback.has_blocking_work() and lab.motion_lab.overlay.get_child_count() == 0, id + ": zero-second cycle has no artificial wait")
	lab.set_preset(0); await settle()
	for id in ["empty", "missing_target", "zero_delta", "lifecycle"]:
		picker.select_animation(id); lab.animation_cycle.value = 30
		lab.play_animation_button.pressed.emit()
		while lab.motion_lab._running: await process_frame
		check(measured[-1].elapsed_ms < 150, id + ": facts with no visible animation never fill a requested thirty-second interval")
	picker.select_animation("burn_edges"); lab.animation_cycle.value = 30
	lab.play_animation_button.pressed.emit()
	check(lab.motion_lab.director.watchdog_seconds > 30, "long requested cycles are not cut off by the default watchdog")
	lab.motion_lab.skip(); await settle()
	var Burn = preload("res://scripts/ui/card_burn_motion.gd")
	check(Burn.cycle_seconds(NAN) == 1 and Burn.cycle_seconds(-1) == 0 and Burn.cycle_seconds(100) == 30, "invalid duration is safely normalized before tween creation")
	await capture("animation-controls-english")
	for resolution in [Vector2i(1280, 720), Vector2i(2560, 1080)]:
		root.size = resolution; await settle()
		var viewport_rect := Rect2(Vector2.ZERO, Vector2(root.size))
		check(viewport_rect.encloses(lab.play_animation_button.get_global_rect()) and viewport_rect.encloses(lab.animation_cycle.get_global_rect()) and viewport_rect.encloses(picker.get_global_rect()), "playback controls stay reachable at " + str(resolution))
	lab.queue_free(); await settle(); root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout; await settle()
	print("LAB ANIMATION CONTROLS: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
