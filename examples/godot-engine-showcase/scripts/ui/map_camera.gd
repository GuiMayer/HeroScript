extends RefCounted
## Pointer navigation only. Never knows nodes, travel options or the session.
var pressed := false
var dragged := false
var _origin := Vector2.ZERO
var _scroll := Vector2.ZERO
var threshold := 7.0
func begin(point: Vector2, scroll: Vector2) -> void:
	pressed = true; dragged = false; _origin = point; _scroll = scroll
func motion(point: Vector2) -> Vector2:
	if not pressed: return _scroll
	var delta := point - _origin
	if delta.length() >= threshold: dragged = true
	return _scroll - delta if dragged else _scroll
func cancel() -> void: pressed = false; dragged = false
