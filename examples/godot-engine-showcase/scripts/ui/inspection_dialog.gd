extends AcceptDialog
## Read-only inspection overlay. No engine service access.
func setup(title_text: String, summary: String, data: Dictionary = {}) -> void:
	title = title_text
	min_size = Vector2i(650, 420)
	var body := VBoxContainer.new()
	add_child(body)
	var text := RichTextLabel.new()
	text.custom_minimum_size = Vector2(620, 340)
	text.size_flags_vertical = Control.SIZE_EXPAND_FILL
	text.text = summary
	text.selection_enabled = true
	body.add_child(text)
	if not data.is_empty():
		var technical := CheckButton.new()
		technical.text = I18n.text("Show engine details")
		technical.toggled.connect(func(value): text.text = JSON.stringify(data, "  ") if value else summary)
		body.add_child(technical)
	confirmed.connect(queue_free)
	canceled.connect(queue_free)
