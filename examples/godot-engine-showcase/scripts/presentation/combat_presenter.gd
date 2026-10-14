extends RefCounted
## Passive projection of one engine snapshot. Never evaluates effects or issues commands.
var _i18n
var _run: Dictionary
var _combat: Dictionary
var _actions: Array
var _cards := {}
var _actors := {}
var _by_card := {}
var _evaluations := {}
var _appearance: Dictionary = {}
var _zone_snapshot: Dictionary = {}
var _sections
var _base_facts: Dictionary = {}

func accept_presentations(cards: Array) -> void:
	_base_facts.clear()
	for facts in cards:
		var id := str(facts.get("identity", {}).get("cardInstanceId", ""))
		if not id.is_empty(): _base_facts[id] = facts.duplicate(true)

func _init(run: Dictionary, combat: Dictionary, actions: Array, translator, appearance := {}, zone_snapshot := {}) -> void:
	_i18n = translator
	_run = preload("res://scripts/contracts/snapshot_projection.gd").run(run)
	_combat = preload("res://scripts/contracts/snapshot_projection.gd").combat(combat)
	_actions = actions.duplicate(true)
	_appearance = appearance.duplicate(true) if appearance is Dictionary else {}
	_zone_snapshot = zone_snapshot.duplicate(true) if zone_snapshot is Dictionary else {}
	if _zone_snapshot.get("zones", []) is Array and not _zone_snapshot.get("zones", []).is_empty():
		for zone in _zone_snapshot.zones:
			for card in zone.get("cards", []):
				_cards[str(card.get("cardInstanceId", ""))] = card
	else:
		for card in _run.get("deck", {}).get("cardInstances", []):
			_cards[str(card.get("cardInstanceId", ""))] = card
	for actor in _combat.get("actors", []):
		_actors[str(actor.get("instanceId", ""))] = actor
	_sections = preload("res://scripts/presentation/card_section_presenter.gd").new(_i18n, _appearance, actors())
	for candidate in _actions:
		var id := str(candidate.get("command", {}).get("cardInstanceId", ""))
		if not _by_card.has(id):
			_by_card[id] = []
		_by_card[id].append(candidate)

func actors() -> Array:
	return _combat.get("actors", []).map(func(actor): return preload("res://scripts/presentation/view_models.gd").Actor.new(actor).to_dictionary())

func candidate_for_card(instance_id: String, target_id := "") -> Dictionary:
	for candidate in _actions:
		var command: Dictionary = candidate.get("command", {})
		if str(command.get("cardInstanceId", "")) != instance_id:
			continue
		var targets: Array = command.get("targetIds", [])
		if target_id.is_empty() or target_id in targets or str(command.get("targetId", "")) == target_id:
			return candidate.duplicate(true)
	return {}

func system_candidate(action_types: Array) -> Dictionary:
	for candidate in _actions:
		if str(candidate.get("source", "")) == "System" and \
			str(candidate.get("command", {}).get("actionType", "")) in action_types:
			return candidate.duplicate(true)
	return {}

func preview(candidate: Dictionary) -> String:
	if candidate.is_empty():
		return _i18n.text("No legal actions for this card in the current state.")
	var lines: Array[String] = [_i18n.text("Cost: %s") % cost_text(candidate)]
	if bool(candidate.get("outcomeUncertain", false)):
		lines.append(_i18n.text("The outcome depends on the next responses."))
	for application in candidate.get("applications", []):
		lines.append(application_text(application))
	return "  •  ".join(lines)

func card_instance(instance_id: String) -> Dictionary:
	return _cards.get(instance_id, {}).duplicate(true)

func intent_for(actor_id: String) -> String:
	for intent in _combat.get("activation", {}).get("intents", []):
		if str(intent.get("actorId", "")) == actor_id:
			var lines: Array[String] = [_i18n.content_name(str(intent.get("actionId", intent.get("actionType", ""))))]
			for application in intent.get("previewApplications", []):
				lines.append(compact_application(application))
			if intent.get("previewUncertain", false):
				lines.append(_i18n.text("Outcome uncertain"))
			return "\n".join(lines)
	return ""

func candidates(id: String) -> Array:
	return _by_card.get(id, []).duplicate(true)

func targets(candidate: Dictionary) -> Array:
	var command: Dictionary = candidate.get("command", {})
	var targets: Array = command.get("targetIds", [])
	if targets.is_empty() and command.get("targetId") != null:
		targets = [command.targetId]
	return targets.duplicate(true)

