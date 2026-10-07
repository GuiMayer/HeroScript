extends AcceptDialog
## Read-only inspection overlay. No engine service access.
func setup(title_text: String, summary: String, data: Dictionary = {}, card_model: Dictionary = {}, style: Resource = null) -> void:
	title = title_text
	min_size = Vector2i(650, 420)
	var body := VBoxContainer.new()
	add_child(body)
	var text := RichTextLabel.new()
	text.custom_minimum_size = Vector2(620, 340)
	text.size_flags_vertical = Control.SIZE_EXPAND_FILL
	text.text = summary
	text.selection_enabled = true
	var detail: Control = null
	if not card_model.is_empty():
		detail = preload("res://scripts/ui/card_details_panel.gd").new()
		detail.configure(card_model, style if style != null else preload("res://data/default_card_visual_style.tres"))
		detail.size_flags_vertical = Control.SIZE_EXPAND_FILL
		body.add_child(detail)
		text.visible = false
	body.add_child(text)
	if not data.is_empty():
		var technical := CheckButton.new()
		technical.text = I18n.text("Show engine details")
		technical.toggled.connect(func(value):
			text.text = JSON.stringify(data, "  ") if value else summary
			if detail != null:
				text.visible = value
				detail.visible = not value)
		body.add_child(technical)
	confirmed.connect(queue_free)
	canceled.connect(queue_free)
