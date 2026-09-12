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
		var resource = application.get("resourceId")
		var previous = application.get("previousValue")
		var current = application.get("currentValue")
		if resource != null and previous != null and current != null:
			var delta := float(current) - float(previous)
			if not is_zero_approx(delta):
				lines.append("%s: %s%s %s" % [_actor_name(str(application.get("targetEntityId", ""))),
					"+" if delta > 0 else "", _i18n.number(delta), _i18n.content_name(str(resource))])
		elif application.get("statusId") != null:
			lines.append("%s: %s" % [_actor_name(str(application.get("targetEntityId", ""))),
				_i18n.content_name(str(application.statusId))])
	return "  •  ".join(lines)

func _card_instance(instance_id: String) -> Dictionary:
	return _cards.get(instance_id, {}).duplicate(true)

func _intent_for(actor_id: String) -> String:
	for intent in _combat.get("activation", {}).get("intents", []):
		if str(intent.get("actorId", "")) == actor_id:
			return _i18n.content_name(str(intent.get("actionId", intent.get("actionType", ""))))
	return _i18n.text("watching")

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
