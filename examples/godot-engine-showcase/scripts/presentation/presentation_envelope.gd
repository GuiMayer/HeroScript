extends RefCounted
## Immutable visual revision, never a gameplay ledger. Getters copy at the boundary.
var _facts: Dictionary = {}
var _scope: Array = []
var _trace: Dictionary = {}
static func build(receipt: Dictionary, before: Dictionary, after: Dictionary, endpoint: Array):
	var previous: Dictionary = before.get("run", {})
	var current: Dictionary = after.get("run", {})
	if previous.get("runId", "") != current.get("runId", "") or previous.get("branchId", "") != current.get("branchId", ""): return null
	if int(receipt.get("sequence", -1)) != int(current.get("sequence", -2)): return null
	var resolution: Dictionary = receipt.get("state", {}).get("resolution", {})
	var combat: Dictionary = receipt.get("state", {}).get("combat", after.get("combat", {}))
	if resolution.get("combatId", combat.get("combatId", "")) != combat.get("combatId", ""): return null
	var final_combat: Dictionary = after.get("combat", {})
	if not final_combat.is_empty() and final_combat.get("combatId") != combat.get("combatId"): return null
	var result = load("res://scripts/presentation/presentation_envelope.gd").new()
	result._scope = [endpoint.duplicate(), current.get("settingId", current.get("configName", "")), current.get("runId", ""), current.get("branchId", ""),
		combat.get("combatId", ""), resolution.get("commandId", receipt.get("commandId", "")), current.get("sequence", 0), current.get("contentRevision", "")]
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