func actor_name(id: String) -> String:
	for actor in _combat.get("actors", []):
		if str(actor.get("instanceId", "")) == id:
			return _i18n.content_name(str(actor.get("definitionId", "")), str(actor.get("name", id)))
	return id

func cost_text(candidate: Dictionary) -> String:
	var parts: Array[String] = []
	for cost in candidate.get("costs", []):
		parts.append("%s %s" % [_i18n.number(float(cost.get("amount", 0))), _i18n.content_name(str(cost.get("resourceId", "")))])
	return " + ".join(parts) if not parts.is_empty() else "0"

func card_view_model(instance_id: String) -> Dictionary:
	var card := card_instance(instance_id)
	var definition_id := str(card.get("definitionId", ""))
	var appearance: Dictionary = _appearance.get("cards", {}).get(definition_id, {})
	var candidate := candidate_for_card(instance_id)
	var inspection: Dictionary = _evaluations.get(instance_id, {})
	var base: Dictionary = inspection.get("baseContainer", {}) if inspection.get("baseContainer") is Dictionary else {}
	var upgrades: Array = inspection.get("appliedUpgrades", card.get("upgrades", []))
	var name: String = _i18n.content_name(definition_id, str(appearance.get("name", definition_id)))
	var tone := str(appearance.get("tone", "skill"))
	var tags: Array = base.get("tags", []) if base.get("tags") is Array else []
	var card_type := _card_type(tags, tone)
	# Definitions describe cards; target outcomes belong to the separate preview.
	var summary := str(appearance.get("text", _i18n.text("See inspection for details")))
	var changes := _change_badges(inspection, upgrades)
	var inspected_evaluation: Dictionary = inspection.get("evaluation", {}) if inspection.get("evaluation") is Dictionary else {}
	var published_costs: Array = candidate.get("costs", []) if not candidate.is_empty() else inspected_evaluation.get("costs", [])
	var requires_alternative: bool = inspection.get("effectiveBase", {}).get("components", []).any(func(component): return component.get("type") == "cost" and not component.get("costs", {}).get("alternativeCosts", []).is_empty())
	var costs_known := (not inspection.is_empty() or not candidate.is_empty()) and (not requires_alternative or not candidate.is_empty() or published_costs.any(func(cost): return cost.get("optionId") != null))
	var sections: Dictionary = _sections.sections(inspection, appearance)
	if not sections.get("effectRows", []).is_empty():
		summary = "\n".join(sections.effectRows.map(func(row):
			return "".join(row.get("segments", []).map(func(segment): return str(segment.get("text", ""))))))
	var cost_options: Array = []
	for option in candidates(instance_id):
		var tokens := _cost_tokens(option.get("costs", []))
		if tokens not in cost_options: cost_options.append(tokens)
	var result := {
		"cardInstanceId": instance_id,
		"definitionId": definition_id,
		"tone": tone,
		"name": name,
		"upgraded": not upgrades.is_empty(),
		"cardType": _i18n.text(card_type),
		"rarity": _i18n.text(str(base.get("rarity", "")).to_upper()),
		"summary": summary,
		"effects": summary.split("\n", false),
		"costs": _cost_tokens(published_costs),
		"costsKnown": costs_known,
		"costUnknownLabel": _i18n.text("Cost unavailable"),
		"cost": "" if not candidate.is_empty() or not inspection.is_empty() else _i18n.text("Cost — inspect"),
		"costOptions": cost_options,
		"availability": _i18n.text("SELECT TO PLAY") if not candidate.is_empty() else unavailable_reason(instance_id),
		"changeBadges": changes,
		"inspectionText": inspection_text(instance_id) if not inspection.is_empty() else "",
		"alternativeCostCount": cost_options.size(),
		"artPlaceholder": true
	}
	result.merge(sections, true)
	result["rarity"] = _i18n.text(str(result.rarityId).to_upper())
	var facts: Dictionary = inspection.get("presentation", {}) if inspection.get("presentation") is Dictionary else _base_facts.get(instance_id, {})
	if not facts.is_empty():
		result.merge(preload("res://scripts/presentation/card_facts_presenter.gd").new(_i18n, _appearance).project(facts), true)
	return preload("res://scripts/presentation/view_models.gd").Card.new(result).to_dictionary()

func _card_type(tags: Array, tone: String) -> String:
	for kind in ["attack", "skill", "power"]:
		if kind in tags:
			return kind.to_upper()
	return tone.to_upper()

