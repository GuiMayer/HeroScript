extends RefCounted
## Shared live/history executor. Recorded facts update immediately; only concrete
## impact reactions and moving bars join the blocking handle.
const Cues = preload("res://scripts/presentation/semantic_cues.gd")
static func can_animate(frame: Dictionary, context, portraits: Dictionary, panels: Dictionary) -> bool:
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
static func execute(frame: Dictionary, handle, context, feedback, presenter, portraits: Dictionary, panels: Dictionary, index: int, total: int, audio: Callable = Callable()) -> String:
	var operations: Array = []
	var text: String = feedback.present(frame, index, total, presenter, portraits, operations)
	for id in portraits:
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
			operations.append(movement)
			var resource_ref: WeakRef = weakref(panels[id].find_child("Resource_" + str(app.get("resourceId", "")).validate_node_name(), true, false))
			handle.cleanup(func():
				var resource = resource_ref.get_ref()
				if resource != null: resource.settle_motion())
	handle.hit()
	if audio.is_valid(): audio.call(frame, context.motion)
	handle.join(operations)
	return text
