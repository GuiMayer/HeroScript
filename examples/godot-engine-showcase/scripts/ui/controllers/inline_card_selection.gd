extends RefCounted
## Local intent only; the engine validates eligibility and applies all consequences.
signal preview_requested(command: Dictionary, generation: int)
signal ready(preview: Dictionary)
signal changed
var command := {}
var snapshot := {}
var chosen := {}
var generation := 0
var _accepted := -1
var active: bool:
	get: return not command.is_empty()
func start(intent: Dictionary) -> void:
	cancel()
	command = intent.duplicate(true)
	refresh()
func cancel() -> void:
	generation += 1
	command = {}; snapshot = {}; chosen = {}
	changed.emit()
func requirements() -> Array:
	return snapshot.get("evaluation", {}).get("paymentRequirements", []).filter(func(item): return item.get("costOptionId") == null or item.costOptionId == command.get("costOptionId"))
func intent() -> Dictionary:
	var result := command.duplicate(true)
	result["paymentSelections"] = []
	for item in requirements():
		var ids: Array = chosen.get(key(item), [])
		if not ids.is_empty(): result.paymentSelections.append({"componentId": item.componentId, "paymentId": item.paymentId, "cardInstanceIds": ids.duplicate()})
	return result
func key(item: Dictionary) -> String: return str(item.componentId) + ":" + str(item.paymentId)
func pick(id: String) -> void:
	if not active: return
	for item in requirements():
		if id not in item.get("eligibleCardIds", []): continue
		var ids: Array = chosen.get(key(item), []).duplicate()
		if id in ids: ids.erase(id)
		elif ids.size() < int(item.count): ids.append(id)
		else: continue
		chosen[key(item)] = ids
		refresh()
		return
func target(id: String) -> void:
	if id not in snapshot.get("evaluation", {}).get("legalTargetIds", []): return
	command["targetIds"] = [id]
	refresh()
func cost(id: String) -> void:
	command["costOptionId"] = id; chosen.clear(); refresh()
func refresh() -> void:
	generation += 1
	changed.emit()
	preview_requested.emit(intent(), generation)
func accept(response: Dictionary, ticket: int) -> void:
	if not active or ticket != generation or ticket == _accepted: return
	if not response.get("ok", false):
		if int(response.get("status", 0)) == 409: cancel()
		return
	snapshot = response.get("data", {}).duplicate(true)
	_accepted = ticket
	var options: Array = snapshot.get("evaluation", {}).get("affordableCostOptionIds", [])
	if not command.has("costOptionId") and options.size() == 1:
		cost(str(options[0])); return
	changed.emit()
	if snapshot.get("isLegal", false): ready.emit(snapshot.duplicate(true))