func _cost_tokens(costs: Array) -> Array:
	var result: Array = []
	var resources: Dictionary = _appearance.get("resources", {})
	for cost in costs:
		var resource_id := str(cost.get("resourceId", ""))
		var style: Dictionary = resources.get(resource_id, {}) if resources.get(resource_id) is Dictionary else {}
		var symbol := str(style.get("symbol", resource_id.substr(0, 1).to_upper()))
		result.append({
			"resourceId": resource_id,
			"amount": float(cost.get("amount", 0)),
			"symbol": symbol if not symbol.is_empty() else "•",
			"name": _i18n.content_name(resource_id)
		})
	return result

func _change_badges(inspection: Dictionary, upgrades: Array) -> Array:
	var badges: Array = []
	if not upgrades.is_empty():
		badges.append({"kind": "upgrade", "label": _i18n.text("UPGRADED"), "symbol": "↑"})
	if inspection.is_empty():
		return badges
	var contextual_change := false
	var source_names: Array[String] = []
	for calculation in inspection.get("calculations", []):
		var outcome_stages: Array = calculation.get("quantity", {}).get("incorporatedStages", []).filter(func(stage): return str(stage.get("scope", "")) in ["Target", "Shared"]).map(func(stage): return str(stage.get("stageId", "")))
		for bucket in calculation.get("buckets", []):
			if str(bucket.get("stageId", "")) in outcome_stages: continue
			for contribution in bucket.get("contributions", []):
				if not bool(contribution.get("applied", false)):
					continue
				var input := float(contribution.get("input", 0))
				var output := float(contribution.get("output", input))
				if is_equal_approx(input, output):
					continue
				var kind := _source_kind(contribution.get("sourceKind", ""))
				if kind not in ["Actor", "Status", "Relic", "GameMode", "Encounter", "Modifier"]:
					continue
				contextual_change = true
				var source_id := str(contribution.get("sourceId", ""))
				if not source_id.is_empty() and source_id not in source_names:
					source_names.append(source_id)
	if not contextual_change:
		return badges
	var kind := "modified"
	var label: String = _i18n.text("CONTEXT CHANGED")
	var symbol := "~"
	# Different parameters/resources cannot be added to infer whether a change is
	# beneficial. Direction is represented on each published numeric effect instead.
	badges.append({"kind": kind, "label": label, "symbol": symbol,
		"sources": source_names.map(func(id): return _i18n.content_name(id))})
	return badges

func _source_kind(value) -> String:
	if value is int or value is float:
		return ["Effect", "Card", "Actor", "Target", "Status", "Relic", "Upgrade", "GameMode", "Encounter", "Modifier"][clampi(int(value), 0, 9)]
	return str(value)

func application_text(application: Dictionary, include_source := false) -> String:
	var text := actor_name(str(application.get("targetEntityId", ""))) + ": "
	var previous = application.get("previousValue")
	var current = application.get("currentValue")
	if previous != null and current != null:
		text += "%s → %s %s" % [_i18n.number(float(previous)), _i18n.number(float(current)),
			_i18n.content_name(str(application.get("resourceId", "")))]
		var field := str(application.get("resourceField", 0))
		if field in ["1", "1.0", "Minimum"]: text += " (" + _i18n.text("Minimum") + ")"
		elif field in ["2", "2.0", "Maximum"]: text += " (" + _i18n.text("Maximum") + ")"
	elif application.get("statusId") != null:
		text += _i18n.content_name(str(application.statusId))
	else:
		text += _i18n.content_name(str(application.get("effectType", "effect")))
	var source: Dictionary = application.get("provenance", {})
	if include_source and not str(source.get("sourceId", "")).is_empty():
		var kind = source.get("kind", "")
		if kind is float or kind is int:
			kind = ["Card", "Status", "Relic", "Ability", "GameMode", "Encounter", "Rule"][clampi(int(kind), 0, 6)]
		text += "  [%s: %s]" % [_i18n.text(str(kind)),
			_i18n.content_name(str(_cards.get(str(source.sourceId), {}).get("definitionId", source.sourceId)))]
	return text

func relationship(actor: Dictionary, viewpoint: String) -> String:
	var policy: Dictionary = _combat.get("relationships", {})
	var from_side := str(_actors.get(viewpoint, {}).get("sideId", ""))
	var to_side := str(actor.get("sideId", ""))
	for rule in policy.get("rules", []):
		if str(rule.get("fromSideId", "")) == from_side and str(rule.get("toSideId", "")) == to_side:
			return str(rule.get("relationship", "Neutral"))
	return str(policy.get("sameSide" if from_side == to_side else "differentSides", "Neutral"))

