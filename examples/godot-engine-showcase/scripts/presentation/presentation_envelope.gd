extends RefCounted
## Immutable visual revision, never a gameplay ledger. Getters copy at the boundary.
var _facts: Dictionary = {}
var _scope: Array = []
var _trace: Dictionary = {}
static func dictionary(value) -> Dictionary:
	# JSON null is not an absent key: Dictionary.get's default does not replace it.
	return value if value is Dictionary else {}
static func build(receipt: Dictionary, before: Dictionary, after: Dictionary, endpoint: Array):
	var previous := dictionary(before.get("run"))
	var current := dictionary(after.get("run"))
	if str(current.get("runId", "")).is_empty(): return null
	if previous.get("runId", "") != current.get("runId", "") or previous.get("branchId", "") != current.get("branchId", ""): return null
	var previous_setting := str(previous.get("settingId", previous.get("configName", "")))
	if not previous_setting.is_empty() and previous_setting != str(current.get("settingId", current.get("configName", ""))): return null
	if int(receipt.get("sequence", -1)) != int(current.get("sequence", -2)): return null
	var state := dictionary(receipt.get("state"))
	var resolution := dictionary(state.get("resolution"))
	var final_combat := dictionary(after.get("combat"))
	var combat: Dictionary = state.combat if state.get("combat") is Dictionary else final_combat
	if resolution.get("combatId", combat.get("combatId", "")) != combat.get("combatId", ""): return null
	if not final_combat.is_empty() and final_combat.get("combatId") != combat.get("combatId"): return null
	var result = load("res://scripts/presentation/presentation_envelope.gd").new()
	result._scope = [endpoint.duplicate(), current.get("settingId", current.get("configName", "")), current.get("runId", ""), current.get("branchId", ""),
		combat.get("combatId", ""), resolution.get("commandId", receipt.get("commandId", "")), current.get("sequence", 0), current.get("contentRevision", current.get("contentRevisionId", ""))]
	result._facts = {"receipt": receipt.duplicate(true), "before": before.duplicate(true), "after": after.duplicate(true), "combat": combat.duplicate(true)}
	result.mark("synchronized")
	return result
func scope() -> Array: return _scope.duplicate(true)
func snapshot(which: String) -> Dictionary: return _facts.get(which, {}).duplicate(true)
func receipt() -> Dictionary: return _facts.get("receipt", {}).duplicate(true)
func mark(name: String) -> void:
	if not _trace.has(name): _trace[name] = Time.get_ticks_msec()
func mark_at(name: String, timestamp: int) -> void:
	if not _trace.has(name): _trace[name] = timestamp
func timings() -> Dictionary: return _trace.duplicate(true)

static func visible_zone_steps(steps: Array, snapshots: Array) -> Array:
	var visible := {}
	for snapshot in snapshots:
		for zone in snapshot.get("zones", []):
			if not zone.get("contentsVisible", false): continue
			for card in zone.get("cards", []): visible[str(card.get("cardInstanceId", ""))] = true
	var result: Array = []
	for fact in steps:
		var step: Dictionary = fact.duplicate(true)
		for key in ["sourceOrderBefore", "sourceOrderAfter", "targetOrderBefore", "targetOrderAfter"]: step.erase(key)
		for key in ["instanceIds", "createdInstanceIds", "destroyedInstanceIds"]:
			var ids: Array = step.get(key, [])
			step[key] = ids.filter(func(id): return visible.has(str(id)))
			step[key + "HiddenCount"] = ids.size() - step[key].size()
		result.append(step)
	return result
