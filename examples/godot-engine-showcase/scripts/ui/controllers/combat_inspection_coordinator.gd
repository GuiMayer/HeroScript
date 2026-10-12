extends RefCounted
## Read-only inspections, keyed by authoritative revision and actual target IDs.
## No scene references, gameplay commands or calculation authority.
class RequestCell extends RefCounted:
	signal completed
	var done := false
	var response: Dictionary = {}
	func finish(value: Dictionary) -> void:
		if done: return
		response = value.duplicate(true)
		done = true
		completed.emit()

const MAX_CACHE_ENTRIES := 16
var _controller
var _cache: Dictionary = {}
var _pending: Dictionary = {}
var _epoch := 0
var _alive := true

func _init(controller) -> void:
	_controller = controller

func inspect(revision: Array, targets: Array) -> Dictionary:
	if not _alive: return {"ok": false, "canceled": true}
	var key := JSON.stringify([revision, targets])
	if _cache.has(key): return _cache[key].duplicate(true)
	if _pending.has(key):
		var existing: RequestCell = _pending[key]
		if not existing.done: await existing.completed
		return existing.response.duplicate(true)
	var cell := RequestCell.new()
	_pending[key] = cell
	var ticket := _epoch
	var response: Dictionary = await _controller.inspect_hand(targets.duplicate(true))
	if not _alive or ticket != _epoch:
		cell.finish({"ok": false, "canceled": true})
		return cell.response.duplicate(true)
	_pending.erase(key)
	if response.get("ok", false):
		if _cache.size() >= MAX_CACHE_ENTRIES: _cache.erase(_cache.keys()[0])
		_cache[key] = response.duplicate(true)
	cell.finish(response)
	return cell.response.duplicate(true)

func invalidate() -> void:
	_epoch += 1
	_cache.clear()
	var cells: Array = _pending.values()
	_pending.clear()
	for cell in cells: cell.finish({"ok": false, "canceled": true})

func dispose() -> void:
	_alive = false
	invalidate()
