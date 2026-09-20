extends SceneTree

var _original_last_run := ""
var _original_scale := 1.0
var _original_contrast := false
var _original_resolution := Vector2i.ZERO
var _original_fullscreen := false

func _init() -> void:
	call_deferred("_capture")

func _capture() -> void:
	_original_last_run = str(root.get_node("Preferences").last_run_id)
	_original_scale = root.get_node("Preferences").text_scale
	_original_contrast = root.get_node("Preferences").high_contrast
	_original_resolution = root.get_node("Preferences").window_resolution
	_original_fullscreen = root.get_node("Preferences").fullscreen
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--capture-resolution="):
			var dimensions := argument.trim_prefix("--capture-resolution=").split("x")
			if dimensions.size() == 2:
				root.get_node("Preferences").fullscreen = false
				root.get_node("Preferences").set_resolution(Vector2i(int(dimensions[0]), int(dimensions[1])), false)
	if "--capture-accessibility" in OS.get_cmdline_user_args():
		root.get_node("Preferences").text_scale = 1.2
		root.get_node("Preferences").high_contrast = true
	var scene = load("res://main.tscn").instantiate()
	root.add_child(scene)
	var combat_capture := "--capture-combat" in OS.get_cmdline_user_args()
	var settings_capture := "--capture-settings" in OS.get_cmdline_user_args()
	if settings_capture:
		scene.show_settings()
	var deadline := Time.get_ticks_msec() + 20000
	var frame := 0
	while Time.get_ticks_msec() < deadline:
		await process_frame
		if not combat_capture or scene.get_meta("capture_ready", false):
			if frame >= 11:
				break
		frame += 1
	await create_timer(.25).timeout
	if combat_capture and scene.current_screen != "combat":
		push_error("Combat capture did not reach the combat screen.")
		_finish(1)
		return
	if "--capture-timeline" in OS.get_cmdline_user_args():
		scene.show_timeline()
		var timeline = scene.host.get_child(0)
		for _attempt in 200:
			await create_timer(.025).timeout
			if timeline.history_view.presenter != null: break
		await create_timer(.2).timeout
	var texture := root.get_texture()
	if texture == null:
		push_error("The active display driver does not expose a viewport texture.")
		_finish(1)
		return
	var image := texture.get_image()
	var language := str(root.get_node("I18n").locale)
	var screen_name := "combat" if combat_capture else ("settings" if settings_capture else "menu")
	if "--capture-timeline" in OS.get_cmdline_user_args(): screen_name = "timeline"
	var output_name := "%s-%s.png" % [screen_name, language]
	var output := ProjectSettings.globalize_path("res://tests/output/%s" % output_name)
	DirAccess.make_dir_recursive_absolute(output.get_base_dir())
	var result := image.save_png(output)
	print("SHOWCASE_CAPTURE=", output, " RESULT=", result)
	_finish(0 if result == OK else 1)

func _finish(code: int) -> void:
	root.get_node("Preferences").last_run_id = _original_last_run
	root.get_node("Preferences").text_scale = _original_scale
	root.get_node("Preferences").high_contrast = _original_contrast
	root.get_node("Preferences").window_resolution = _original_resolution
	root.get_node("Preferences").fullscreen = _original_fullscreen
	root.get_node("Preferences").save()
	root.get_node("GameAudio").shutdown()
	create_timer(.15).timeout.connect(func(): process_frame.connect(
		func(): quit(code), CONNECT_ONE_SHOT), CONNECT_ONE_SHOT)
