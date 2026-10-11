extends SceneTree
## Real rendered captures of the shared components. No engine commands/network.
var failures := 0
var output_dir := ""
var captures: Array = []
var lab
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 12: await process_frame
func capture(state: String, locale: String) -> void:
	await settle()
	check(preload("res://tests/layout_inspector.gd").vertical_text_issues(lab).is_empty(), "readable capture: " + state)
	await RenderingServer.frame_post_draw
	var name := "%s-%s-%sx%s.png" % [state, locale, root.size.x, root.size.y]
	check(root.get_texture().get_image().save_png(output_dir.path_join(name)) == OK, "saved " + name)
	captures.append(name)
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	if output_dir.is_empty() or DisplayServer.get_name() == "headless": quit(2); return
	root.content_scale_size = Vector2i(1440, 900)
	root.content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	root.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_EXPAND
	root.gui_embed_subwindows = true
	var prefs = root.get_node("Preferences")
	var previous_profile := AppTheme.profile
	var saved := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	var prior_requests: int = root.get_node("GameServices").diagnostics().size()
	var prior_run: Dictionary = root.get_node("GameSession").run
	lab = load("res://scenes/card_hand_lab.tscn").instantiate()
	root.add_child(lab)
	prefs.reduced_motion = true; prefs.high_contrast = false
	for resolution in [Vector2i(1280, 720), Vector2i(2560, 1080)]:
		DisplayServer.window_set_size(resolution)
		root.size = resolution
		for language in ["en", "pt_BR"]:
			lab.set_language(language)
			lab.set_text_scale(1.2 if language == "pt_BR" else 1.0)
			lab.set_preset(0)
			await capture("hand", language)
			lab.set_preset(2)
			await capture("scaling", language)
			lab.select_card(str(lab.models[3].labId))
			lab.inspect_selected()
			await capture("inspection", language)
			for child in lab.get_children():
				if child is AcceptDialog: child.queue_free()
			await settle()
			lab.set_count(0)
			await capture("empty", language)
	root.size = Vector2i(1280, 720)
	lab.set_language("en"); lab.set_text_scale(1.0); lab.set_preset(3)
	prefs.high_contrast = true; lab._refresh()
	await capture("high-contrast", "en")
	prefs.high_contrast = false; lab._refresh(); lab.set_count(100)
	lab.hand.show_overview()
	await capture("overview-100", "en")
	for child in lab.hand.get_children():
		if child is AcceptDialog: child.queue_free()
	lab.queue_free()
	await settle()
	check(previous_profile == AppTheme.profile, "production profile restored")
	check(root.get_node("GameServices").diagnostics().size() == prior_requests and root.get_node("GameSession").run == prior_run, "no requests or run mutations")
	check(saved == (FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""), "preferences file untouched")
	var report := {"fixture": true, "captures": captures, "failures": failures, "rendered": true,
		"godot": Engine.get_version_info().string, "production_stretch": "1440x900 / canvas_items / expand",
		"font_weights": {"Cinzel": 600, "Source Sans 3": 500}, "captured_at_utc": Time.get_datetime_string_from_system(true)}
	var file := FileAccess.open(output_dir.path_join("captures.json"), FileAccess.WRITE)
	if file: file.store_string(JSON.stringify(report, "\t") + "\n"); file.close()
	else: check(false, "capture report writable")
	root.get_node("GameAudio").shutdown()
	await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
