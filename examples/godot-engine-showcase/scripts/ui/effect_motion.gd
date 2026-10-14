extends RefCounted
## Shared live/history executor. Recorded facts update immediately; only concrete
## impact reactions and moving bars join the handle unless the owning card cycle
## explicitly delegates them to the feedback's independent cosmetic lifecycle.
const Cues = preload("res://scripts/presentation/semantic_cues.gd")
static func can_animate(frame: Dictionary, context, portraits: Dictionary, panels: Dictionary, cosmetic := false) -> bool:
	if cosmetic: return false
	if context.motion.duration("impact", true) <= 0: return false
	for app in frame.get("applications", []):
		if Cues.kind(app, frame) != "impact" or not context.motion.cue("impact").get("enabled", true): continue
		if app.get("previousValue") == null or app.get("currentValue") == null or is_equal_approx(float(app.previousValue), float(app.currentValue)): continue
		var id := str(app.get("targetEntityId", ""))
		if portraits.has(id) and portraits[id].has_method("flash_hit"): return true
		if panels.has(id):
			var resource = panels[id].find_child("Resource_" + str(app.get("resourceId", "")).validate_node_name(), true, false)
			if resource != null and resource.bar.visible: return true
	return false
static func execute(frame: Dictionary, handle, context, feedback, presenter, portraits: Dictionary, panels: Dictionary, index: int, total: int, audio: Callable = Callable(), cosmetic := false) -> String:
	var operations: Array = []
	var text: String = feedback.present(frame, index, total, presenter, portraits, operations)
	for id in portraits if not cosmetic else {}:
		if not portraits[id].has_method("settle_motion"): continue
		var portrait_ref: WeakRef = weakref(portraits[id])
		handle.cleanup(func():
			var portrait = portrait_ref.get_ref()
			if portrait != null: portrait.settle_motion())
	for app in frame.get("applications", []):
		var id := str(app.get("targetEntityId", ""))
		if not panels.has(id): continue
		var duration: float = context.motion.duration("impact", true) if Cues.kind(app, frame) == "impact" and context.motion.cue("impact").get("enabled", true) else 0.0
		var movement = panels[id].present_resource(app, duration)
		if movement is Tween:
			var resource_ref: WeakRef = weakref(panels[id].find_child("Resource_" + str(app.get("resourceId", "")).validate_node_name(), true, false))
			var settle := func():
				var resource = resource_ref.get_ref()
				if resource != null: resource.settle_motion()
			if cosmetic: feedback.track_cosmetic(movement, settle)
			else: operations.append(movement); handle.cleanup(settle)
	handle.hit()
	if audio.is_valid(): audio.call(frame, context.motion)
	if cosmetic:
		var targets: Array = portraits.values().map(func(portrait): return weakref(portrait))
		for reaction in operations:
			feedback.track_cosmetic(reaction, func():
				for target in targets:
					var portrait = target.get_ref()
					if portrait != null and portrait.has_method("settle_motion"): portrait.settle_motion())
		handle.finish()
	else: handle.join(operations)
	return text
