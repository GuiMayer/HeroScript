extends RefCounted
## Shape adaptation only: persisted aggregates and API projections share one reader.
static func combat(snapshot) -> Dictionary:
	if not (snapshot is Dictionary):
		return {}
	var result: Dictionary = snapshot.duplicate(true)
	var roster = result.get("actors", [])
	if roster is Dictionary:
		var actors: Array = []
		var raw_order = result.get("actorOrder", [])
		var order: Array = raw_order.duplicate() if raw_order is Array else []
		for id in roster:
			if id not in order: order.append(id)
		var status_effects = result.get("statusEffects", {})
		var status_map: Dictionary = status_effects if status_effects is Dictionary else {}
		for id in order:
			if not roster.has(id) or not (roster[id] is Dictionary): continue
			var actor: Dictionary = roster[id].duplicate(true)
			actor["resources"] = {}
			var components = actor.get("components", {})
			if components is Dictionary:
				for component in components.values():
					if component is Dictionary and str(component.get("type", "")) == "resources":
						var res_state = component.get("state", {})
						if res_state is Dictionary and res_state.get("resources") is Dictionary:
							actor.resources = res_state.resources.duplicate(true)
			var actor_statuses = status_map.get(id, [])
			actor["statuses"] = actor_statuses.duplicate(true) if actor_statuses is Array else []
			actors.append(actor)
		result.actors = actors
		result["activation"] = result.get("activationState", {}) if result.get("activationState") is Dictionary else {}
		result["phase"] = result.get("phaseState", {}) if result.get("phaseState") is Dictionary else {}
		var det = result.get("determinism", {})
		result["step"] = det.get("step", 0) if det is Dictionary else 0
	elif not (roster is Array):
		result["actors"] = []
	var activation = result.get("activation", {})
	if not (activation is Dictionary):
		activation = {}
		result["activation"] = activation
	if not result.has("activeActorId") or not (result.get("activeActorId") is String):
		result["activeActorId"] = str(activation.get("activeActorId", "")) if activation.get("activeActorId") is String else ""
	return result

static func run(snapshot) -> Dictionary:
	if not (snapshot is Dictionary):
		return {}
	var result: Dictionary = snapshot.duplicate(true)
	var deck = result.get("deck", {})
	if deck is Dictionary:
		if deck.get("cardInstances") is Dictionary:
			deck.cardInstances = deck.cardInstances.values()
		elif deck.has("cardInstances") and not (deck.get("cardInstances") is Array):
			deck.cardInstances = []
	elif result.has("deck"):
		result["deck"] = {}
	return result
