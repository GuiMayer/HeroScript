class_name AppTheme
extends RefCounted

const INK := Color("#f5ecd8")
const MUTED := Color("#a9a0a8")
const EMBER := Color("#ee8653")
const GOLD := Color("#e7b75d")
const TEAL := Color("#53c9b5")
const BLOOD := Color("#ce5269")
const NIGHT := Color("#111323")
const PANEL := Color("#202236")
const PANEL_LIGHT := Color("#2d2d43")

static func build(high_contrast := false) -> Theme:
	var result := Theme.new()
	result.default_font_size = 17
	result.set_font_size("font_size", "Label", 17)
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
	result.set_color("font_disabled_color", "Button", Color("#656275"))
	result.set_stylebox("normal", "Button", box(PANEL_LIGHT, 9, Color("#4b4961"), 1))
	result.set_stylebox("hover", "Button", box(Color("#3b3546"), 9, EMBER, 2))
	result.set_stylebox("pressed", "Button", box(Color("#181927"), 9, GOLD, 2))
	result.set_stylebox("disabled", "Button", box(Color("#191a27"), 9, Color("#313142"), 1))
	result.set_stylebox("panel", "PanelContainer", box(Color("#1b1d2edb"), 13, Color("#3a3b55"), 1))
	result.set_stylebox("normal", "LineEdit", box(Color("#131522"), 7, Color("#44465d"), 1))
	result.set_stylebox("focus", "LineEdit", box(Color("#131522"), 7, TEAL, 2))
	result.set_color("font_color", "LineEdit", INK)
	result.set_color("font_placeholder_color", "LineEdit", MUTED)
	result.set_stylebox("normal", "TextEdit", box(Color("#11131f"), 8, Color("#3e4058"), 1))
	result.set_stylebox("focus", "TextEdit", box(Color("#11131f"), 8, TEAL, 2))
	result.set_color("font_color", "TextEdit", Color("#d9e6dd"))
	result.set_color("background", "ProgressBar", Color("#11131f"))
	result.set_color("font_color", "ProgressBar", INK)
	result.set_stylebox("background", "ProgressBar", box(Color("#11131f"), 6))
	result.set_stylebox("fill", "ProgressBar", box(BLOOD, 6))
	result.set_color("font_color", "RichTextLabel", INK)
	result.set_color("default_color", "RichTextLabel", INK)
	result.set_stylebox("panel", "PopupPanel", box(Color("#171927"), 12, EMBER, 1))
	return result

static func box(color: Color, radius := 8, border := Color.TRANSPARENT, width := 0) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = color
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

static func title(text: String, size := 34, color := INK) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", size)
	label.add_theme_color_override("font_color", color)
	return label

static func muted(text: String, size := 15) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", size)
	label.add_theme_color_override("font_color", MUTED)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	return label

static func button(text: String, min_width := 0) -> Button:
	var value := Button.new()
	value.text = text
	value.custom_minimum_size = Vector2(min_width, 48)
	value.focus_mode = Control.FOCUS_ALL
	return value

static func panel(content: Control, color := PANEL) -> PanelContainer:
	var wrapper := PanelContainer.new()
	wrapper.add_theme_stylebox_override("panel", box(color, 13, Color(color).lightened(0.18), 1))
	wrapper.add_child(content)
	return wrapper
