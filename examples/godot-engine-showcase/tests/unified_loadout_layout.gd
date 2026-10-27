extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func _run() -> void:
	var output := ""
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output = argument.trim_prefix("--lab-output=")
	var translator = root.get_node("I18n"); var prefs = root.get_node("Preferences")
	for resolution in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = resolution; root.content_scale_size = resolution
		for language in ["en", "pt_BR"]:
			translator.set_locale(language); prefs.text_scale = 1.2 if language == "pt_BR" else 1.0
			var lab = load("res://scenes/unified_loadout_lab.tscn").instantiate(); root.add_child(lab)
			for _frame in 12: await process_frame
			for tab in ["inscriptions", "deck", "companions"]:
				lab.shell.select(tab)
				for _frame in 6: await process_frame
				check(lab.shell.buttons.companions.get_global_rect().end.x <= resolution.x, "tabs remain within viewport: %s/%s/%s" % [resolution, language, tab])
				check(load("res://tests/layout_inspector.gd").vertical_text_issues(lab).is_empty(), "no squeezed vertical text: %s/%s/%s" % [resolution, language, tab])
				if not output.is_empty():
					await RenderingServer.frame_post_draw
					root.get_texture().get_image().save_png(output.path_join("%s-%s-%dx%d.png" % [tab, language, resolution.x, resolution.y]))
			lab.shell._drawer_open = true; lab.shell._responsive(); await process_frame
			check(lab.shell.attributes_panel.get_global_rect().end.x <= resolution.x + 1, "attribute panel fits viewport")
			check(lab.shell.attributes_panel.get_parent() == lab.shell if resolution.x == 1280 else lab.shell.attributes_panel.get_parent() == lab.shell.body, "responsive drawer reserves no empty column")
			lab.queue_free(); await process_frame
	quit(1 if failures else 0)
