extends RefCounted
## Local draft only. Capacity, eligibility and component composition belong to the engine.
var source: Dictionary = {}
var draft: Dictionary = {}
var preview: Dictionary = {}
var revision := 0

func load_snapshot(snapshot: Dictionary) -> void:
	source = snapshot.duplicate(true)
	draft = {}
	for allocation in source.get("allocations", []): draft[str(allocation.cardInstanceId)] = str(allocation.compartmentId)
	preview = source.duplicate(true)
	revision += 1

func allocations() -> Array:
	var result: Array = []
	var ids: Array = draft.keys(); ids.sort()
	for id in ids: result.append({"cardInstanceId": id, "compartmentId": draft[id]})
	return result

func dirty() -> bool:
	var original: Dictionary = {}
	for allocation in source.get("allocations", []): original[str(allocation.cardInstanceId)] = str(allocation.compartmentId)
	return original != draft

func stage(card_id: String, compartment_id: String) -> bool:
	if not source.get("canChange", false): return false
	if not source.get("cards", []).any(func(card): return str(card.cardInstanceId) == card_id): return false
	if compartment_id.is_empty(): draft.erase(card_id)
	else: draft[card_id] = compartment_id
	preview = {}
	revision += 1
	return true

func clear() -> void:
	draft = {}; preview = {}; revision += 1

func accept_preview(response: Dictionary, ticket: int) -> bool:
	if ticket != revision or str(response.get("runId", "")) != str(source.get("runId", "")) or int(response.get("runSequence", -1)) != int(source.get("runSequence", 0)): return false
	preview = response.duplicate(true)
	return true
