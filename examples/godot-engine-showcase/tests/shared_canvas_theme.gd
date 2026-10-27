extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func run() -> void:
	var menu = load("res://data/themes/ivory_theme.tres")
	var combat = load("res://data/themes/ivory_run.tres")
	var scenery = load("res://data/themes/ivory_scenery.tres")
	check(menu.palette == combat.palette and menu.palette == scenery.palette, "menu, run and scenery share one palette")
	check(menu.color("surface.canvas") == Color.WHITE, "menu canvas is white")
	check(combat.color("surface.canvas") == Color.WHITE, "run canvas is white")
	check(scenery.color("scenery.base") == Color.WHITE, "scenery canvas is white")
	var lab = preload("res://scripts/ui/styles/programmer_art_profile.gd").create(menu)
	check(lab.color("surface.canvas") == Color.WHITE, "laboratory inherits the shared canvas")
	for role in ["surface.panel", "surface.raised", "surface.input", "surface.popup", "surface.settings_row", "stroke.panel", "text.primary"]:
		check(menu.color(role) == combat.color(role) and lab.color(role) == menu.color(role), "consistent shared neutral palette: " + role)
	for role in ["surface.panel", "surface.raised", "surface.hover", "surface.pressed", "surface.settings_row"]:
		var neutral: Color = menu.color(role)
		check(absf(neutral.r - neutral.b) < .04, "surface has no parchment tint: " + role)
	var ink: Color = menu.color("text.primary")
	var paper: Color = menu.color("surface.panel")
	check((paper.srgb_to_linear().get_luminance() + .05) / (ink.srgb_to_linear().get_luminance() + .05) >= 7.0, "primary text maintains strong contrast on white")
	check(menu.color("surface.hover") != paper and menu.color("surface.pressed") != menu.color("surface.hover"), "neutral interaction states remain distinguishable")
	var original = menu.presentation_key()
	var custom = menu.duplicate()
	custom.palette = menu.palette.duplicate(true)
	custom.palette.colors["surface.canvas"] = "#123456"
	check(custom.color("surface.canvas") == Color.html("123456"), "changing palette propagates without component overrides")
	check(custom.presentation_key() != original, "palette changes invalidate presentation caches")
	var custom_scenery = scenery.duplicate()
	custom_scenery.palette = custom.palette
	check(custom_scenery.color("scenery.base") == custom.color("surface.canvas"), "scenery follows the same configurable canvas")
	custom.colors = custom.colors.duplicate(true)
	custom.colors["surface.canvas"] = "#abcdef"
	check(custom.color("surface.canvas") == Color.html("abcdef"), "explicit theme overrides remain supported")
	check(menu.presentation_key() == original and menu.color("surface.canvas") == Color.WHITE, "customizing a copy preserves source resources")
	var svg = FileAccess.get_file_as_string("res://assets/ivory/battlefield.svg")
	check(not svg.contains("<rect"), "combat illustration does not obscure the themed canvas")
	quit(0 if failures == 0 else 1)