func accept_evaluations(cards: Array) -> void:
	for card in cards:
		if int(card.get("version", {}).get("runSequence", -1)) == int(_run.get("sequence", 0)):
			_evaluations[str(card.get("evaluation", {}).get("cardInstanceId", ""))] = card.duplicate(true)

func unavailable_reason(id: String) -> String:
	var reasons: Array = _evaluations.get(id, {}).get("evaluation", {}).get("failureReasons", [])
	return "\n".join(reasons) if not reasons.is_empty() else _i18n.text("No legal actions for this card in the current state.")

func action_summary(id: String) -> String:
	var candidates := candidates(id)
	if candidates.is_empty(): return _i18n.text("UNAVAILABLE")
	var descriptions: Array[String] = []
	for candidate in candidates:
		var effects: Array[String] = []
		for application in candidate.get("applications", []):
			if not _is_cost(application, candidate): effects.append(compact_application(application))
		var summary := "\n".join(effects.slice(0, 3))
		if effects.size() > 3: summary += "\n" + _i18n.text("More effects in inspection")
		if candidate.get("outcomeUncertain", false): summary += "\n" + _i18n.text("Outcome uncertain")
		if summary.is_empty(): summary = _i18n.text("See inspection for details")
		if summary not in descriptions: descriptions.append(summary)
	return descriptions[0] if descriptions.size() == 1 else _i18n.text("%s legal choices — select a target") % descriptions.size()

func action_preview(id: String, target_id := "") -> String:
	var candidates := candidates(id)
	if not target_id.is_empty():
		candidates = candidates.filter(func(candidate): return target_id in targets(candidate))
	if candidates.is_empty(): return unavailable_reason(id)
	if target_id.is_empty() and candidates.size() > 1:
		return _i18n.text("Choose a highlighted target.")
	return "\n".join(candidates.map(func(candidate): return preview(candidate)))

func _is_cost(application: Dictionary, candidate: Dictionary) -> bool:
	var source: Dictionary = application.get("provenance", {})
	if str(source.get("sourceId", "")) != str(candidate.get("command", {}).get("cardInstanceId", "")): return false
	return candidate.get("costs", []).any(func(cost): return str(cost.get("componentId", "")) == str(source.get("componentId", "")))

func compact_application(application: Dictionary) -> String:
	# This is formatting a published delta, not calculating an effect or its mitigation.
	var previous = application.get("previousValue")
	var current = application.get("currentValue")
	if previous == null or current == null: return application_text(application)
	var delta := float(current) - float(previous)
	var field := str(application.get("resourceField", 0))
	var suffix := ""
	if field in ["1", "1.0", "Minimum"]: suffix = " " + _i18n.text("Minimum")
	elif field in ["2", "2.0", "Maximum"]: suffix = " " + _i18n.text("Maximum")
	return "%s%s %s%s · %s" % ["+" if delta > 0 else "", _i18n.number(delta),
		_i18n.content_name(str(application.get("resourceId", ""))), suffix,
		actor_name(str(application.get("targetEntityId", "")))]

