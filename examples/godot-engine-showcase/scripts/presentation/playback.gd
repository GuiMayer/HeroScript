extends Node
## Disposable presentation cursor. Never sends commands or owns game state.
signal frame_presented(frame: Dictionary, index: int, total: int)
var _frames: Array = []
var index := 0
var total: int:
	get: return _frames.size()
var frames: Array:
	get: return _frames.duplicate(true)

func load_receipt(receipt: Dictionary) -> void:
	var resolution = receipt.get("state", {}).get("resolution", {})
	_frames = presentation_frames(resolution.get("frames", [])) if resolution is Dictionary else []
	index = 0

static func presentation_frames(engine_frames: Array) -> Array:
	# Group only explicitly identified condensed procs. Preserve every application
	# and the engine frame metadata; this cursor never rewrites journal frames.
	var result: Array = []
	for frame in engine_frames:
		var condensed: Array = []
		for step in frame.get("effectSteps", []):
			var identity: Dictionary = step.get("identity", {}) if step.get("identity") is Dictionary else {}
			var proc_id := str(identity.get("procId", ""))
			if step.get("condensation") != null and not proc_id.is_empty() and proc_id not in condensed: condensed.append(proc_id)
		if condensed.is_empty():
			result.append(frame.duplicate(true))
			continue
		var groups: Dictionary = {}
		for application in frame.get("applications", []):
			var identity: Dictionary = application.get("identity", {}) if application.get("identity") is Dictionary else {}
			var proc_id := str(identity.get("procId", ""))
			var key := proc_id if proc_id in condensed else "other"
			if not groups.has(key): groups[key] = []
			groups[key].append(application.duplicate(true))
		if groups.is_empty(): result.append(frame.duplicate(true))
		for key in groups:
			var projected: Dictionary = frame.duplicate(true)
			projected["applications"] = groups[key]
			projected["presentationProcId"] = key if key != "other" else ""
			projected["condensed"] = key != "other"
			result.append(projected)
	return result

func clear(_run_id := "") -> void:
	_frames = []
	index = 0

func has_frames() -> bool:
	return index < _frames.size()

func advance() -> bool:
	if not has_frames():
		return false
	var current := index
	index += 1
	frame_presented.emit(_frames[current].duplicate(true), current, _frames.size())
	return true
