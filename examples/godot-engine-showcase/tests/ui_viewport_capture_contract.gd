extends SceneTree
## The renderer's actual pixel dimensions, never the requested file name, are
## the baseline authority. Offscreen viewports also work on smaller monitors.
var failures := 0
var output := ""
var records: Array = []
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--capture="): output = arg.trim_prefix("--capture=")
	if not output.is_empty(): DirAccess.make_dir_recursive_absolute(output)
	for dimensions in [Vector2i(960,540),Vector2i(1280,720),Vector2i(1280,800),Vector2i(1440,900),Vector2i(1920,1080),Vector2i(2560,1080)]:
		var viewport := SubViewport.new(); viewport.size = dimensions; viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
		root.add_child(viewport)
		var bg := ColorRect.new(); bg.color = Color.WHITE; viewport.add_child(bg); bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		var menu = preload("res://scenes/ui/screens/main_menu.tscn").instantiate(); viewport.add_child(menu); menu.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		var translator = root.get_node("I18n"); translator.set_locale("pt_BR")
		var context = preload("res://scripts/ui/visual_context.gd").new(translator,{},preload("res://data/themes/ivory_theme.tres"))
		menu.mount(context)
		menu.apply_model({"settingName":"Alisyum", "description":"", "selectedSettingId":"alisyum", "settings":[{"settingId":"alisyum","displayName":"Alisyum"},{"settingId":"prototype-test","displayName":"MVP"}], "available":true,"connecting":false,"pending":false})
		for _i in 8: await process_frame
		check(viewport.size == dimensions, "viewport %s" % dimensions)
		if DisplayServer.get_name() != "headless":
			await RenderingServer.frame_post_draw
			var bitmap := viewport.get_texture().get_image()
			check(bitmap.get_size() == dimensions, "renderer pixel size %s" % dimensions)
			if not output.is_empty():
				var file := "menu-%sx%s.png" % [dimensions.x,dimensions.y]
				check(bitmap.save_png(output.path_join(file)) == OK, "captured " + file)
				records.append({"file":file,"requested":[dimensions.x,dimensions.y],"actual":[bitmap.get_width(),bitmap.get_height()],"locale":"pt_BR","state":"fixture: campaign choice","scale":context.text_scale})
		viewport.queue_free(); await process_frame
	if not output.is_empty():
		var report := FileAccess.open(output.path_join("captures.json"),FileAccess.WRITE)
		if report: report.store_string(JSON.stringify({"renderer":DisplayServer.get_name(),"godot":Engine.get_version_info().string,"captures":records},"\t"))
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
