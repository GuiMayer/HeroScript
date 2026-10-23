extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func run() -> void:
	var base := preload("res://data/themes/ivory_theme.tres")
	var original := base.presentation_key()
	var profile = preload("res://scripts/ui/styles/programmer_art_profile.gd").create(base)
	for property in ["screen_margin", "corner_radius", "body_font_size", "body_font", "title_font", "actor_art_max_height", "compact_actor_hud", "art_manifest"]:
		check(profile.get(property) == base.get(property), "profile inherits " + property)
	for property in base.card_style.get_property_list():
		if property.usage & PROPERTY_USAGE_SCRIPT_VARIABLE and property.name != "keyline_style":
			check(profile.card_style.get(property.name) == base.card_style.get(property.name), "card preserves " + property.name)
	check(profile.card_style.keyline_style == "registration", "registration marks opt in without replacing the component")
	var factory = preload("res://scripts/ui/styles/view_style_factory.gd").new()
	for contrast in [false, true]:
		factory.profile = base
		var old_theme: Theme = factory.build(contrast)
		factory.profile = profile
		var new_theme: Theme = factory.build(contrast)
		for type in old_theme.get_type_list():
			for name in old_theme.get_stylebox_list(type):
				check(old_theme.get_stylebox(name, type).get_minimum_size() == new_theme.get_stylebox(name, type).get_minimum_size(), "same native style minimum: %s/%s/%s" % [contrast, type, name])
	check(base.presentation_key() == original and base.card_style.keyline_style == "frame", "base resources remain untouched")
	quit(0 if failures == 0 else 1)
