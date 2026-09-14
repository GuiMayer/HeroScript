extends RefCounted
## Converts engine dialogue projections to display data. No condition/effect evaluation or networking.

static func localized(value: Dictionary, locale: String) -> String:
	var translated := str(value.get("translations", {}).get(locale, ""))
	return translated if not translated.is_empty() else str(value.get("text", ""))

static func build(run: Dictionary, choices: Array, translator) -> Dictionary:
	var dialogue: Dictionary = {}
	for item in run.get("dialogues", []):
		if str(item.get("activityNodeId", "")) == str(run.get("currentNodeId", "")): dialogue = item
	if dialogue.is_empty(): return {}
	var options: Array = []
	for option in dialogue.get("choices", []):
		var command: Dictionary = {}
		for choice in choices:
			if str(choice.type) == "CHOOSE_DIALOGUE_OPTION" and str(choice.subjectId) == str(option.choiceId): command = choice.duplicate(true)
		var price: Array[String] = []
		for cost in option.get("costs", []):
			price.append("%s %s" % [translator.number(float(cost.amount)), translator.content_name(str(cost.resourceId))])
		options.append({"id": option.choiceId, "text": localized(option.text, translator.locale),
			"cost": " + ".join(price), "available": bool(option.get("available", false)) and not command.is_empty(),
			"reason": localized(option.unavailableText, translator.locale) if option.get("unavailableText") is Dictionary else "",
			"command": command})
	var history: Array = []
	for entry in dialogue.get("transcript", []):
		history.append({"speaker": localized(entry.speaker, translator.locale), "text": localized(entry.text, translator.locale)})
	return {"title": localized(dialogue.title, translator.locale), "speaker": localized(dialogue.speaker, translator.locale),
		"text": localized(dialogue.text, translator.locale), "portrait": str(dialogue.get("portraitId", "")),
		"completed": bool(dialogue.get("completed", false)), "options": options, "history": history}

static func recorded_transcript(state: Dictionary, translator) -> Array[String]:
	var lines: Array[String] = []
	var narrative: Dictionary = state.get("narrative", {}) if state.get("narrative") is Dictionary else {}
	for dialogue in narrative.get("dialogues", []):
		if str(dialogue.get("activityNodeId", "")) != str(state.get("currentNodeId", "")): continue
		var nodes: Array = dialogue.get("definition", {}).get("nodes", [])
		for visit in dialogue.get("history", []):
			for node in nodes:
				if str(node.get("nodeId", "")) != str(visit.get("nodeId", "")): continue
				if visit.get("choiceId") == null:
					lines.append(localized(node.get("speaker", {}), translator.locale) + ": " + localized(node.get("text", {}), translator.locale))
				else:
					for choice in node.get("choices", []):
						if str(choice.get("choiceId", "")) == str(visit.choiceId): lines.append("› " + localized(choice.get("text", {}), translator.locale))
	return lines