func inspection_text(id: String) -> String:
	var data: Dictionary = _evaluations.get(id, {})
	if data.get("presentation") is Dictionary:
		return "\n".join(preload("res://scripts/presentation/card_calculation_presenter.gd").new(_i18n).describe(data.presentation, false))
	var lines: Array[String] = [action_summary(id)]
	if candidates(id).is_empty(): lines.append(unavailable_reason(id))
	for candidate in candidates(id): lines.append(preview(candidate))
	if data.is_empty():
		lines.append(_i18n.text("Detailed inspection is unavailable."))
	else:
		lines.append(_i18n.text("Upgrades: %s") % data.get("appliedUpgrades", []).size())
		lines.append(_i18n.text("Base components"))
		for component in data.get("effectiveBase", {}).get("components", []):
			if component.get("effect") is Dictionary:
				var effect: Dictionary = component.effect
				lines.append("%s · %s · ×%s" % [str(component.get("componentId", "")), _i18n.content_name(str(effect.get("type", effect.get("effectType", "effect")))), effect.get("repeat", 1)])
				for parameter in effect.get("parameters", []):
					lines.append("%s: %s" % [str(parameter.get("parameter", parameter.get("parameterId", "value"))), parameter.get("flatValue", "—")])
		for proc in data.get("procs", []):
			lines.append(_i18n.text("Condensed proc" if proc.get("isCondensation", false) else "Proc") + " · " + _i18n.text("Impacts: %s") % proc.get("impactIds", []).size())
			for consumed in proc.get("consumedStacks", []):
				lines.append(_i18n.text("Consumed stacks: %s → %s") % [consumed.get("previousStacks", 0), consumed.get("currentStacks", 0)])
			for hop in proc.get("continuations", []):
				lines.append("%s → %s" % [actor_name(str(hop.get("fromEntityId", ""))), actor_name(str(hop.get("toEntityId", "")))])
		var context: Dictionary = data.get("contextSources", {}) if data.get("contextSources") is Dictionary else {}
		for upgrade in data.get("appliedUpgrades", []):
			lines.append(_i18n.content_name(str(upgrade.get("upgradeId", ""))))
		for pair in [["Relics", "relics", "definitionId"], ["Modifiers", "modifiers", "modifierId"]]:
			var names: Array[String] = []
			for item in context.get(pair[1], []): names.append(_i18n.content_name(str(item.get(pair[2], item.get("definitionId", "")))))
			lines.append(_i18n.text(pair[0]) + ": " + ", ".join(names))
		for actor_id in context.get("statuses", {}):
			for status in context.statuses[actor_id]:
				lines.append("%s: %s ×%s" % [actor_name(actor_id), _i18n.content_name(str(status.get("statusId", ""))), status.get("stacks", 1)])
	return "\n\n".join(lines)

func inspection_data(id: String) -> Dictionary:
	return _evaluations.get(id, {}).duplicate(true)

func pile_cards(zone: String) -> Array:
	var result: Array = []
	if _zone_snapshot.get("zones", []) is Array and not _zone_snapshot.get("zones", []).is_empty():
		for view in _zone_snapshot.zones:
			if str(view.get("zoneId", "")) == zone and bool(view.get("contentsVisible", false)):
				result.append_array(view.get("cards", []))
	else:
		for id in _run.get("deck", {}).get(zone, []):
			var card := card_instance(str(id))
			if not card.is_empty(): result.append(card)
	# Avoid revealing the shuffled draw order through the inspector.
	result.sort_custom(func(a, b): return str(a.get("definitionId", "")) < str(b.get("definitionId", "")))
	return result


func context_key() -> String:
	return JSON.stringify([_run.get("runId", ""), _run.get("branchId", ""), _run.get("sequence", 0),
		_combat.get("combatId", ""), _combat.get("step", 0)])
func action_options(instance_id: String) -> Array:
	var result: Array = []
	for index in _actions.size():
		var candidate: Dictionary = _actions[index]
		if str(candidate.get("command", {}).get("cardInstanceId", "")) == instance_id:
			result.append(preload("res://scripts/presentation/view_models.gd").ActionOption.new({
				"reference": context_key() + "/" + str(index), "candidate": candidate}))
	return result
func combat_model(viewpoint: String):
	var items: Array = actors().map(func(actor):
		var relation := relationship(actor, viewpoint)
		actor["relationship"] = relation
		actor["isInputActor"] = str(actor.get("instanceId", "")) == viewpoint
		for intent in _combat.get("activation", {}).get("intents", []):
			if str(intent.get("actorId", "")) == str(actor.get("instanceId", "")):
				actor["intentIconId"] = str(_appearance.get("intent_icons", {}).get(str(intent.get("actionId", "")), "unknown"))
		return {"actor": actor, "relationship": relation, "sideId": str(actor.get("sideId", "")),
			"intent": intent_for(str(actor.get("instanceId", "")))})
	return preload("res://scripts/presentation/view_models.gd").Combat.new({"context": context_key(), "actors": items})

func header_model(labels: Dictionary):
	var activation: Dictionary = _combat.get("activation", {})
	return preload("res://scripts/presentation/view_models.gd").Header.new({
		"title": _i18n.text("COMBAT"), "context": str(_combat.get("runNodeId", "")) + " · " + str(_combat.get("stateHash", "")),
		"round": _i18n.text("ROUND %s") % int(activation.get("round", _combat.get("currentTurn", 1))),
		"timeline": _i18n.text("TIMELINE  [%s]") % str(labels.get("open_timeline", "open_timeline")),
		"tools": _i18n.text("ZONE TOOLS"),
		"allow_tools": not preload("res://scripts/presentation/card_zone_presenter.gd").tool_flows(_run).is_empty(),
		"pause": _i18n.text("PAUSE")})
