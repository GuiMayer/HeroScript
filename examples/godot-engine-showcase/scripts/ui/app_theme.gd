class_name AppTheme
extends RefCounted

static var INK: Color:
	get: return color("text.primary")
static var MUTED: Color:
	get: return color("text.secondary")
static var EMBER: Color:
	get: return color("accent.warm")
static var GOLD: Color:
	get: return color("accent.primary")
static var TEAL: Color:
	get: return color("accent.positive")
static var BLOOD: Color:
	get: return color("accent.danger")
static var NIGHT: Color:
	get: return color("surface.canvas")
static var PANEL: Color:
	get: return color("surface.panel")
static var PANEL_LIGHT: Color:
	get: return color("surface.raised")
static var profile: Resource = preload("res://data/themes/preserved.tres")
static var generation := 0
static var text_scale := 1.0
static var high_contrast_enabled := false
static func configure_preferences(scale: float, contrast: bool) -> void:
	text_scale = scale
	high_contrast_enabled = contrast
static func set_profile(value: Resource) -> void:
	profile = value
	generation += 1
static func color(role: String) -> Color: return profile.color(role)
static func color_tag(role: String) -> String: return "[color=#" + color(role).to_html(false) + "]"
const MIN_READABLE_TEXT_WIDTH := 112.0

static func build(high_contrast := false) -> Theme:
	var result := Theme.new()
	result.default_font_size = roundi(profile.body_font_size * text_scale)
	if profile.body_font: result.default_font = profile.body_font
	result.set_font_size("font_size", "Label", roundi(profile.font_size("body", profile.body_font_size) * text_scale))
	result.set_color("font_color", "Label", INK)
	if high_contrast:
		result.set_constant("outline_size", "Label", 2)
		result.set_color("font_outline_color", "Label", Color.BLACK)
		result.set_constant("outline_size", "Button", 2)
		result.set_color("font_outline_color", "Button", Color.BLACK)
	result.set_color("font_shadow_color", "Label", Color(0, 0, 0, 0.55))
	result.set_constant("shadow_offset_x", "Label", 1)
	result.set_constant("shadow_offset_y", "Label", 2)
	result.set_color("font_color", "Button", INK)
	result.set_color("font_hover_color", "Button", Color.WHITE)
	result.set_color("font_pressed_color", "Button", GOLD)
	result.set_color("font_disabled_color", "Button", color("text.disabled"))
	result.set_stylebox("normal", "Button", box(PANEL_LIGHT, 9, color("stroke.control"), 1))
	result.set_stylebox("hover", "Button", box(color("surface.hover"), 9, EMBER, 2))
	result.set_stylebox("pressed", "Button", box(color("surface.pressed"), 9, GOLD, 2))
	result.set_stylebox("disabled", "Button", box(color("surface.disabled"), 9, color("stroke.disabled"), 1))
	result.set_stylebox("panel", "PanelContainer", box(color("surface.panel_translucent"), 13, color("stroke.panel"), 1))
	result.set_stylebox("normal", "LineEdit", box(color("surface.input"), 7, color("stroke.input"), 1))
	result.set_stylebox("focus", "LineEdit", box(color("surface.input"), 7, TEAL, 2))
	result.set_color("font_color", "LineEdit", INK)
	result.set_color("font_placeholder_color", "LineEdit", MUTED)
	result.set_stylebox("normal", "TextEdit", box(color("surface.inset"), 8, color("stroke.editor"), 1))
	result.set_stylebox("focus", "TextEdit", box(color("surface.inset"), 8, TEAL, 2))
	result.set_color("font_color", "TextEdit", color("text.code"))
	result.set_color("background", "ProgressBar", color("surface.inset"))
	result.set_color("font_color", "ProgressBar", INK)
	result.set_stylebox("background", "ProgressBar", box(color("surface.inset"), 6))
	result.set_stylebox("fill", "ProgressBar", box(BLOOD, 6))
	result.set_color("font_color", "RichTextLabel", INK)
	result.set_color("default_color", "RichTextLabel", INK)
	result.set_stylebox("panel", "PopupPanel", box(color("surface.popup"), 12, EMBER, 1))
	result.set_stylebox("focus", "Button", box(Color.TRANSPARENT, 9, Color.WHITE if high_contrast else GOLD, 3))
	if high_contrast:
		result.set_color("font_disabled_color", "Button", color("text.high_contrast_disabled"))
		result.set_stylebox("normal", "Button", box(color("surface.high_contrast"), 9, Color.WHITE, 2))
	for variant in ["PrimaryAction", "SecondaryAction", "DangerAction"]:
		result.set_type_variation(variant, "Button")
	result.set_stylebox("normal", "PrimaryAction", box(color("surface.primary_action"), profile.corner_radius, GOLD, 2))
	result.set_color("font_color", "DangerAction", BLOOD)
	for variant in ["ScreenTitle", "SectionTitle", "Caption"]:
		result.set_type_variation(variant, "Label")
	for variant in ["ScreenTitle", "SectionTitle"]:
		if profile.title_font: result.set_font("font", variant, profile.title_font)
	result.set_color("font_color", "Caption", MUTED)
	if profile.printed_surfaces: _printed_controls(result, high_contrast)
	return result

