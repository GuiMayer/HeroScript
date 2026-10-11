extends HBoxContainer
## Authoritative numbers, optional bounded visualization; no resource semantics.
var visual_context
var label := Label.new()
var bar := ProgressBar.new()
var amount := Label.new()
func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
	label.custom_minimum_size.x = 78
	label.add_theme_font_size_override("font_size", 12)
	add_child(label)
	bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bar.custom_minimum_size.y = 22
	bar.show_percentage = false
	add_child(bar)
	amount.custom_minimum_size.x = 65
	amount.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	add_child(amount)
func setup(id: String, state: Dictionary, appearance: Dictionary = {}) -> void:
	var model = preload("res://scripts/presentation/view_models.gd").ResourceValue.new(state)
	label.text = visual_context.translator.content_name(id).to_upper()
	var bounded: bool = model.has_bounds and is_finite(model.current) and model.current >= float(state.minimum) and model.current <= float(state.maximum)
	bar.visible = str(appearance.get("display", "bar")) == "bar" and bounded
	if bounded:
		bar.min_value = float(state.minimum)
		bar.max_value = float(state.maximum)
		bar.value = model.current
	var tone := Color.from_string(str(appearance.get("color", "")), AppTheme.color("resource.unknown"))
	bar.add_theme_stylebox_override("fill", AppTheme.box(tone, 5))
	amount.text = visual_context.translator.number(model.current)
	if state.has("maximum") and str(appearance.get("display", "bar")) != "counter": amount.text += "/" + visual_context.translator.number(float(state.maximum))
	amount.size_flags_horizontal = Control.SIZE_FILL if bar.visible else Control.SIZE_EXPAND_FILL
	bar.tooltip_text = amount.text
	amount.tooltip_text = amount.text
