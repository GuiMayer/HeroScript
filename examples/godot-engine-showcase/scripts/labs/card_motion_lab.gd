extends Node
## Visual fixtures only, executed through production presentation components.
signal finished(models: Array, metrics: Dictionary)
var hand
var context
var overlay := Control.new()
var playback
var director
var registry
var final_models: Array = []
var _models: Dictionary = {}
var _zones: Array = []
var _origin: Callable
var _destination: Callable
var _stage: Callable
var _serial := 0
var _started := 0
var _running := false
var _last_instant := false
var preset := "normal"
class Options extends RefCounted:
	var auto_animations := true
var options := Options.new()
func _init(hand_view, visual_context, origin: Callable, destination: Callable, stage: Callable) -> void:
	hand = hand_view; context = visual_context; _origin = origin; _destination = destination; _stage = stage
	overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE; add_child(overlay)
	playback = preload("res://scripts/presentation/playback.gd").new(); add_child(playback)
	director = preload("res://scripts/ui/controllers/presentation_director.gd").new(playback, options, func(): return get_tree().paused); add_child(director)
	playback.executor = _execute; playback.visual_resolver = _can_animate; playback.settled.connect(_settle)
func play(scenario: Dictionary, initial: Array) -> void:
	if _running: skip()
	director.reset_metrics(); _started = Time.get_ticks_usec()
	_serial += 1
	context.motion = preload("res://scripts/presentation/motion_policy.gd").capture(context.setting_id,
		{"animation_preset": preset, "reduced_motion": context.reduced_motion, "animation_speed": context.animation_speed})
	final_models = initial.duplicate(true); _models.clear()
	for model in initial: _models[str(model.cardInstanceId)] = model
	var frames: Array = []; var reservations: Array = []
	var operation := str(scenario.operation); var count: int = int(scenario.get("count", 1))
	if operation in ["draw", "draw_discard", "create"] and not initial.is_empty():
		var arrivals: Array = []
		for index in count:
			var model: Dictionary = initial[index % initial.size()].duplicate(true)
			model.cardInstanceId = "motion:%s:%s" % [_serial, index]; model.labId = model.cardInstanceId
			_models[model.cardInstanceId] = model; final_models.append(model); arrivals.append(model.cardInstanceId)
		frames.append(_flow("" if operation == "create" else "origin", "hand", arrivals, "createdInstanceIds" if operation == "create" else "instanceIds"))
		if operation == "draw_discard":
			reservations = final_models.duplicate(); frames.append(_flow("hand", "destination", arrivals)); final_models = initial.duplicate(true)
	elif operation in ["use", "retained"] and not initial.is_empty():
		var id := str(initial[0].cardInstanceId)
		frames.append({"presentationOnly": "card_departure", "cardInstanceId": id, "retained": operation == "retained", "continued": operation == "use"})
		if operation == "use": final_models = initial.slice(1); frames.append(_flow("hand", "destination", [id]))
	elif operation in ["discard", "destroy"]:
		frames.append(_flow("hand", "" if operation == "destroy" else "destination", initial.map(func(model): return str(model.cardInstanceId)), "destroyedInstanceIds" if operation == "destroy" else "instanceIds")); final_models = []
	elif operation == "reorder": final_models.reverse(); frames.append({"transitionType": "reorder"})
	else:
		for _index in count:
			frames.append({"transitionType": "activation-start" if operation == "lifecycle" else "effect", "applications": [] if operation == "lifecycle" else [{"effectType": "DAMAGE", "targetEntityId": "absent", "previousValue": 1, "currentValue": 0 if operation == "missing_target" else 1}]})
	var visible := initial + final_models + reservations
	_zones = [{"zones": [{"scopeId": "lab", "ownerId": "actor", "zoneId": "hand", "contentsVisible": true, "cards": visible}]},
		{"zones": [{"scopeId": "lab", "ownerId": "actor", "zoneId": "destination", "contentsVisible": true, "cards": visible}]}]
	hand.clear_choice(); hand.set_read_only(true); hand.begin_presentation(reservations if not reservations.is_empty() else final_models)
	registry = preload("res://scripts/ui/card_transit_registry.gd").new(hand, overlay, context)
	_running = true
	playback.load_receipt({"state": {"resolution": {"frames": frames}}}); director._process(0)
func _flow(source: String, destination: String, ids: Array, key := "instanceIds") -> Dictionary:
	var step := {"sourceAddress": "lab::actor::" + source, "targetAddress": "lab::actor::" + destination}; step[key] = ids
	return {"transitionType": "effect", "cardZoneSteps": [step]}
func _anchor(address: String, id: String) -> Vector2:
	if address == "lab::actor::hand": return hand.destination_center(id)
	if address == "lab::actor::origin": return _origin.call()
	if address == "lab::actor::destination": return _destination.call()
	return Vector2.INF
func _role(address: String) -> String: return "hand" if address == "lab::actor::hand" else "pile"
func _can_animate(frame: Dictionary) -> bool:
	if frame.get("presentationOnly") == "card_departure": return context.motion.duration("card_flight", true) > 0
	return not preload("res://scripts/ui/card_zone_motion.gd").resolve(frame.get("cardZoneSteps", []), _zones, _anchor, context.motion, _role).is_empty()
func _execute(frame: Dictionary, handle) -> void:
	_last_instant = not _can_animate(frame)
	if frame.get("presentationOnly") == "card_departure":
		GameAudio.card()
		preload("res://scripts/ui/card_use_motion.gd").execute(str(frame.cardInstanceId), bool(frame.retained), bool(frame.continued), hand, registry, context, _stage, handle)
	else:
		preload("res://scripts/ui/card_zone_motion.gd").execute(frame.get("cardZoneSteps", []), _zones, _models, _anchor, overlay, context, handle, _role, registry,
			func(item): if _role(item.target) == "hand": GameAudio.play_event("card_move"))
func _settle(result: String, _generation: int) -> void:
	if not _running: return
	_running = false
	if registry != null: registry.dispose(); registry = null
	hand.end_presentation(); hand.sync_models(final_models, hand.scope); hand.set_read_only(false)
	var metrics: Dictionary = director.metrics()
	metrics["dispatches"] = playback.index
	if _last_instant: metrics["instant"] += 1
	metrics["elapsed_ms"] = (Time.get_ticks_usec() - _started) / 1000.0; metrics["result"] = result
	finished.emit(final_models.duplicate(true), metrics)
func skip() -> void: director.skip()
func dispose() -> void:
	if director != null: director.dispose()
	if registry != null: registry.dispose(); registry = null
func _exit_tree() -> void: dispose()
