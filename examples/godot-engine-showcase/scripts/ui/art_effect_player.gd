extends RefCounted
## One local cosmetic lane per ArtSlot; no timers, queues, REST or model mutation.
const FeedbackShader = preload("res://shaders/art_feedback.gdshader")
var style = preload("res://scripts/ui/styles/art_effect_style.gd").new()
var _target: WeakRef
var _previous: Material
var _material: ShaderMaterial
var tween: Tween
func _init(target: TextureRect) -> void: _target = weakref(target)
func _bind() -> void:
	var target = _target.get_ref()
	if _material != null: return
	_previous = target.material
	_material = ShaderMaterial.new(); _material.shader = FeedbackShader
	_material.set_shader_parameter("impact_color", style.impact_color)
	target.material = _material
func play(effect: String, seconds: float, strength: float, reduced := false, instant := false) -> Tween:
	clear()
	var target = _target.get_ref()
	if effect != "impact" or not is_finite(seconds) or seconds <= 0 or not is_finite(strength) or strength <= 0 or reduced or instant: return null
	if not is_instance_valid(target) or not target.is_inside_tree() or target.texture == null: return null
	_bind()
	tween = target.create_tween().set_pause_mode(Tween.TWEEN_PAUSE_BOUND)
	tween.tween_method(func(progress):
		if _material != null: _material.set_shader_parameter("impact_amount", sin(progress * PI) * clampf(strength,0,1)),
		0.,1.,clampf(seconds,0,2)).set_trans(Tween.TRANS_LINEAR)
	tween.finished.connect(clear, CONNECT_ONE_SHOT)
	return tween
func clear() -> void:
	if tween != null and tween.is_valid(): tween.kill()
	tween = null
	var target = _target.get_ref()
	if is_instance_valid(target) and _material != null and target.material == _material: target.material = _previous
	_material = null; _previous = null
