extends VBoxContainer
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
	add_child(detail_label)
	selection_label = AppTheme.caption("", 13, 80)
	add_child(selection_label)
	selection_label.hide()
	queue_label = AppTheme.caption("", 13, 80)
	queue_label.add_theme_color_override("font_color", AppTheme.TEAL)
	queue_label.custom_minimum_size.y = 18
	add_child(queue_label)
