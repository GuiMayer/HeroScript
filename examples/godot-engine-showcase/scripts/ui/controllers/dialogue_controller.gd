extends RefCounted
## Published option coordination only; never interprets narrative consequences.
signal changed
var _session
var _alive := true
var _epoch := 0
var _scope := ""
var _choices: Array = []
var _model: Dictionary = {}
var submitting := false
func _init(session) -> void: _session = session
func bind(run: Dictionary, choices: Array, model: Dictionary) -> void:
	var scope := JSON.stringify([run.get("runId", ""), run.get("branchId", ""), run.get("currentNodeId", ""),
		model.get("dialogueInstanceId", ""), model.get("nodeId", ""), model.get("contentRevision", "")])
	if scope != _scope: _epoch += 1
	_scope = scope; _choices = choices.duplicate(true); _model = model.duplicate(true)
func locked() -> bool: return not _alive or submitting or _session.busy or not _session.synchronized
func submit(reference: String) -> bool:
	if locked(): return false
	for option in _model.get("options", []):
		if str(option.get("reference", "")) == reference and bool(option.get("available", false)):
			return await _send(option.command)
	return false
func command(type: String) -> Dictionary:
	var matches: Array = _choices.filter(func(choice): return str(choice.get("type", "")) == type)
	return matches[0].duplicate(true) if matches.size() == 1 else {}
func execute_published(type: String) -> bool:
	var choice := command(type)
	return await _send(choice) if not choice.is_empty() and not locked() else false
func _send(choice: Dictionary) -> bool:
	if choice not in _choices: return false
	var ticket := _epoch
	submitting = true; changed.emit()
	var accepted: bool = await _session.submit_activity(choice.duplicate(true))
	if not _alive or ticket != _epoch: return false
	submitting = false; changed.emit()
	return accepted
func recover() -> bool: return await _session.refresh() if _alive and not submitting else false
func dispose() -> void: _alive = false; _epoch += 1
