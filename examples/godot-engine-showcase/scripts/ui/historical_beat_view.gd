@tool
extends VBoxContainer
## Read-only session of the same planner/director/feedback used by live combat.
signal settled(result: String)
var visual_context
var playback
var director
var history
var feedback
var status: Label
var _settled := false
func _finish(result: String) -> void:
	if _settled: return
	if result in ["skipped", "cancelled"] and feedback != null: feedback.clear_cosmetics()
	_settled = true; settled.emit(result)
func set_paused(value: bool) -> void:
	process_mode = Node.PROCESS_MODE_DISABLED if value else Node.PROCESS_MODE_INHERIT
class Options extends RefCounted:
	var auto_animations := true
	var animation_speed := 1.0
var _bound := false
static func create(context = null):
	var view = load("res://scenes/ui/components/historical_beat.tscn").instantiate()
	view.visual_context = context.derive(context.profile) if context != null else preload("res://scripts/ui/visual_context.gd").new()
	return view
func _ready() -> void:
	_bind_nodes()
func _bind_nodes() -> void:
	history = get_node("History"); feedback = get_node("Feedback"); status = get_node("Status")
	if visual_context == null: visual_context = preload("res://scripts/ui/visual_context.gd").new()
	theme = visual_context.theme
	if _bound: return
	_bound = true
	get_node("Controls/Next").pressed.connect(func(): if director: director.advance())
	get_node("Controls/Skip").pressed.connect(func(): if director: director.skip())
func load_commit(commit: Dictionary, appearance: Dictionary, automatic := true) -> void:
	_bind_nodes()
	var saved: Dictionary = preload("res://scripts/presentation/historical_presentation.gd").state(commit)
	visual_context.setting_id = str(saved.run.get("settingId", saved.run.get("configName", visual_context.setting_id)))
	visual_context.capture_motion()
	playback = preload("res://scripts/presentation/playback.gd").new(); add_child(playback)
	var options := Options.new(); options.auto_animations = automatic; options.animation_speed = visual_context.animation_speed
	director = preload("res://scripts/ui/controllers/presentation_director.gd").new(playback, options, func(): return get_tree().paused); add_child(director)
	history.visual_context = visual_context; history.display(saved, appearance)
	feedback.visual_context = visual_context
	get_node("Controls/Next").text = visual_context.translator.text("NEXT")
	get_node("Controls/Skip").text = visual_context.translator.text("Skip presentation")
	playback.executor = _execute
	playback.visual_resolver = _can_animate
	playback.settled.connect(func(result, _generation): _finish(result))
	playback.load_receipt({"state": {"resolution": {"frames": preload("res://scripts/presentation/historical_presentation.gd").frames(commit)}}})
func _execute(frame: Dictionary, handle) -> void:
	if frame.get("stateAfter") is Dictionary: history.apply_frame_snapshot(frame.stateAfter)
	var portraits := {}
	for id in history.actor_panels: portraits[id] = history.actor_panels[id].portrait_view
	status.text = preload("res://scripts/ui/effect_motion.gd").execute(frame, handle, visual_context, feedback, history.presenter, portraits, history.actor_panels, playback.index - 1, playback.total)
func _can_animate(frame: Dictionary) -> bool:
	var portraits := {}
	for id in history.actor_panels: portraits[id] = history.actor_panels[id].portrait_view
	return preload("res://scripts/ui/effect_motion.gd").can_animate(frame, visual_context, portraits, history.actor_panels)
func blocking() -> bool: return playback != null and playback.has_blocking_work()
func dispose() -> void:
	if director: director.dispose()
	if feedback: feedback.dispose()
	_finish("cancelled")
func _exit_tree() -> void: dispose()
