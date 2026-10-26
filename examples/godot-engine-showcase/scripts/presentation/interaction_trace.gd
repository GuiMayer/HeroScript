extends RefCounted
## Local diagnostics only. Never added to engine commands, snapshots or replay facts.
var _clock: Callable
var _marks: Dictionary = {}
var _origin := 0
func _init(clock: Callable = Time.get_ticks_usec) -> void:
	_clock = clock
func begin() -> void:
	_marks.clear(); _origin = int(_clock.call()); mark("input")
func mark(event: String) -> void:
	if not _marks.has(event): _marks[event] = maxi(0, int(_clock.call()) - _origin)
func snapshot() -> Dictionary:
	return _marks.duplicate()
