extends Node
## Disposable presentation cursor. Never sends commands or owns game state.
signal frame_presented(frame: Dictionary, index: int, total: int)
var _frames: Array = []
var index := 0
var generation := 0
var total: int:
	get: return _frames.size()
var frames: Array:
	get: return _frames.duplicate(true)

func load_receipt(receipt: Dictionary) -> void:
	generation += 1
	var resolution = receipt.get("state", {}).get("resolution", {})
	_frames = presentation_frames(resolution.get("frames", [])) if resolution is Dictionary else []
	index = 0

static func presentation_frames(engine_frames: Array) -> Array:
	return preload("res://scripts/presentation/presentation_planner.gd").plan(engine_frames)

func clear(_run_id := "") -> void:
	generation += 1
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
