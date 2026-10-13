extends RefCounted
## Shape adaptation only: persisted aggregates and API projections share one reader.
static func combat(snapshot: Dictionary) -> Dictionary:
	var result := snapshot.duplicate(true)
	var roster = result.get("actors", [])
	if roster is Dictionary:
		var actors: Array = []
		var order: Array = result.get("actorOrder", []).duplicate()
		for id in roster:
			if id not in order: order.append(id)
		for id in order:
			if not roster.has(id) or not roster[id] is Dictionary: continue
			var actor: Dictionary = roster[id].duplicate(true)
			actor["resources"] = {}
			var components = actor.get("components", {})
			if components is Dictionary:
				for component in components.values():
					if component is Dictionary and str(component.get("type", "")) == "resources":
						actor.resources = component.get("state", {}).get("resources", {}).duplicate(true)
			actor["statuses"] = result.get("statusEffects", {}).get(id, []).duplicate(true)
			actors.append(actor)
		result.actors = actors
		result["activation"] = result.get("activationState", {}) if result.get("activationState") is Dictionary else {}
		result["phase"] = result.get("phaseState", {}) if result.get("phaseState") is Dictionary else {}
		result["step"] = result.get("determinism", {}).get("step", 0)
	if not result.has("activeActorId"): result["activeActorId"] = result.get("activation", {}).get("activeActorId", "")
	return result
static func run(snapshot: Dictionary) -> Dictionary:
	var result := snapshot.duplicate(true)
	var deck: Dictionary = result.get("deck", {})
	if deck.get("cardInstances") is Dictionary: deck.cardInstances = deck.cardInstances.values()
	return result
