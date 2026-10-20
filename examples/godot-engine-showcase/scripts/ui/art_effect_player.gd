extends RefCounted
## One local cosmetic lane per ArtSlot; no timers, queues, REST or model mutation.
const FeedbackShader = preload("res://shaders/art_feedback.gdshader")
var style = preload("res://scripts/ui/styles/art_effect_style.gd").new()
var _target: WeakRef
var _previous: Material
var _material: ShaderMaterial
var tween: Tween
var _outline := ""
func _init(target: TextureRect) -> void:
	_target = weakref(target)
	target.resized.connect(_sync_geometry)
func _bind() -> void:
	var target = _target.get_ref()
	if _material != null: return
	_previous = target.material
	_material = ShaderMaterial.new(); _material.shader = FeedbackShader
	_material.set_shader_parameter("impact_color", style.impact_color)
	_material.set_shader_parameter("sheen_color", style.sheen_color)
	_material.set_shader_parameter("impact_amount",0.)
	_material.set_shader_parameter("sheen_progress",-1.)
	_material.set_shader_parameter("sheen_amount",0.)
	target.material = _material
	_sync_geometry()
func _sync_geometry() -> void:
	var target = _target.get_ref()
	if _material == null or not is_instance_valid(target) or target.texture == null: return
	var tex_size: Vector2 = target.texture.get_size()
	var draw_size: Vector2 = target.size
	if target.stretch_mode == TextureRect.STRETCH_KEEP_ASPECT_CENTERED and tex_size.x > 0 and tex_size.y > 0:
		draw_size = tex_size * minf(target.size.x / tex_size.x,target.size.y / tex_size.y)
	_material.set_shader_parameter("quad_size",draw_size.max(Vector2.ONE))
	_material.set_shader_parameter("expand_outline",target.stretch_mode == TextureRect.STRETCH_KEEP_ASPECT_CENTERED)
func warmup() -> void:
	# Draw a neutral material on the actual visible preview, not a hidden/culled node.
	var target = _target.get_ref()
	if DisplayServer.get_name() == "headless" or _material != null: return
	if not is_instance_valid(target) or target.texture == null or not target.is_inside_tree(): return
	_bind()
	var captured: ShaderMaterial = _material
	await RenderingServer.frame_post_draw
	# A click during warmup owns its effect; never clear it on this callback.
	if _material == captured: _release_if_idle()
func set_outline(state: String) -> void:
	var target = _target.get_ref()
	_outline = state if state in ["available","selected"] else ""
	if _outline.is_empty():
		if _material != null: _material.set_shader_parameter("outline_width",0.)
		_release_if_idle(); return
	if not is_instance_valid(target) or not target.is_inside_tree() or target.texture == null:
		_outline = ""; return
	_bind()
	_material.set_shader_parameter("outline_color",style.selected_color if _outline == "selected" else style.available_color)
	_material.set_shader_parameter("outline_width",style.selected_width if _outline == "selected" else style.available_width)
func play(effect: String, seconds: float, strength: float, reduced := false, instant := false) -> Tween:
	_stop_transient()
	var target = _target.get_ref()
	if effect not in ["impact","sheen"] or not is_finite(seconds) or seconds <= 0 or not is_finite(strength) or strength <= 0 or reduced or instant:
		_release_if_idle(); return null
	if not is_instance_valid(target) or not target.is_inside_tree() or target.texture == null:
		_release_if_idle(); return null
	_bind()
	if effect == "sheen":
		_material.set_shader_parameter("sheen_progress",0.)
		_material.set_shader_parameter("sheen_amount",clampf(strength,0,1))
	tween = target.create_tween().set_pause_mode(Tween.TWEEN_PAUSE_BOUND)
	tween.tween_method(func(progress):
		if _material == null: return
		if effect == "impact": _material.set_shader_parameter("impact_amount", sin(progress * PI) * clampf(strength,0,1))
		else:
			_material.set_shader_parameter("sheen_progress",progress)
			_material.set_shader_parameter("sheen_amount",clampf(strength,0,1)),
		0.,1.,clampf(seconds,0,2)).set_trans(Tween.TRANS_LINEAR)
	tween.finished.connect(func(): _stop_transient(); _release_if_idle(), CONNECT_ONE_SHOT)
	return tween
func _stop_transient() -> void:
	if tween != null and tween.is_valid(): tween.kill()
	tween = null
	if _material != null:
		_material.set_shader_parameter("impact_amount",0.)
		_material.set_shader_parameter("sheen_progress",-1.)
		_material.set_shader_parameter("sheen_amount",0.)
func _release_if_idle() -> void:
	if not _outline.is_empty() or tween != null: return
	var target = _target.get_ref()
	if is_instance_valid(target) and _material != null and target.material == _material: target.material = _previous
	_material = null; _previous = null
func clear() -> void:
	_stop_transient(); _outline = ""; _release_if_idle()
