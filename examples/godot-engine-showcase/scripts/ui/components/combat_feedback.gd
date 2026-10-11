extends Node
## Consumes frames from the existing cursor. Never owns a second playback queue.
var visual_context
var effects := Control.new()
func _init(context) -> void:
	visual_context = context
	effects.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(effects)
func present(frame: Dictionary, index: int, total: int, presenter, portraits: Dictionary) -> String:
	var i18n = visual_context.translator
	var text: String = "%s %s/%s  •  %s" % [i18n.text("Animations"), index + 1, total, i18n.content_name(str(frame.get("transitionType", "")))]
	var flashed: Array[String] = []
	for application in frame.get("applications", []):
		text += "\n" + presenter.application_text(application, true)
		var target := str(application.get("targetEntityId", ""))
		if portraits.has(target):
			if not visual_context.reduced_motion and target not in flashed:
				portraits[target].flash_hit()
				flashed.append(target)
			_float_application(application, portraits[target])
	return text
func _float_application(application: Dictionary, portrait: Control) -> void:
	var previous = application.get("previousValue")
	var current = application.get("currentValue")
	if previous == null or current == null: return
	var delta := float(current) - float(previous)
	if is_zero_approx(delta): return
	var label := Label.new()
	label.text = "%s%s %s" % ["+" if delta > 0 else "", visual_context.translator.number(delta),
		visual_context.translator.content_name(str(application.get("resourceId", "")))]
	label.add_theme_font_size_override("font_size", 22)
	label.add_theme_color_override("font_color", AppTheme.TEAL if delta > 0 else AppTheme.GOLD)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.process_mode = Node.PROCESS_MODE_PAUSABLE
	effects.add_child(label)
	label.global_position = portrait.global_position + Vector2(20, 45)
	var tween := label.create_tween().set_parallel()
	var duration: float = .6 / visual_context.animation_speed
	if not visual_context.reduced_motion: tween.tween_property(label, "position:y", label.position.y - 36, duration)
	tween.tween_property(label, "modulate:a", 0.0, duration)
	tween.chain().tween_callback(label.queue_free)
func dispose() -> void:
	for child in effects.get_children():
		effects.remove_child(child)
		child.queue_free()
