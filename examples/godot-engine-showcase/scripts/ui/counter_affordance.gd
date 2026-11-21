extends Node
## A small acknowledgement of a new round or resource value; no layout mutation.
const LABELS := [&"Round", &"EnergyIndicator"]
var _label: Label
var _context
var _previous := ""
var _tween: Tween

static func attach(label: Label, context) -> void:
	if label.has_node("CounterAffordance") or not label.name in LABELS: return
	var effect = load("res://scripts/ui/counter_affordance.gd").new()
	effect.name = "CounterAffordance"
	effect._label = label
	effect._context = context
	label.add_child(effect)

func _ready() -> void:
	_previous = _label.text
	_label.offset_transform_enabled = true
	_label.offset_transform_visual_only = true
	_context.changed.connect(_reset)

func _process(_delta: float) -> void:
	if _label.text == _previous: return
	var had_value := not _previous.is_empty()
	_previous = _label.text
	_reset_pose()
	if not had_value or _previous.is_empty() or not _label.is_visible_in_tree(): return
	var seconds: float = _context.capture_motion().duration("hover", true)
	if seconds <= 0: return
	_label.offset_transform_scale = Vector2(1.045, 1.045)
	_label.offset_transform_position = Vector2(0, -2)
	_tween = create_tween().set_parallel().set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	_tween.tween_property(_label, "offset_transform_scale", Vector2.ONE, seconds)
	_tween.tween_property(_label, "offset_transform_position", Vector2.ZERO, seconds)

func _reset() -> void:
	_previous = _label.text
	_reset_pose()

func _reset_pose() -> void:
	if _tween and _tween.is_valid(): _tween.kill()
	_label.offset_transform_scale = Vector2.ONE
	_label.offset_transform_position = Vector2.ZERO
