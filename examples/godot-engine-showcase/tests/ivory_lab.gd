extends SceneTree
## Same production components, isolated preferences, no gameplay gateway calls.
var failures := 0
const Layout = preload("res://tests/layout_inspector.gd")
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 8: await process_frame
func luminance(color: Color) -> float:
	var rgb := []
	for channel in [color.r, color.g, color.b]: rgb.append(channel / 12.92 if channel <= .04045 else pow((channel + .055) / 1.055, 2.4))
	return .2126 * rgb[0] + .7152 * rgb[1] + .0722 * rgb[2]
func contrast(a: Color, b: Color) -> float:
	var first := luminance(a); var second := luminance(b)
	return (maxf(first, second) + .05) / (minf(first, second) + .05)
func _run() -> void:
	var prefs = root.get_node("Preferences")
	var locale = root.get_node("I18n")
	var session = root.get_node("GameSession")
	var services = root.get_node("GameServices")
	var previous_profile := AppTheme.profile
	var old_state := [session.run, session.combat, services.diagnostics().size()]
	var old_file := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	var old_options := [prefs.text_scale, prefs.high_contrast, prefs.reduced_motion, locale.locale]
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate()
	root.add_child(lab)
	prefs.reduced_motion = true
	lab._refresh()
	await settle()
	check(AppTheme.profile.profile_id == "ivory_lab", "only laboratory opts into the ivory profile")
	check(AppTheme.profile.body_font is FontVariation and AppTheme.profile.title_font is FontVariation, "bundled fonts use explicit readable weights")
	for font in [AppTheme.profile.body_font, AppTheme.profile.title_font]:
		var supported := true
		for character in "ÁÉÍÓÚáéíóúâêôãõçÇ0123456789": supported = supported and font.has_char(character.unicode_at(0))
		check(supported, "bundled typeface supports Portuguese accents and numerals")
	var card: CardView = lab.card_controls[0]
	check(card.visual_style == lab.visual_context.card_style and card.visual_style.printed_face, "hand injects the shared printed card policy")
	check(card.get_node("PrintedKeyline").mouse_filter == Control.MOUSE_FILTER_IGNORE, "ink keyline supplements rarity without capturing input")
	var style: CardVisualStyle = card.visual_style
	var rules: RichTextLabel = card.find_child("Rules", true, false)
	check(rules.get_theme_color("default_color").is_equal_approx(style.effect_ink), "inverse prose color is actually applied to the live component")
	check(rules.get_theme_font("normal_font") == AppTheme.profile.body_font, "effect text inherits the bundled reading font")
	var weight_axis := TextServerManager.get_primary_interface().name_to_tag("wght")
	check(AppTheme.profile.body_font.variation_opentype.get(weight_axis, 0) == 500.0, "reading font explicitly requests medium weight by OpenType tag")
	var native_coordinates := TextServerManager.get_primary_interface().font_get_variation_coordinates(AppTheme.profile.body_font.get_rids()[0])
	check(native_coordinates.get(weight_axis, 0) == 500.0, "native font cache actually applies the requested weight")
	var context = load("res://scripts/ui/visual_context.gd").new(locale, {}, AppTheme.profile)
	var isolated_card := CardView.new(context)
	isolated_card.configure(card.model)
	context.profile = previous_profile
	isolated_card.configure(card.model)
	check(not isolated_card.visual_style.printed_face, "same card can return to the production policy after profile change")
	isolated_card.visual_style = style
	isolated_card.configure(card.model)
	check(isolated_card.visual_style == style, "explicit component policy still takes precedence over profile")
	isolated_card.free()
	for ink in [style.base_value_color, style.lower_value_color, style.higher_value_color, style.effect_ink]:
		check(contrast(ink, style.effect_surface) >= 4.5, "effect text/numeric colors meet normal-text contrast on inverse surface")
	check(style.base_value_color == Color.WHITE and style.lower_value_color == Color("#ff7373") and style.higher_value_color == Color("#72b7ff"), "numeric comparison policy is preserved exactly")
	var trace = lab.get_node("PrintedCircuit")
	check(trace.mouse_filter == Control.MOUSE_FILTER_IGNORE and trace.get_process_mode() != Node.PROCESS_MODE_ALWAYS and not trace.is_processing(), "circuits are passive and not redrawn every frame")
	var lines: Array = trace.paths()
	var valid := true
	for line in lines:
		var diagonal: Vector2 = line[2] - line[1]
		valid = valid and is_equal_approx(absf(diagonal.x), absf(diagonal.y))
	check(valid, "all decorative elbows have exactly 45-degree diagonals")
	var direction: Vector2 = lines[0][2] - lines[0][1]
	var normal := Vector2(-direction.y, direction.x).normalized()
	check(is_equal_approx(absf((lines[1][1] - lines[0][1]).dot(normal)), trace.spacing), "parallel spacing is conserved through mitered elbows")
	lab.set_preset(2)
	var sample: CardView = lab.card_controls[1]
	lab.select_card(str(sample.model.labId))
	lab.inspect_selected()
	await settle()
	var inspector = lab.get_children().filter(func(child): return child is AcceptDialog)[0]
	check(inspector.find_child("EffectPanel", true, false) != null, "persistent inspection uses the same inverse effect treatment")
	inspector.queue_free()
	await settle()
	var tooltip = sample._make_custom_tooltip(sample.tooltip_text)
	root.add_child(tooltip)
	check(tooltip.find_child("EffectPanel", true, false) != null and tooltip.find_child("CardDetailsScroll", true, false) == null, "tooltip has inverse effects and remains passive without inaccessible scrollbars")
	tooltip.queue_free()
	for dimensions in [Vector2i(1280, 720), Vector2i(2560, 1080)]:
		root.size = dimensions
		for language in ["en", "pt_BR"]:
			lab.set_language(language)
			for scale in [1.0, 1.2]:
				lab.set_text_scale(scale)
				for amount in [0, 1, 5, 12, 100]:
					lab.set_count(amount)
					await settle()
					check(lab.hand.order.size() == amount and Layout.vertical_text_issues(lab).is_empty() and lab.hand.get_global_rect().end.y <= root.get_visible_rect().end.y + 1, "ivory hand fits/readable %s / %s / %s / %s cards" % [dimensions, language, scale, amount])
	root.size = Vector2i(1280, 720)
	lab.set_text_scale(1.0); lab.set_count(5)
	prefs.high_contrast = true; lab._refresh()
	await settle()
	card = lab.card_controls[0]
	var effects: PanelContainer = card.find_child("EffectPanel", true, false)
	check(effects.get_theme_stylebox("panel").bg_color.is_equal_approx(style.effect_surface) and trace.simplified, "high contrast preserves inverse effects and removes decoration")
	check(card.get_theme_stylebox("normal").border_color == style.rarity_color(str(card.model.rarityId)), "high contrast does not erase rarity")
	var chip: PanelContainer = card.find_child("Cost", true, false).get_child(0)
	check(contrast(Color.WHITE, chip.get_theme_stylebox("panel").bg_color) >= 4.5, "high contrast keeps white resource-cost text on a readable colored surface")
	check(old_state == [session.run, session.combat, services.diagnostics().size()], "lab makes no requests and changes no session snapshots")
	lab.queue_free()
	await settle()
	check(AppTheme.profile == previous_profile and old_options == [prefs.text_scale, prefs.high_contrast, prefs.reduced_motion, locale.locale], "leaving the lab restores the production profile and temporary preferences")
	check(old_file == (FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""), "lab never writes player preferences")
	root.get_node("GameAudio").shutdown()
	await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
