extends RefCounted
## Published facts to cosmetic cues. Does not calculate effects or assume health.
static func cues(frame: Dictionary, resource_gain_cues: Dictionary = {}) -> Array[String]:
	var result: Array[String] = []
	for application in frame.get("applications", []):
		var cue := ""
		var type := str(application.get("effectType", ""))
		if type in ["DAMAGE", "HEAL", "MODIFY_RESOURCE"]:
			var before = application.get("previousValue")
			var after = application.get("currentValue")
			var field := str(application.get("resourceField", "Current"))
			if before != null and after != null and field in ["Current", "0", "0.0"] and not is_equal_approx(float(before), float(after)):
				if type == "DAMAGE": cue = "impact"
				elif type == "HEAL": cue = "heal"
				elif float(after) > float(before): cue = str(resource_gain_cues.get(str(application.get("resourceId", "")), ""))
		elif type in ["APPLY_STATUS", "APPLY_MODIFIER", "CONDENSE_STACKS"]:
			cue = "status"
		elif type == "CARD_ZONE_FLOW" and not application.get("cardZoneSteps", []).is_empty():
			cue = "card_move"
		if not cue.is_empty() and cue not in result: result.append(cue)
	return result.slice(0, 3)