static func _printed_controls(result: Theme, contrast: bool) -> void:
	# Opt-in presentation variant; the production theme remains unchanged.
	for type in ["Label", "Button", "CheckButton", "CheckBox", "OptionButton", "SpinBox", "LineEdit", "RichTextLabel", "PopupMenu"]:
		result.set_color("font_color", type, INK)
		result.set_color("font_hover_color", type, INK)
		result.set_color("font_focus_color", type, INK)
		result.set_color("font_pressed_color", type, GOLD)
		result.set_color("font_disabled_color", type, color("text.disabled"))
		result.set_color("font_outline_color", type, PANEL)
		result.set_constant("outline_size", type, 0)
	result.set_color("font_shadow_color", "Label", Color.TRANSPARENT)
	result.set_constant("shadow_offset_x", "Label", 0)
	result.set_constant("shadow_offset_y", "Label", 0)
	for type in ["Button", "OptionButton"]:
		for state in ["normal", "hover", "pressed", "disabled", "focus"]:
			var fill := {"normal": PANEL_LIGHT, "hover": color("surface.hover"), "pressed": color("surface.pressed"), "disabled": color("surface.disabled"), "focus": Color.TRANSPARENT}
			result.set_stylebox(state, type, box(fill[state], 2, GOLD if state == "focus" else color("stroke.control"), 2 if state == "focus" or contrast else 1))
			var control_box: StyleBoxFlat = result.get_stylebox(state, type)
			control_box.content_margin_left = 10; control_box.content_margin_right = 10
			control_box.content_margin_top = 6; control_box.content_margin_bottom = 6
	result.set_stylebox("panel", "PopupMenu", box(PANEL, 2, INK, 1))
	result.set_stylebox("hover", "PopupMenu", box(color("surface.hover"), 0))
	result.set_stylebox("panel", "PopupPanel", box(PANEL, 2, INK, 1))
	result.set_stylebox("normal", "LineEdit", box(color("surface.input"), 2, color("stroke.input"), 1))
	result.set_stylebox("focus", "LineEdit", box(color("surface.input"), 2, GOLD, 2))
	for type in ["HScrollBar", "VScrollBar"]:
		var track := box(color("surface.inset"), 0)
		track.content_margin_left = 2; track.content_margin_right = 2
		track.content_margin_top = 2; track.content_margin_bottom = 2
		result.set_stylebox("scroll", type, track)
		for state in ["grabber", "grabber_highlight", "grabber_pressed"]:
			var style := box(GOLD if state == "grabber_pressed" else MUTED, 0)
			style.content_margin_left = 4; style.content_margin_right = 4
			style.content_margin_top = 4; style.content_margin_bottom = 4
			result.set_stylebox(state, type, style)
	var window_border := box(PANEL, 2, INK, 1)
	window_border.expand_margin_top = 32
	result.set_stylebox("embedded_border", "Window", window_border)
	result.set_color("title_color", "Window", INK)
	result.set_constant("title_height", "Window", 32)
	result.set_stylebox("panel", "AcceptDialog", box(PANEL, 2, INK, 1))

static func box(color: Color, radius := 8, border := Color.TRANSPARENT, width := 0) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = color
	style.set_meta("background_role", profile.role_for(color))
	style.set_meta("border_role", profile.role_for(border))
	style.corner_radius_top_left = radius
	style.corner_radius_top_right = radius
	style.corner_radius_bottom_left = radius
	style.corner_radius_bottom_right = radius
	style.border_width_left = width
	style.border_width_top = width
	style.border_width_right = width
	style.border_width_bottom = width
	style.border_color = border
	style.content_margin_left = 14
	style.content_margin_right = 14
	style.content_margin_top = 12
	style.content_margin_bottom = 12
	return style

