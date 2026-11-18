@tool
class_name CombatFeedback
extends Node
## Consumes frames from the existing cursor. Never owns a second playback queue.
const ContextType = preload("res://scripts/ui/visual_context.gd")
const Cues = preload("res://scripts/presentation/semantic_cues.gd")
var visual_context: ContextType
var effects: Control
var _active_lanes: Dictionary = {}
var _tails: Array = []
var _tail_cleanup: Dictionary = {}
func track_cosmetic(tween: Tween, cleanup: Callable = Callable()) -> void:
	if tween == null or not tween.is_valid(): return
	_tails = _tails.filter(func(item): return item.is_valid())
	for old in _tail_cleanup.keys():
		if not old.is_valid(): _tail_cleanup.erase(old)
	_tails.append(tween)
	if cleanup.is_valid(): _tail_cleanup[tween] = cleanup
	tween.finished.connect(func(): _tails.erase(tween); _tail_cleanup.erase(tween), CONNECT_ONE_SHOT)
static func create(context) -> CombatFeedback:
	var feedback = (load("res://scenes/ui/components/combat_feedback.tscn") as PackedScene).instantiate()
	feedback.mount(context); return feedback
func _init(context = null) -> void:
	visual_context = context if context != null else ContextType.new()
func _ready() -> void: effects = get_node("Effects")
func mount(context) -> void:
	visual_context = context; effects = get_node("Effects")
func present(frame: Dictionary, index: int, total: int, presenter, portraits: Dictionary, blocking: Array = []) -> String:
	if visual_context.motion == null: visual_context.capture_motion()
	var flashed: Array[String] = []
	for entry in _float_entries(frame):
		var application: Dictionary = entry.application
		var target := str(application.get("targetEntityId", ""))
		if portraits.has(target):
			var kind: String = entry.kind
			var has_delta: bool = application.get("previousValue") != null and application.get("currentValue") != null and not is_equal_approx(float(application.previousValue), float(application.currentValue))
			if kind == "impact" and has_delta and not visual_context.reduced_motion and target not in flashed and portraits[target].has_method("flash_hit") and visual_context.motion.cue(kind).get("enabled", true):
				var reaction = portraits[target].call("flash_hit")
				if reaction is Tween: blocking.append(reaction)
				flashed.append(target)
			_float_application(application, portraits[target], 0, kind)
	return describe_frame(frame, index, total, presenter)

func describe_frame(frame: Dictionary, index: int, total: int, presenter) -> String:
	var i18n = visual_context.translator
	var transition := str(frame.get("transitionType", ""))
	var text: String = "%s %s/%s  •  %s" % [i18n.text("Animations"), index + 1, total, i18n.content_name(transition)]
	if visual_context.setting_id in ["prototype-test", "alisyum"]:
		var title: String = {
			"combat.action.applied": "Action resolved",
			"combat.activation.started": "Turn started",
			"combat.activation.ended": "Turn ended",
		}.get(transition.to_lower(), "Combat update")
		text = i18n.text(title)
	for application in frame.get("applications", []):
		text += "\n" + presenter.application_text(application, true)
		var highlights := _random_text(frame, application)
		if not highlights.is_empty(): text += "\n" + highlights
	return text

func _float_entries(frame: Dictionary) -> Array:
	var result: Array = []
	var latest := {}
	var compact := visual_context.setting_id in ["prototype-test", "alisyum"]
	for source in frame.get("applications", []):
		var kind := Cues.kind(source, frame)
		var application: Dictionary = source.duplicate()
		if compact and source.get("resourceId") != null and source.get("currentValue") != null and source.get("previousValue") != null:
			var key := [source.get("targetEntityId"), source.resourceId, source.get("resourceField", "Current"), kind]
			if latest.has(key):
				var previous: Dictionary = result[latest[key]].application
				if previous.currentValue == source.previousValue:
					previous.currentValue = source.currentValue
					continue
			latest[key] = result.size()
		result.append({"application": application, "kind": kind})
	return result

func _random_text(frame: Dictionary, application: Dictionary) -> String:
	var parts: Array[String] = []
	var labels: Dictionary = visual_context.motion.random_labels() if visual_context.motion != null else {}
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
	if not visual_context.motion.cue(kind).get("enabled", true): return
	var duration: float = visual_context.motion.duration("float")
	if duration <= 0: return
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
	var label: Label = preload("res://scenes/ui/components/floating_effect_label.tscn").instantiate()
	label.tree_exiting.connect(func():
		if _active_lanes.has(target): _active_lanes[target].erase(lane), CONNECT_ONE_SHOT)
	var name_id := str(application.get("resourceId", ""))
	if name_id.is_empty(): name_id = str(application.get("statusId", application.get("modifierId", "")))
	var effect_name: String = visual_context.translator.content_name(name_id)
	label.text = ("%s%s %s" % ["+" if delta > 0 else "", visual_context.translator.number(delta), effect_name]).strip_edges() if previous != null and current != null else effect_name
	label.add_theme_font_size_override("font_size", roundi(22 * visual_context.text_scale))
	label.add_theme_color_override("font_outline_color", Color.BLACK)
	label.add_theme_constant_override("outline_size", 4)
	if kind == "payment": label.text = visual_context.translator.text("Cost") + " " + label.text
	else:
		var cue: Dictionary = visual_context.motion.cue(kind)
		if cue.has("label"): label.text = visual_context.translator.text(str(cue.label)) + " " + label.text
	var field := str(application.get("resourceField", "Current"))
	if field not in ["Current", "0", "0.0"]: label.text += " " + visual_context.translator.text(field)
	label.add_theme_color_override("font_color", visual_context.styles.MUTED if kind == "payment" else (visual_context.styles.TEAL if delta > 0 else visual_context.styles.GOLD))
	var cue_color := str(visual_context.motion.cue(kind).get("color", ""))
	if not cue_color.is_empty(): label.add_theme_color_override("font_color", Color.from_string(cue_color, visual_context.styles.INK))
	var campaign := visual_context.setting_id in ["prototype-test", "alisyum"]
	if campaign:
		var glyph: String = {"payment": "◇", "impact": "✦", "heal": "+", "status": "◆", "modifier": "◆"}.get(kind, "▰" if name_id == "block" else "")
		if not glyph.is_empty(): label.text = "%s  %s" % [glyph, label.text]
		label.add_theme_color_override("font_outline_color", Color.WHITE)
		var ink: Color = label.get_theme_color("font_color")
		label.add_theme_color_override("font_color", ink.darkened(0.24))
	effects.add_child(label)
	label.global_position = portrait.global_position + Vector2(20 + (lane % 3) * 12, 45 + lane * 27)
	var tween := label.create_tween().set_parallel()
	_tails.append(tween)
	tween.finished.connect(func(): _tails.erase(tween), CONNECT_ONE_SHOT)
	if not visual_context.motion.reduced: tween.tween_property(label, "position:y", label.position.y - (20 if campaign else 36), duration)
	var hold := duration * 0.6 if campaign else 0.0
	tween.tween_property(label, "modulate:a", 0.0, duration - hold).set_delay(hold)
	tween.chain().tween_callback(label.queue_free)
func clear_cosmetics() -> void:
	for tween in _tails:
		if tween.is_valid(): tween.kill()
	for cleanup in _tail_cleanup.values(): cleanup.call()
	_tail_cleanup.clear()
	_tails.clear()
	for child in effects.get_children():
		if not child is Label: continue
		effects.remove_child(child)
		child.queue_free()
	_active_lanes.clear()
func dispose() -> void:
	clear_cosmetics()
	for child in effects.get_children(): effects.remove_child(child); child.queue_free()
