@tool
extends RefCounted
## Narrow playback/animation adapter around the Playback cursor or an isolated test double.

var _target
var _generation := 0
var _frames: Array = []
var _index := 0

func _init(playback_target = null) -> void:
	_target = playback_target

var target:
	get: return _target

var generation: int:
	get:
		if is_instance_valid(_target) and "generation" in _target:
			return int(_target.generation)
		return _generation

var active_handle:
	get:
		if is_instance_valid(_target) and "active_handle" in _target:
			return _target.active_handle
		return null

var frames: Array:
	get:
		if is_instance_valid(_target) and "frames" in _target:
			return _target.frames
		return _frames.duplicate(true)

var total: int:
	get:
		if is_instance_valid(_target) and "total" in _target:
			return int(_target.total)
		return _frames.size()

var index: int:
	get:
		if is_instance_valid(_target) and "index" in _target:
			return int(_target.index)
		return _index

var executor: Callable:
	get:
		if is_instance_valid(_target) and "executor" in _target:
			return _target.executor
		return Callable()
	set(value):
		if is_instance_valid(_target) and "executor" in _target:
			_target.executor = value

var visual_resolver: Callable:
	get:
		if is_instance_valid(_target) and "visual_resolver" in _target:
			return _target.visual_resolver
		return Callable()
	set(value):
		if is_instance_valid(_target) and "visual_resolver" in _target:
			_target.visual_resolver = value

func has_frames() -> bool:
	if is_instance_valid(_target) and _target.has_method("has_frames"):
		return bool(_target.has_frames())
	return _index < _frames.size()

func has_blocking_work() -> bool:
	if is_instance_valid(_target) and _target.has_method("has_blocking_work"):
		return bool(_target.has_blocking_work())
	return has_frames()

func next_is_passive() -> bool:
	if is_instance_valid(_target) and _target.has_method("next_is_passive"):
		return bool(_target.next_is_passive())
	return false

func advance() -> bool:
	if is_instance_valid(_target) and _target.has_method("advance"):
		return bool(_target.advance())
	if _index < _frames.size():
		_index += 1
		return true
	return false

func skip() -> void:
	if is_instance_valid(_target) and _target.has_method("skip"):
		_target.skip()
	else:
		_index = _frames.size()

func clear(run_id := "") -> void:
	_generation += 1
	_frames.clear()
	_index = 0
	if is_instance_valid(_target) and _target.has_method("clear"):
		_target.clear(run_id)

func complete_active() -> void:
	if is_instance_valid(_target) and _target.has_method("complete_active"):
		_target.complete_active()

func release_facts() -> void:
	if is_instance_valid(_target) and _target.has_method("release_facts"):
		_target.release_facts()
	else:
		_frames.clear()
