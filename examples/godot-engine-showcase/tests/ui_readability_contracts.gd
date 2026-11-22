extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ",label)
	if not ok: failures += 1
func luminance(color: Color) -> float:
	var linear := color.srgb_to_linear()
	return linear.r * .2126 + linear.g * .7152 + linear.b * .0722
func ratio(a: Color,b: Color) -> float:
	var x := luminance(a); var y := luminance(b)
	return (maxf(x,y)+.05)/(minf(x,y)+.05)
func _run() -> void:
	for path in ["ivory_run.tres","mvp_combat_theme.tres","ivory_theme.tres"]:
		var profile = load("res://data/themes/"+path)
		var context = preload("res://scripts/ui/visual_context.gd").new(null,{"text_scale":1.0},profile)
		var theme: Theme = context.theme
		check(ratio(profile.color("text.primary"),profile.color("surface.canvas")) >= 4.5 and ratio(profile.color("text.secondary"),profile.color("surface.canvas")) >= 4.5, path+" text contrast")
		check(ratio(theme.get_stylebox("normal","Button").border_color,profile.color("surface.canvas")) >= 3.0, path+" interactive boundary")
		check(theme.default_font_size >= 18 and profile.font_size("decision",0) >= 22, path+" semantic typography")
		var accessible = preload("res://scripts/ui/visual_context.gd").new(null,{"text_scale":2.0,"high_contrast":true},profile)
		check(accessible.theme.default_font_size >= 36 and ratio(accessible.theme.get_color("font_color","PrimaryAction"),accessible.theme.get_stylebox("normal","PrimaryAction").bg_color)>=7.0, path+" contrast and 200% scale")
	check(preload("res://scripts/ui/styles/settings_schema.gd").option("text_scale").max == 2.0,"accessibility scale supported by schema")
	quit(1 if failures else 0)