static func title(text: String, size := 34, color = null) -> Label:
	var label := Label.new()
	label.text = text
	if size >= 28: label.theme_type_variation = "ScreenTitle"
	elif size >= 20: label.theme_type_variation = "SectionTitle"
	label.set_meta("font_role", "screen_title" if size >= 28 else ("section_title" if size >= 20 else ("body" if size >= 15 else "caption")))
	label.add_theme_font_size_override("font_size", size)
	label.add_theme_color_override("font_color", INK if color == null else color)
	label.set_meta("text_role", profile.role_for(INK if color == null else color))
	return label

static func muted(text: String, size := 15, min_width := 0.0) -> Label:
	var label := Label.new()
	label.set_meta("font_role", "caption")
	label.text = text
	label.add_theme_font_size_override("font_size", size)
	label.theme_type_variation = "Caption"
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	if min_width > 0:
		label.custom_minimum_size.x = min_width
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return label

static func caption(text: String, size := 15, min_width := 0) -> Label:
	var label := Label.new()
	label.set_meta("font_role", "caption")
	label.text = text
	label.add_theme_font_size_override("font_size", size)
	label.theme_type_variation = "Caption"
	label.custom_minimum_size.x = min_width
	label.clip_text = true
	label.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	return label

static func wrap(label: Label, min_width := MIN_READABLE_TEXT_WIDTH) -> Label:
	label.clip_text = false
	label.text_overrun_behavior = TextServer.OVERRUN_NO_TRIMMING
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.custom_minimum_size.x = maxf(label.custom_minimum_size.x, min_width)
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return label

static func button(text: String, min_width := 0) -> Button:
	var value := Button.new()
	value.text = text
	value.custom_minimum_size = Vector2(min_width, 48)
	value.focus_mode = Control.FOCUS_ALL
	value.theme_type_variation = "SecondaryAction"
	return value

static func panel(content: Control, color = null) -> PanelContainer:
	var wrapper := PanelContainer.new()
	var resolved: Color = PANEL if color == null else color
	wrapper.add_theme_stylebox_override("panel", box(resolved, 13, resolved.lightened(0.18), 1))
	wrapper.add_child(content)
	return wrapper

static func apply_view_preferences(node: Node, scale_override := -1.0, contrast_override = null, revision: Array = []) -> void:
	var effective_scale: float = text_scale if scale_override < 0 else scale_override
	var effective_contrast: bool = high_contrast_enabled if contrast_override == null else bool(contrast_override)
	if revision.is_empty(): revision = [profile.presentation_key(), effective_scale, effective_contrast]
	if node is Control and node.get_meta("visual_preferences_revision", []) != revision:
		node.set_meta("visual_preferences_revision", revision.duplicate())
		if node.has_meta("text_role") and not str(node.get_meta("text_role")).is_empty():
			node.add_theme_color_override("font_color", color(str(node.get_meta("text_role"))))
		for font_key in ["font_size", "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size"]:
			if not node.has_theme_font_size_override(font_key): continue
			var font_meta: String = "base_" + font_key
			if not node.has_meta(font_meta): node.set_meta(font_meta, node.get_theme_font_size(font_key))
			node.add_theme_font_size_override(font_key, roundi(profile.font_size(str(node.get_meta("font_role", "")), float(node.get_meta(font_meta))) * effective_scale))
		for style_name in ["normal", "panel", "hover", "pressed", "disabled"]:
			if not node.has_theme_stylebox_override(style_name): continue
			var meta: String = "base_style_" + style_name
			if not node.has_meta(meta): node.set_meta(meta, node.get_theme_stylebox(style_name).duplicate())
			var style = node.get_meta(meta).duplicate()
			if style is StyleBoxFlat:
				var background_role := str(style.get_meta("background_role", ""))
				var border_role := str(style.get_meta("border_role", ""))
				if not background_role.is_empty(): style.bg_color = color(background_role)
				if not border_role.is_empty(): style.border_color = color(border_role)
			if effective_contrast and style is StyleBoxFlat:
				if not node.get_meta("contrast_preserve_colors", false):
					style.bg_color = color(str(node.get_meta("contrast_surface_role", "surface.high_contrast_panel")))
					style.border_color = node.get_meta("semantic_border_color", Color.WHITE)
				style.set_border_width_all(2)
			node.add_theme_stylebox_override(style_name, style)
	for child in node.get_children(): apply_view_preferences(child, effective_scale, effective_contrast, revision)
