extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var original: Resource = AppTheme.profile
	var prefs = root.get_node("Preferences")
	var scale: float = prefs.text_scale
	var panel := AppTheme.panel(AppTheme.title("Profile", 20))
	root.add_child(panel)
	AppTheme.apply_view_preferences(panel)
	var label: Label = panel.get_child(0)
	var prior: Color = label.get_theme_color("font_color")
	var alternate: Resource = original.duplicate(true)
	alternate.profile_id = "contract-alternative"
	alternate.colors = {"text.primary": "#182a35", "surface.panel": "#eee8dc", "accent.primary": "#a94b31"}
	AppTheme.set_profile(alternate)
	panel.theme = AppTheme.build()
	AppTheme.apply_view_preferences(panel)
	check(label.get_theme_color("font_color") == alternate.color("text.primary"), "existing text adopts semantic role")
	check(panel.get_theme_stylebox("panel").bg_color == alternate.color("surface.panel"), "existing panel adopts profile without rebuilding")
	prefs.text_scale = 1.2
	AppTheme.configure_preferences(1.2, false)
	AppTheme.apply_view_preferences(panel)
	AppTheme.apply_view_preferences(panel)
	check(label.get_theme_font_size("font_size") == 24, "text scaling is idempotent from canonical size")
	AppTheme.set_profile(original)
	AppTheme.apply_view_preferences(panel)
	check(label.get_theme_color("font_color") == prior and panel.get_theme_stylebox("panel").bg_color == original.color("surface.panel"), "A to B to A has no stale base-style cache")
	var theme := AppTheme.build(true)
	check(theme.get_type_variation_base("PrimaryAction") == "Button" and theme.get_type_variation_base("Caption") == "Label", "native control variants")
	var one := AppTheme.button("One")
	var two := AppTheme.button("Two")
	one.theme = theme; two.theme = theme
	var local: StyleBoxFlat = one.get_theme_stylebox("hover").duplicate()
	local.bg_color = Color.RED
	one.add_theme_stylebox_override("hover", local)
	check(two.get_theme_stylebox("hover").bg_color != Color.RED, "local feedback never mutates shared theme resource")
	one.free(); two.free(); panel.queue_free()
	prefs.text_scale = scale
	AppTheme.configure_preferences(scale, prefs.high_contrast)
	await process_frame
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
