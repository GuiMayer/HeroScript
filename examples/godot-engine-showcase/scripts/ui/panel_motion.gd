extends RefCounted
## Decorative visual offset and opacity; never changes geometry or hit areas.
static func cancel(content: CanvasItem) -> void:
	var old = content.get_meta("panel_motion") if content.has_meta("panel_motion") else null
	if old is Tween and old.is_valid(): old.kill()
	if content.has_meta("panel_motion"): content.remove_meta("panel_motion")
	content.modulate.a = 1.0
	if content is Control: content.offset_transform_position = Vector2.ZERO
static func enter(content: CanvasItem, context):
	cancel(content)
	var policy = context.capture_motion()
	var seconds: float = policy.duration("hover", true)
	if seconds <= 0 or not content.is_inside_tree(): return null
	content.modulate.a = 0.0
	var tween := content.create_tween().set_parallel()
	content.set_meta("panel_motion", tween)
	tween.tween_property(content, "modulate:a", 1.0, seconds).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	if content is Control:
		content.offset_transform_enabled = true
		content.offset_transform_visual_only = true
		content.offset_transform_position = Vector2(0, 8)
		tween.tween_property(content, "offset_transform_position", Vector2.ZERO, seconds).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	return tween
