extends RefCounted
## Application coordination for published activity choices, no UI construction.
var _session
var _alive := true
var _epoch := 0
var _scope := ""
var _choices: Array = []
var verifying := false
var recovering := false
var automatic_recovery_attempted := false
var auto_advancing := false
func _init(session) -> void: _session = session
func bind(run: Dictionary, choices: Array) -> void:
	var scope := str(run.get("runId", "")) + "/" + str(run.get("branchId", ""))
	if scope != _scope:
		_epoch += 1
		automatic_recovery_attempted = false
	_scope = scope
	_choices = choices.duplicate(true)
static func option_reference(choice: Dictionary) -> String:
	return JSON.stringify([choice.get("type", ""), choice.get("subjectId", ""), choice.get("payload", choice.get("validPayload", {}))]).sha256_text()
func choice_for(reference_id: String) -> Dictionary:
	var matches := _choices.filter(func(choice): return option_reference(choice) == reference_id)
	return matches[0].duplicate(true) if matches.size() == 1 else {}
func execute(choice: Dictionary) -> bool:
	if not _alive or _session.busy or choice not in _choices: return false
	var ticket := _epoch
	var accepted: bool = await _session.submit_activity(choice.duplicate(true))
	return _alive and ticket == _epoch and accepted
func preview_transformation(choice: Dictionary) -> Dictionary:
	if not _alive or choice not in _choices: return {}
	var ticket := _epoch
	var result: Dictionary = await _session.preview_transformation(choice.duplicate(true))
	return result if _alive and ticket == _epoch else {}
func recover() -> bool:
	if recovering or not _alive or _session.busy: return false
	recovering = true
	var ticket := _epoch
	var accepted: bool = await _session.refresh()
	recovering = false
	return _alive and ticket == _epoch and accepted
func forced_advance() -> Dictionary:
	return preload("res://scripts/application/activity_choices.gd").only_forced_advance(_choices).duplicate(true)
func verify() -> Dictionary:
	if verifying or not _alive: return {"ok": false}
	verifying = true
	var ticket := _epoch
	var result: Dictionary = await _session.verify()
	verifying = false
	return result if _alive and ticket == _epoch else {"ok": false}
func dispose() -> void:
	_alive = false
	_epoch += 1
