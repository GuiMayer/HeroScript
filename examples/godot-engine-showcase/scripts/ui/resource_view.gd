@tool
class_name ResourceView
extends HBoxContainer

const ContextType = preload("res://scripts/ui/visual_context.gd")
## Authoritative numbers, optional bounded visualization; no resource semantics.
var visual_context: ContextType
var label: Label
var bar: ProgressBar
var amount: Label
var _model: Dictionary = {}
var _value_motion: Tween

func _ready() -> void:
	_ensure_nodes()
	if not _model.is_empty():
		_apply_model()
	elif Engine.is_editor_hint():
		# Live preview in Godot editor 2D canvas
		setup("health", {"current": 75, "minimum": 0, "maximum": 100}, {"color": "#e53935"})

func _ensure_nodes() -> void:
	if label == null:
		label = get_node_or_null("%ResourceLabel")
		if label == null: label = find_child("ResourceLabel", true, false)
		if label == null:
			label = Label.new()
			label.name = "ResourceLabel"
			label.custom_minimum_size.x = 78
			label.add_theme_font_size_override("font_size", 12)
			add_child(label)
	if bar == null:
		bar = get_node_or_null("%ProgressBar")
		if bar == null: bar = find_child("ProgressBar", true, false)
		if bar == null:
			bar = ProgressBar.new()
			bar.name = "ProgressBar"
			bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			bar.custom_minimum_size.y = 22
			bar.show_percentage = false
			add_child(bar)
	if amount == null:
		amount = get_node_or_null("%AmountLabel")
		if amount == null: amount = find_child("AmountLabel", true, false)
		if amount == null:
			amount = Label.new()
			amount.name = "AmountLabel"
			amount.custom_minimum_size.x = 65
			amount.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
			add_child(amount)

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

func settle_motion() -> void:
	if _value_motion != null and _value_motion.is_valid(): _value_motion.kill()
	if not _model.is_empty(): bar.value = float(_model.state.get("current", bar.value))

func presentation_model() -> Dictionary: return _model.duplicate(true)

func reconcile_from(other) -> void:
	visual_context = other.visual_context
	custom_minimum_size = other.custom_minimum_size
	apply_model(other.presentation_model())

func apply_model(data: Dictionary) -> void:
	setup(str(data.get("resourceId", "")), data.get("state", {}), data.get("appearance", {}))

func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
	_ensure_nodes()

func setup(id: String, state: Dictionary, appearance: Dictionary = {}) -> void:
	_ensure_nodes()
	theme = visual_context.theme
	_model = {"resourceId": id, "state": state.duplicate(true), "appearance": appearance.duplicate(true)}
	_apply_model()

func _apply_model() -> void:
	if _model.is_empty() or label == null: return
	var id: String = str(_model.get("resourceId", ""))
	var state: Dictionary = _model.get("state", {})
	var appearance: Dictionary = visual_context.resource_appearance(id, _model.get("appearance", {}))
	var model = preload("res://scripts/presentation/view_models.gd").ResourceValue.new(state)
	label.text = visual_context.translator.content_name(id).to_upper()
	var bounded: bool = model.has_bounds and is_finite(model.current) and model.current >= float(state.get("minimum", 0)) and model.current <= float(state.get("maximum", 0))
	bar.visible = str(appearance.get("display", "bar")) == "bar" and bounded
	if bounded:
		bar.min_value = float(state.get("minimum", 0))
		bar.max_value = float(state.get("maximum", 0))
		bar.value = model.current
	var tone := Color.from_string(str(appearance.get("color", "")), visual_context.styles.color("resource.unknown"))
	bar.add_theme_stylebox_override("fill", visual_context.styles.box(tone, 5))
	amount.text = visual_context.translator.number(model.current)
	if state.has("maximum") and str(appearance.get("display", "bar")) != "counter": amount.text += "/" + visual_context.translator.number(float(state.maximum))
	amount.size_flags_horizontal = Control.SIZE_FILL if bar.visible else Control.SIZE_EXPAND_FILL
	bar.tooltip_text = amount.text
	amount.tooltip_text = amount.text
