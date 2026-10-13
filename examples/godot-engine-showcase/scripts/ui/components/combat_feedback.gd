extends Node
## Consumes frames from the existing cursor. Never owns a second playback queue.
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
var effects := Control.new()
var _active_lanes: Dictionary = {}
var _tails: Array = []
func _init(context) -> void:
	visual_context = context
	effects.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(effects)
func present(frame: Dictionary, index: int, total: int, presenter, portraits: Dictionary, blocking: Array = []) -> String:
	if visual_context.motion == null: visual_context.capture_motion()
	var i18n = visual_context.translator
	var text: String = "%s %s/%s  •  %s" % [i18n.text("Animations"), index + 1, total, i18n.content_name(str(frame.get("transitionType", "")))]
	var flashed: Array[String] = []
	var lanes: Dictionary = {}
	for application in frame.get("applications", []):
		text += "\n" + presenter.application_text(application, true)
		var highlights := _random_text(frame, application)
		if not highlights.is_empty(): text += "\n" + highlights
		var target := str(application.get("targetEntityId", ""))
		if portraits.has(target):
			var kind: String = preload("res://scripts/presentation/semantic_cues.gd").kind(application, frame)
			var has_delta: bool = application.get("previousValue") != null and application.get("currentValue") != null and not is_equal_approx(float(application.previousValue), float(application.currentValue))
			if kind == "impact" and has_delta and not visual_context.reduced_motion and target not in flashed and portraits[target].has_method("flash_hit") and visual_context.motion.cue(kind).get("enabled", true):
				var reaction = portraits[target].call("flash_hit")
				if reaction is Tween: blocking.append(reaction)
				flashed.append(target)
			var lane: int = lanes.get(target, 0)
			_float_application(application, portraits[target], lane, kind)
			lanes[target] = lane + 1
	return text

func _random_text(frame: Dictionary, application: Dictionary) -> String:
	var parts: Array[String] = []
	var labels: Dictionary = visual_context.motion.random_labels()
	var impact_id := str(application.get("identity", {}).get("impactId", "")) if application.get("identity") is Dictionary else ""
	for step in frame.get("effectSteps", []):
		if impact_id.is_empty() or not step.get("identity") is Dictionary or str(step.identity.get("impactId", "")) != impact_id: continue
		for outcome in step.get("randomInputs", []):
			var rule: Dictionary = labels.get(str(outcome.get("inputId", "")), {})
			if rule.is_empty(): continue
			for checkpoint in rule.get("checkpoints", {}):
				var value = outcome.get("calculation", {}).get("checkpoints", {}).get(checkpoint) if outcome.get("calculation") is Dictionary else null
				if value != null: parts.append("%s: %s" % [visual_context.translator.text(str(rule.checkpoints[checkpoint])), visual_context.translator.number(float(value))])
			if rule.has("success_label"): parts.append(visual_context.translator.text(str(rule.success_label)) + (" ✓" if outcome.get("success", false) else " —"))
	return " · ".join(parts)
func _float_application(application: Dictionary, portrait: Control, lane := 0, kind := "fact") -> void:
	var previous = application.get("previousValue")
	var current = application.get("currentValue")
	var delta := 0.0
	if previous != null and current != null:
		delta = float(current) - float(previous)
		if is_zero_approx(delta): return
	elif application.get("statusId") == null and application.get("modifierId") == null: return
	if effects.get_child_count() >= visual_context.motion.limit("max_floats"): return
	var target := str(application.get("targetEntityId", ""))
	if not _active_lanes.has(target): _active_lanes[target] = []
	if _active_lanes[target].size() >= 6: return
	lane = 0
	while lane in _active_lanes[target]: lane += 1
	_active_lanes[target].append(lane)
	var label := Label.new()
	label.tree_exiting.connect(func():
		if _active_lanes.has(target): _active_lanes[target].erase(lane), CONNECT_ONE_SHOT)
	label.text = "%s%s %s" % ["+" if delta > 0 else "", visual_context.translator.number(delta), visual_context.translator.content_name(str(application.get("resourceId", "")))] if previous != null else visual_context.translator.content_name(str(application.get("statusId", application.get("modifierId", ""))))
	if kind == "payment": label.text = visual_context.translator.text("Cost") + " " + label.text
	else:
		var cue: Dictionary = visual_context.motion.cue(kind)
		if cue.has("label"): label.text = visual_context.translator.text(str(cue.label)) + " " + label.text
	var field := str(application.get("resourceField", "Current"))
	if field not in ["Current", "0", "0.0"]: label.text += " " + visual_context.translator.text(field)
	label.add_theme_font_size_override("font_size", 22)
	label.add_theme_color_override("font_color", visual_context.styles.MUTED if kind == "payment" else (visual_context.styles.TEAL if delta > 0 else visual_context.styles.GOLD))
	var cue_color := str(visual_context.motion.cue(kind).get("color", ""))
	if not cue_color.is_empty(): label.add_theme_color_override("font_color", Color.from_string(cue_color, visual_context.styles.INK))
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.process_mode = Node.PROCESS_MODE_PAUSABLE
	effects.add_child(label)
	label.global_position = portrait.global_position + Vector2(20 + (lane % 3) * 12, 45 + lane * 27)
	var tween := label.create_tween().set_parallel()
	_tails.append(tween)
	tween.finished.connect(func(): _tails.erase(tween), CONNECT_ONE_SHOT)
	var duration: float = visual_context.motion.duration("float")
	if duration <= 0: label.queue_free(); return
	if not visual_context.motion.reduced: tween.tween_property(label, "position:y", label.position.y - 36, duration)
	tween.tween_property(label, "modulate:a", 0.0, duration)
	tween.chain().tween_callback(label.queue_free)
func clear_cosmetics() -> void:
	for tween in _tails:
		if tween.is_valid(): tween.kill()
	_tails.clear()
	for child in effects.get_children():
		if not child is Label: continue
		effects.remove_child(child)
		child.queue_free()
	_active_lanes.clear()
func dispose() -> void:
	clear_cosmetics()
	for child in effects.get_children(): effects.remove_child(child); child.queue_free()
