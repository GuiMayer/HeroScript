extends RefCounted
## Bounded, UI-session memory only; no files, engine saves or global static state.
const Arrangement = preload("res://scripts/ui/hand_arrangement.gd")
var _states: Dictionary = {}
const LIMIT := 16
func state_for(scope: String):
	if scope.is_empty(): return Arrangement.new()
	if _states.has(scope): return _states[scope]
	if _states.size() >= LIMIT: _states.erase(_states.keys()[0])
	_states[scope] = Arrangement.new()
	return _states[scope]
func clear() -> void: _states.clear()
