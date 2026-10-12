extends VBoxContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType = ContextType.new()
var detail_label: RichTextLabel
var selection_label: Label
var queue_label: Label
func mount(hint: String) -> void:
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_theme_constant_override("separation", 0)
	detail_label = RichTextLabel.new()
	detail_label.name = "ActionPreview"
	detail_label.custom_minimum_size = Vector2(200, 42)
	detail_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	detail_label.add_theme_font_size_override("normal_font_size", 14)
	detail_label.text = hint
	var row := HBoxContainer.new(); row.add_theme_constant_override("separation", 8)
	var icon := TextureRect.new(); icon.texture = preload("res://scripts/ui/combat_chrome.gd").texture("arrow")
	icon.custom_minimum_size = Vector2(24, 24); icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED; icon.modulate = visual_context.styles.INK
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE; row.add_child(icon); row.add_child(detail_label); add_child(row)
	selection_label = visual_context.styles.caption("", 13, 80)
	add_child(selection_label)
	selection_label.hide()
	queue_label = visual_context.styles.caption("", 13, 80)
	queue_label.add_theme_color_override("font_color", visual_context.styles.TEAL)
	queue_label.custom_minimum_size.y = 18
	add_child(queue_label)
