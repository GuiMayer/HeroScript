extends Node
## Disposable presentation cursor. Never sends commands or owns game state.
signal frame_presented(frame: Dictionary, index: int, total: int)
signal settled(result: String, generation: int)
var active_handle
var executor: Callable
var _frames: Array = []
var index := 0
var generation := 0
var total: int:
	get: return _frames.size()
var frames: Array:
	get: return _frames.duplicate(true)

func load_receipt(receipt: Dictionary) -> void:
	clear()
	var resolution = receipt.get("state", {}).get("resolution", {})
	_frames = presentation_frames(resolution.get("frames", [])) if resolution is Dictionary else []
	index = 0

static func presentation_frames(engine_frames: Array) -> Array:
	return preload("res://scripts/presentation/presentation_planner.gd").plan(engine_frames)

func clear(_run_id := "") -> void:
	generation += 1
	var old = active_handle
	active_handle = null
	if old != null: old.finish("cancelled")
	_frames = []
	index = 0

func has_frames() -> bool:
	return index < _frames.size()

func has_blocking_work() -> bool: return active_handle != null or has_frames()
func skip() -> void:
	index = _frames.size()
	if active_handle != null: active_handle.finish("skipped")
	else: settled.emit("skipped", generation)
func complete_active() -> void:
	if active_handle != null: active_handle.finish()

func advance() -> bool:
	if active_handle != null or not has_frames():
		return false
	var current := index
	index += 1
	var ticket := generation
	var handle = preload("res://scripts/presentation/presentation_handle.gd").new()
	active_handle = handle
	handle.completed.connect(func(outcome):
		if generation != ticket or active_handle != handle: return
		active_handle = null
		if not has_frames(): settled.emit(outcome, ticket), CONNECT_ONE_SHOT)
	handle.begin()
	var frame: Dictionary = _frames[current].duplicate(true)
	frame_presented.emit(frame, current, _frames.size())
	if executor.is_valid():
		handle.claimed = true
		executor.call(frame, handle)
	return true
