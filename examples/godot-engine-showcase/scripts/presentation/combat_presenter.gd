extends RefCounted
## Passive projection of one engine snapshot. Never evaluates effects or issues commands.
enum InputState { READY, SELECTING, SUBMITTING, ANIMATING, RECOVERING, PAUSED }
var _i18n
var _run: Dictionary
var _combat: Dictionary
var _actions: Array
var _cards := {}
var _actors := {}
var _by_card := {}
var _evaluations := {}

func _init(run: Dictionary, combat: Dictionary, actions: Array, translator) -> void:
	_i18n = translator
	_run = run.duplicate(true)
	_combat = combat.duplicate(true)
	_actions = actions.duplicate(true)
	for card in _run.get("deck", {}).get("cardInstances", []):
		_cards[str(card.get("cardInstanceId", ""))] = card
	for actor in _combat.get("actors", []):
		_actors[str(actor.get("instanceId", ""))] = actor
	for candidate in _actions:
		var id := str(candidate.get("command", {}).get("cardInstanceId", ""))
		if not _by_card.has(id):
			_by_card[id] = []
		_by_card[id].append(candidate)

func actors() -> Array:
	return _combat.get("actors", []).duplicate(true)

func input_state(submitting: bool, synchronized: bool, animating: bool, paused: bool, selected: String) -> InputState:
	if paused: return InputState.PAUSED
	if submitting: return InputState.SUBMITTING
	if not synchronized: return InputState.RECOVERING
	if animating: return InputState.ANIMATING
	return InputState.READY if selected.is_empty() else InputState.SELECTING

func _candidate_for_card(instance_id: String, target_id := "") -> Dictionary:
	for candidate in _actions:
		var command: Dictionary = candidate.get("command", {})
		if str(command.get("cardInstanceId", "")) != instance_id:
			continue
		var targets: Array = command.get("targetIds", [])
		if target_id.is_empty() or target_id in targets or str(command.get("targetId", "")) == target_id:
			return candidate
	return {}

func _system_candidate(action_types: Array) -> Dictionary:
	for candidate in _actions:
		if str(candidate.get("source", "")) == "System" and \
			str(candidate.get("command", {}).get("actionType", "")) in action_types:
			return candidate
	return {}

func _preview(candidate: Dictionary) -> String:
	if candidate.is_empty():
		return _i18n.text("No legal actions for this card in the current state.")
	var lines: Array[String] = [_i18n.text("Cost: %s") % _cost_text(candidate)]
	if bool(candidate.get("outcomeUncertain", false)):
		lines.append(_i18n.text("The outcome depends on the next responses."))
	for application in candidate.get("applications", []):
		lines.append(application_text(application))
	return "  •  ".join(lines)

func _card_instance(instance_id: String) -> Dictionary:
	return _cards.get(instance_id, {}).duplicate(true)

func _intent_for(actor_id: String) -> String:
	for intent in _combat.get("activation", {}).get("intents", []):
		if str(intent.get("actorId", "")) == actor_id:
			var lines: Array[String] = [_i18n.content_name(str(intent.get("actionId", intent.get("actionType", ""))))]
			for application in intent.get("previewApplications", []):
				lines.append(application_text(application))
			if intent.get("previewUncertain", false):
				lines.append(_i18n.text("The outcome depends on the next responses."))
			return "\n".join(lines)
	return ""

func _candidates(id: String) -> Array:
	return _by_card.get(id, []).duplicate(true)

func _targets(candidate: Dictionary) -> Array:
	var command: Dictionary = candidate.get("command", {})
	var targets: Array = command.get("targetIds", [])
	if targets.is_empty() and command.get("targetId") != null:
		targets = [command.targetId]
	return targets

func _actor_name(id: String) -> String:
	for actor in _combat.get("actors", []):
		if str(actor.get("instanceId", "")) == id:
			return _i18n.content_name(str(actor.get("definitionId", "")), str(actor.get("name", id)))
	return id

func _cost_text(candidate: Dictionary) -> String:
	var parts: Array[String] = []
	for cost in candidate.get("costs", []):
		parts.append("%s %s" % [_i18n.number(float(cost.get("amount", 0))), _i18n.content_name(str(cost.get("resourceId", "")))])
	return " + ".join(parts) if not parts.is_empty() else "0"

func application_text(application: Dictionary, include_source := false) -> String:
	var text := _actor_name(str(application.get("targetEntityId", ""))) + ": "
	var previous = application.get("previousValue")
	var current = application.get("currentValue")
	if previous != null and current != null:
		text += "%s → %s %s" % [_i18n.number(float(previous)), _i18n.number(float(current)),
			_i18n.content_name(str(application.get("resourceId", "")))]
	elif application.get("statusId") != null:
		text += _i18n.content_name(str(application.statusId))
	else:
		text += _i18n.content_name(str(application.get("effectType", "effect")))
	var source: Dictionary = application.get("provenance", {})
	if include_source and not str(source.get("sourceId", "")).is_empty():
		text += "  [%s: %s]" % [_i18n.content_name(str(source.get("kind", ""))),
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

func card_summary(id: String) -> String:
	var candidates := _candidates(id)
	if candidates.is_empty(): return _i18n.text("UNAVAILABLE")
	var descriptions: Array[String] = []
	for candidate in candidates:
		var effects: Array[String] = []
		for application in candidate.get("applications", []):
			effects.append(application_text(application))
		var summary := "\n".join(effects.slice(0, 3))
		if effects.size() > 3: summary += "\n" + _i18n.text("More effects in inspection")
		if candidate.get("outcomeUncertain", false): summary += "\n" + _i18n.text("Outcome uncertain")
		if summary.is_empty(): summary = _i18n.text("See inspection for details")
		if summary not in descriptions: descriptions.append(summary)
	return descriptions[0] if descriptions.size() == 1 else _i18n.text("%s legal choices — select a target") % descriptions.size()

func inspection_text(id: String) -> String:
	var data: Dictionary = _evaluations.get(id, {})
	var lines: Array[String] = [card_summary(id)]
	if _candidates(id).is_empty(): lines.append(unavailable_reason(id))
	for candidate in _candidates(id): lines.append(_preview(candidate))
	if data.is_empty():
		lines.append(_i18n.text("Detailed inspection is unavailable."))
	else:
		lines.append(_i18n.text("Upgrades: %s") % data.get("appliedUpgrades", []).size())
		var context: Dictionary = data.get("contextSources", {}) if data.get("contextSources") is Dictionary else {}
		for pair in [["Relics", "relics", "relicId"], ["Modifiers", "modifiers", "scriptId"]]:
			var names: Array[String] = []
			for item in context.get(pair[1], []): names.append(_i18n.content_name(str(item.get(pair[2], item.get("definitionId", "")))))
			lines.append(_i18n.text(pair[0]) + ": " + ", ".join(names))
		for actor_id in context.get("statuses", {}):
			for status in context.statuses[actor_id]:
				lines.append("%s: %s ×%s" % [_actor_name(actor_id), _i18n.content_name(str(status.get("statusId", ""))), status.get("stacks", 1)])
	return "\n\n".join(lines)

func inspection_data(id: String) -> Dictionary:
	return _evaluations.get(id, {}).duplicate(true)

func pile_cards(zone: String) -> Array:
	var result: Array = []
	for id in _run.get("deck", {}).get(zone, []):
		var card := _card_instance(str(id))
		if not card.is_empty(): result.append(card)
	# Avoid revealing the shuffled draw order through the inspector.
	result.sort_custom(func(a, b): return str(a.get("definitionId", "")) < str(b.get("definitionId", "")))
	return result
