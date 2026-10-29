extends RefCounted
## Only formats committed facts. No rule calculation, hidden-state inference or response execution.
const Messages = preload("res://scripts/presentation/gameplay_event_messages.gd")

static func rows(batch: Dictionary, locale := "en", names: Dictionary = {}) -> Array:
	var result: Array = []
	for fact in batch.get("occurrences", []):
		if not fact is Dictionary: continue
		var payload: Dictionary = fact.get("payload", {})
		var source := str(fact.get("attributedActorId") if fact.get("attributedActorId") != null else fact.get("sourceActorId", ""))
		if source == "<null>" or source.is_empty(): source = Messages.word("environment", locale)
		var target := str(fact.get("subjectActorId", ""))
		if target == "<null>": target = ""
		var owner: Variant = payload.get("owner")
		if target.is_empty() and owner is Dictionary: target = str(owner.get("id", ""))
		var values := {"source": str(names.get(source, source)), "target": str(names.get(target, target)),
			"resource": str(names.get(str(payload.get("resourceId", "")), payload.get("resourceId", ""))),
			"applied": str(payload.get("appliedDelta", "?")), "kind": str(fact.get("kind", ""))}
		var message := Messages.resolve(str(fact.get("reasonId", "")), values.kind, locale).format(values)
		result.append({"id": str(fact.get("eventId", "")), "parent": str(fact.get("parentEventId", "")),
			"message": message, "fact": fact.duplicate(true), "originCause": fact.get("originCause")})
	return result

static func details(row: Dictionary, locale := "en") -> String:
	var fact: Dictionary = row.get("fact", {})
	var payload: Dictionary = fact.get("payload", {})
	var lines: Array[String] = [str(row.get("message", "")), str(fact.get("kind", "")), str(fact.get("reasonId", ""))]
	for key in ["resourceId", "operation", "field", "before", "requestedDelta", "appliedDelta", "limitedDelta", "after"]:
		if payload.has(key): lines.append("%s: %s" % [Messages.word(key, locale), str(payload[key])])
	if row.get("originCause") is Dictionary:
		var cause: Dictionary = row.originCause
		lines.append("%s: %s / %s" % [Messages.word("earlier_cause", locale), str(cause.get("branchId", "")), str(cause.get("eventId", ""))])
	return "\n".join(lines)
