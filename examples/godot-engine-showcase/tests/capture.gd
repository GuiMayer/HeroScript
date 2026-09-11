extends SceneTree

var _original_last_run := ""

func _init() -> void:
	call_deferred("_capture")

func _capture() -> void:
	_original_last_run = str(root.get_node("Preferences").last_run_id)
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
	var texture := root.get_texture()
	if texture == null:
		push_error("The active display driver does not expose a viewport texture.")
		_finish(1)
		return
	var image := texture.get_image()
	var language := str(root.get_node("I18n").locale)
	var screen_name := "combat" if combat_capture else ("settings" if settings_capture else "menu")
	var output_name := "%s-%s.png" % [screen_name, language]
	var output := ProjectSettings.globalize_path("res://tests/output/%s" % output_name)
	DirAccess.make_dir_recursive_absolute(output.get_base_dir())
	var result := image.save_png(output)
	print("SHOWCASE_CAPTURE=", output, " RESULT=", result)
	_finish(0 if result == OK else 1)

func _finish(code: int) -> void:
	root.get_node("Preferences").last_run_id = _original_last_run
	root.get_node("Preferences").save()
	root.get_node("GameAudio").shutdown()
	create_timer(.15).timeout.connect(func(): process_frame.connect(
		func(): quit(code), CONNECT_ONE_SHOT), CONNECT_ONE_SHOT)
