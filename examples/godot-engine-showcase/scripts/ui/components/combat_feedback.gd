extends Node
## Consumes frames from the existing cursor. Never owns a second playback queue.
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
var effects := Control.new()
func _init(context) -> void:
	visual_context = context
	effects.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(effects)
func present(frame: Dictionary, index: int, total: int, presenter, portraits: Dictionary) -> String:
	var i18n = visual_context.translator
	var text: String = "%s %s/%s  •  %s" % [i18n.text("Animations"), index + 1, total, i18n.content_name(str(frame.get("transitionType", "")))]
	var flashed: Array[String] = []
	var lanes: Dictionary = {}
	for application in frame.get("applications", []):
		text += "\n" + presenter.application_text(application, true)
		var target := str(application.get("targetEntityId", ""))
		if portraits.has(target):
			var kind: String = preload("res://scripts/presentation/semantic_cues.gd").kind(application, frame)
			if kind == "impact" and not visual_context.reduced_motion and target not in flashed:
				portraits[target].flash_hit()
				flashed.append(target)
			var lane: int = lanes.get(target, 0)
			_float_application(application, portraits[target], lane, kind)
			lanes[target] = lane + 1
	return text
func _float_application(application: Dictionary, portrait: Control, lane := 0, kind := "fact") -> void:
	var previous = application.get("previousValue")
	var current = application.get("currentValue")
	var delta := 0.0
	if previous != null and current != null:
		delta = float(current) - float(previous)
		if is_zero_approx(delta): return
	elif application.get("statusId") == null and application.get("modifierId") == null: return
	var label := Label.new()
	label.text = "%s%s %s" % ["+" if delta > 0 else "", visual_context.translator.number(delta), visual_context.translator.content_name(str(application.get("resourceId", "")))] if previous != null else visual_context.translator.content_name(str(application.get("statusId", application.get("modifierId", ""))))
	if kind == "payment": label.text = visual_context.translator.text("Cost") + " " + label.text
	var field := str(application.get("resourceField", "Current"))
	if field not in ["Current", "0", "0.0"]: label.text += " " + visual_context.translator.text(field)
	label.add_theme_font_size_override("font_size", 22)
	label.add_theme_color_override("font_color", visual_context.styles.MUTED if kind == "payment" else (visual_context.styles.TEAL if delta > 0 else visual_context.styles.GOLD))
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.process_mode = Node.PROCESS_MODE_PAUSABLE
	effects.add_child(label)
	label.global_position = portrait.global_position + Vector2(20 + (lane % 3) * 12, 45 + lane * 27)
	var tween := label.create_tween().set_parallel()
	var duration: float = .6 / visual_context.animation_speed
	if not visual_context.reduced_motion: tween.tween_property(label, "position:y", label.position.y - 36, duration)
	tween.tween_property(label, "modulate:a", 0.0, duration)
	tween.chain().tween_callback(label.queue_free)
func dispose() -> void:
	for child in effects.get_children():
		effects.remove_child(child)
		child.queue_free()
