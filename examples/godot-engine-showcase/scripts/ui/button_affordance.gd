extends Node
## Godot 4.7 visual-only feedback. Container layout and hit rectangles stay fixed.
const ACTION_TYPES := [&"PrimaryAction", &"SecondaryAction", &"DangerAction", &"MenuLink"]
const REST := Vector2.ONE
const HOVER := Vector2(1.025, 1.025)
const FOCUSED := Vector2(1.012, 1.012)
const PRESSED := Vector2(1.025, 0.96)
var _button: Button
var _context
var _tween: Tween
var _held := false
var _target := REST

static func attach(button: Button, context) -> void:
	if button.has_node("ButtonAffordance") or not button.theme_type_variation in ACTION_TYPES: return
	var feedback = load("res://scripts/ui/button_affordance.gd").new()
	feedback.name = "ButtonAffordance"
	feedback._button = button
	feedback._context = context
	button.add_child(feedback)

func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	_button.offset_transform_enabled = true
	_button.offset_transform_visual_only = true
	_button.button_down.connect(func(): _held = true)
	_button.button_up.connect(func(): _held = false)
	_context.changed.connect(_reset)

func _process(_delta: float) -> void:
	if not _button.is_visible_in_tree() or _button.disabled or _context.reduced_motion:
		if _target != REST or _held or not _button.offset_transform_scale.is_equal_approx(REST): _reset()
		return
	var target := PRESSED if _held else (HOVER if _button.is_hovered() else (FOCUSED if _button.has_focus() else REST))
	if target == _target: return
	_target = target
	if _tween and _tween.is_valid(): _tween.kill()
	var seconds: float = _context.capture_motion().duration("hover", true)
	if seconds <= 0:
		_button.offset_transform_scale = REST
		return
	_tween = create_tween().set_trans(Tween.TRANS_QUAD if _held else Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	_tween.tween_property(_button, "offset_transform_scale", target, minf(seconds, .20) * (.55 if _held else 1.0))

func _reset() -> void:
	if _tween and _tween.is_valid(): _tween.kill()
	_held = false
	_target = REST
	_button.offset_transform_scale = REST
