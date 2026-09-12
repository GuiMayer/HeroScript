extends HBoxContainer
## Resource appearance only; bounds and values are supplied by the engine.

func setup(id: String, state: Dictionary) -> void:
	var label := Label.new()
	label.text = I18n.content_name(id).to_upper()
	label.custom_minimum_size.x = 78
	label.add_theme_font_size_override("font_size", 12)
	add_child(label)
	var bar := ProgressBar.new()
	bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bar.custom_minimum_size.y = 22
	bar.min_value = float(state.get("minimum", 0))
	bar.max_value = maxf(float(state.get("maximum", 1)), 1.0)
	bar.value = float(state.get("current", 0))
	bar.show_percentage = false
	var tone: Color = {"health": AppTheme.BLOOD, "block": Color("#7e9fbd"), "energy": AppTheme.GOLD, "mana": Color("#8c75dc")}.get(id, AppTheme.TEAL)
	bar.add_theme_stylebox_override("fill", AppTheme.box(tone, 5))
	add_child(bar)
	var amount := Label.new()
	amount.text = "%s/%s" % [I18n.number(float(state.get("current", 0))), I18n.number(float(state.get("maximum", 0)))]
	amount.custom_minimum_size.x = 65
	amount.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	add_child(amount)
