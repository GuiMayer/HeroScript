extends SceneTree

func _init() -> void:
	call_deferred("_capture")

func _capture() -> void:
	var scene = load("res://main.tscn").instantiate()
	root.add_child(scene)
	var combat_capture := "--capture-combat" in OS.get_cmdline_user_args()
	for _frame in (180 if combat_capture else 12):
		await process_frame
		if not combat_capture or scene.get_meta("capture_ready", false):
			if _frame >= 11:
				break
	await create_timer(.25).timeout
	var texture := root.get_texture()
	if texture == null:
		push_error("The active display driver does not expose a viewport texture.")
		quit(1)
		return
	var image := texture.get_image()
	var output_name := "combat.png" if combat_capture else "menu.png"
	var output := ProjectSettings.globalize_path("res://tests/output/%s" % output_name)
	DirAccess.make_dir_recursive_absolute(output.get_base_dir())
	var result := image.save_png(output)
	print("SHOWCASE_CAPTURE=", output, " RESULT=", result)
	quit(0 if result == OK else 1)
