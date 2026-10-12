extends RefCounted
## Native buttons keep input/accessibility; vectors and cut keylines are visual only.
const STYLE = preload("res://scripts/ui/styles/combat_chrome_style.gd")
static func texture(id: String) -> Texture2D:
	var path := "res://assets/ui-icons/%s.svg" % id
	return load(path if ResourceLoader.exists(path) else "res://assets/ui-icons/unknown.svg")
static func apply(button: Button, id: String, label: String, context, primary := false, compact := false) -> void:
	var style := STYLE.new()
	button.icon = texture(id); button.expand_icon = true
	button.add_theme_constant_override("icon_max_width", style.icon_size)
	button.tooltip_text = label; button.accessibility_name = label
	button.text = "" if compact else label
	button.custom_minimum_size.y = style.primary_height if primary else style.control_height
	if compact: button.custom_minimum_size.x = style.control_height
	var ink: Color = context.styles.color("text.inverse" if primary else "text.primary")
	for state in ["normal", "hover", "pressed", "focus", "disabled"]:
		var fill: Color = context.styles.color("surface.primary_action") if primary else Color.TRANSPARENT
		if state == "hover": fill = context.styles.color("surface.primary_hover" if primary else "surface.hover")
		if state == "disabled": fill = context.styles.color("surface.disabled")
		var box: StyleBoxFlat = context.styles.box(fill, 0)
		box.content_margin_left = 10; box.content_margin_right = 10; box.content_margin_top = 6; box.content_margin_bottom = 6
		button.add_theme_stylebox_override(state, box)
	for key in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color"]: button.add_theme_color_override(key, ink)
	button.add_theme_color_override("icon_disabled_color", context.styles.MUTED)
	var line = button.get_node_or_null("CircuitKeyline")
	if line == null:
		line = Keyline.new(); line.name = "CircuitKeyline"; button.add_child(line)
		line.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	line.ink = ink; line.cut = style.corner_cut; line.width = style.stroke_width; line.queue_redraw()
class Keyline extends Control:
	var ink := Color.BLACK
	var cut := 8.0
	var width := 1.0
	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE; resized.connect(queue_redraw)
	func _draw() -> void:
		var r := size - Vector2(2, 2); var c := minf(cut, r.y * .25)
		draw_polyline(PackedVector2Array([Vector2(1+c,1), Vector2(r.x,1), Vector2(r.x,r.y-c), Vector2(r.x-c,r.y), Vector2(1,r.y), Vector2(1,1+c), Vector2(1+c,1)]), ink, width, true)
