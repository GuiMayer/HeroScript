extends RefCounted
## One owner for page operations. Workspaces stage intentions; the session owns all commits/retries.
signal changed
var session
var identity: Array
var inscriptions
var busy := false
var uncertain := false
var message := "Loading equipment…"
var _alive := true
var _reading := false

func _init(owner) -> void:
	session = owner; identity = context()
	session.changed.connect(_session_changed)
func context() -> Array:
	var run: Dictionary = session.run
	return [run.get("runId", ""), run.get("playerEntityId", ""), run.get("settingId", run.get("configName", "")), run.get("branchId", "")]
func current() -> bool: return _alive and not str(identity[0]).is_empty() and context() == identity
func can_leave() -> bool: return not busy and not uncertain and not session.has_pending_command
func _session_changed() -> void:
	if not busy and not _reading: refresh()
func refresh() -> void:
	if _reading or busy or not current(): return
	_reading = true
	var response: Dictionary = await session.read_loadout(str(identity[1]))
	if not current(): _reading = false; changed.emit(); return
	if response.get("ok", false) and response.get("data") is Dictionary and valid(response.data):
		var candidate = preload("res://scripts/application/loadout_workspace.gd").new()
		if candidate.accept(response.data):
			if inscriptions != null:
				candidate.selected_item = inscriptions.selected_item; candidate.selected_slot = inscriptions.selected_slot; candidate.search = inscriptions.search
			inscriptions = candidate
	else: inscriptions = null
	_reading = false; changed.emit()
func valid(snapshot: Dictionary) -> bool:
	return current() and str(snapshot.get("actorId", "")) == str(identity[1]) and session.loadout_context_matches(snapshot)
func editable() -> bool:
	return current() and not busy and not _reading and not uncertain and not session.has_pending_command
func edit_inscription(method: String, args: Array = []) -> void:
	if not editable() or inscriptions == null: return
	busy = true; message = "Validating equipment…"; changed.emit()
	# Refresh version before constructing the candidate. A previous module may have committed.
	var response: Dictionary = await session.read_loadout(str(identity[1]))
	if not current(): _finish("Run or character changed. Reopen loadout."); return
	if not response.get("ok", false) or not response.get("data") is Dictionary or not valid(response.data):
		_finish("Equipment changed. Synchronizing…"); return
	var candidate = preload("res://scripts/application/loadout_workspace.gd").new()
	if not candidate.accept(response.data): _finish("Loadout unavailable."); return
	candidate.selected_slot = inscriptions.selected_slot; candidate.selected_item = inscriptions.selected_item
	candidate.callv(method, args)
	if not candidate.dirty(): _finish("No equipment changes."); return
	var draft: Dictionary = candidate.assignments.duplicate(true)
	response = await session.preview_loadout(candidate.source, draft)
	if not current(): _finish("Run or character changed. Reopen loadout."); return
	if not response.get("ok", false) or not response.get("data") is Dictionary or not candidate.accept_preview(response.data, candidate.generation, draft):
		_finish(str(response.get("error", "Equipment was not changed."))); return
	message = "Saving equipment…"; changed.emit()
	var ok: bool = await session.apply_loadout(candidate.source, draft)
	uncertain = session.has_pending_command
	_finish("Equipment saved automatically." if ok else "Equipment not saved. Recover or retry the operation.")
func _finish(status: String) -> void:
	busy = false; message = status
	if current() and not uncertain: await refresh()
	changed.emit()
func recover() -> void:
	if busy or not current(): return
	busy = true; message = "Recovering equipment…"; changed.emit()
	var ok: bool = await session.recover_pending()
	uncertain = session.has_pending_command
	_finish("Equipment synchronized." if ok else "Equipment recovery failed. Retry pending operation.")
func dispose() -> void:
	_alive = false
	if session.changed.is_connected(_session_changed): session.changed.disconnect(_session_changed)

