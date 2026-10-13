extends HBoxContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
## Authoritative numbers, optional bounded visualization; no resource semantics.
var visual_context: ContextType
var label := Label.new()
var bar := ProgressBar.new()
var amount := Label.new()
var _model: Dictionary = {}
var _value_motion: Tween
func present_change(application: Dictionary, duration: float) -> Tween:
	if str(application.get("resourceId", "")) != str(_model.get("resourceId", "")) or application.get("currentValue") == null: return null
	var field: String = {"Current": "current", "0": "current", "0.0": "current", "Minimum": "minimum", "1": "minimum", "1.0": "minimum", "Maximum": "maximum", "2": "maximum", "2.0": "maximum"}.get(str(application.get("resourceField", "Current")), "")
	if field.is_empty(): return null
	var state: Dictionary = _model.state.duplicate(true)
	state[field] = application.currentValue
	var previous: float = float(application.get("previousValue", bar.value)) if field == "current" else bar.value
	setup(_model.resourceId, state, _model.appearance)
	if _value_motion and _value_motion.is_valid(): _value_motion.kill()
	if bar.visible and duration > 0 and not is_equal_approx(previous, bar.value):
		var destination: float = bar.value; bar.value = previous
		_value_motion = create_tween(); _value_motion.tween_property(bar, "value", destination, duration)
		return _value_motion
	return null
func _exit_tree() -> void:
	if _value_motion and _value_motion.is_valid(): _value_motion.kill()
func presentation_model() -> Dictionary: return _model.duplicate(true)
func reconcile_from(other) -> void:
	visual_context = other.visual_context
	custom_minimum_size = other.custom_minimum_size
	apply_model(other.presentation_model())
func apply_model(data: Dictionary) -> void:
	setup(str(data.get("resourceId", "")), data.get("state", {}), data.get("appearance", {}))
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
	theme = visual_context.theme
	_model = {"resourceId": id, "state": state.duplicate(true), "appearance": appearance.duplicate(true)}
	appearance = visual_context.resource_appearance(id, appearance)
	var model = preload("res://scripts/presentation/view_models.gd").ResourceValue.new(state)
	label.text = visual_context.translator.content_name(id).to_upper()
	var bounded: bool = model.has_bounds and is_finite(model.current) and model.current >= float(state.minimum) and model.current <= float(state.maximum)
	bar.visible = str(appearance.get("display", "bar")) == "bar" and bounded
	if bounded:
		bar.min_value = float(state.minimum)
		bar.max_value = float(state.maximum)
		bar.value = model.current
	var tone := Color.from_string(str(appearance.get("color", "")), visual_context.styles.color("resource.unknown"))
	bar.add_theme_stylebox_override("fill", visual_context.styles.box(tone, 5))
	amount.text = visual_context.translator.number(model.current)
	if state.has("maximum") and str(appearance.get("display", "bar")) != "counter": amount.text += "/" + visual_context.translator.number(float(state.maximum))
	amount.size_flags_horizontal = Control.SIZE_FILL if bar.visible else Control.SIZE_EXPAND_FILL
	bar.tooltip_text = amount.text
	amount.tooltip_text = amount.text
