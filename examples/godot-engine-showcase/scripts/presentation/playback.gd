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
	_frames = resolution.get("frames", []).duplicate(true) if resolution is Dictionary else []
	index = 0

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
