extends RefCounted
## A compact list of visible, applied contributions. Never simulates an effect.
static func cues(frame: Dictionary, translator) -> Array[String]:
	var result: Array[String] = []
	for application in frame.get("applications", []):
		var id := str(application.get("provenance", {}).get("componentId", ""))
		var identity = application.get("identity")
		if identity is Dictionary: id += " / " + str(identity.get("componentId", ""))
		var type := str(application.get("effectType", ""))
		var label := ""
		if id.contains("rune.affinity") and type == "APPLY_STATUS":
			label = "◆ " + translator.content_name(str(application.get("statusId", "")))
		elif id.contains("rune.resonance") and type == "MODIFY_RESOURCE" and application.get("previousValue") != null and application.get("currentValue") != null:
			if float(application.currentValue) > float(application.previousValue): label = "◇ " + translator.text("Resonance") + " +" + translator.number(float(application.currentValue) - float(application.previousValue)) + " " + translator.content_name(str(application.get("resourceId", "")))
		elif id.contains("rune.catalyst") and type == "CARD_ZONE_FLOW" and not application.get("cardZoneSteps", []).is_empty():
			label = "✧ " + translator.text("Catalyst")
		elif type == "CONDENSE_STACKS" and application.get("condensation") != null:
			label = "✦ " + translator.text("Condensation")
		if not label.is_empty() and label not in result: result.append(label)
	var impact_ids: Array = []
	for application in frame.get("applications", []):
		var identity = application.get("identity")
		if identity is Dictionary: impact_ids.append(identity.get("impactId"))
	for step in frame.get("effectSteps", []):
		var identity = step.get("identity")
		if not identity is Dictionary or identity.get("impactId") not in impact_ids or not step.get("applied", false): continue
		var continuation = step.get("continuation")
		if continuation is Dictionary and continuation.get("toEntityId") != null and continuation.get("stopReason") == null:
			var label: String = "↗ " + translator.text("Overflow")
			if label not in result: result.append(label)
	return result
