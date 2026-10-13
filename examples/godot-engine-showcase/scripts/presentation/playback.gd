extends Node
## Disposable presentation cursor. Never sends commands or owns game state.
signal frame_presented(frame: Dictionary, index: int, total: int)
signal settled(result: String, generation: int)
var active_handle
var executor: Callable
var dispatch_ready := true
var envelope
var _measurements: Array = []
func measurements() -> Array: return _measurements.duplicate(true)
func release_facts() -> void:
	if has_blocking_work(): return
	_frames.clear(); envelope = null
func load_envelope(value) -> void:
	load_receipt(value.receipt())
	envelope = value; dispatch_ready = false
func activate(prefix: Array = []) -> void:
	_frames = prefix + _frames
	dispatch_ready = true
func annotate(values: Dictionary) -> void:
	for frame in _frames: frame.merge(values.duplicate(true), true)
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
	envelope = null; dispatch_ready = true

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
	if not dispatch_ready or active_handle != null or not has_frames():
		return false
	var current := index
	index += 1
	var ticket := generation
	var handle = preload("res://scripts/presentation/presentation_handle.gd").new()
	active_handle = handle
	handle.completed.connect(func(outcome):
		if generation != ticket or active_handle != handle: return
		active_handle = null
		if not has_frames():
			if envelope != null:
				envelope.mark("blocking_completed")
				_measurements.append({"scope": envelope.scope(), "result": outcome, "timings": envelope.timings()})
				if _measurements.size() > 64: _measurements.pop_front()
			settled.emit(outcome, ticket), CONNECT_ONE_SHOT)
	if envelope != null:
		envelope.mark("first_cue")
		handle.impact.connect(func(): if envelope != null and generation == ticket: envelope.mark("first_impact"), CONNECT_ONE_SHOT)
	handle.begin()
	var frame: Dictionary = _frames[current].duplicate(true)
	frame_presented.emit(frame, current, _frames.size())
	if executor.is_valid():
		handle.claimed = true
		executor.call(frame, handle)
	return true
