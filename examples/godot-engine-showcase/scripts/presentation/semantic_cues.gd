extends RefCounted
## Classifies recorded outcomes, never calculates amounts or resource rules.
static func kind(application: Dictionary, frame: Dictionary = {}) -> String:
	var source: Dictionary = application.get("provenance", {})
	for payment in frame.get("payments", []):
		if str(payment.get("sourceId", "")) == str(source.get("sourceId", "")) and str(payment.get("componentId", "")) == str(source.get("componentId", "")): return "payment"
	if str(source.get("componentId", "")) in frame.get("paymentComponentIds", []): return "payment"
	var field := str(application.get("resourceField", "Current"))
	if field not in ["Current", "0", "0.0"] and application.get("resourceId") != null: return "resource_bound"
	match str(application.get("effectType", "")):
		"DAMAGE": return "impact"
		"HEAL": return "heal"
		"APPLY_STATUS", "REMOVE_STATUS", "DISPEL_STATUS": return "status"
		"APPLY_MODIFIER", "REMOVE_MODIFIER", "MODIFY_ATTRIBUTE": return "modifier"
		"CONDENSE_STACKS": return "condense"
		"CARD_ZONE_FLOW": return "card_move"
		"MODIFY_RESOURCE":
			if application.get("previousValue") != null and application.get("currentValue") != null:
				return "resource_gain" if float(application.currentValue) > float(application.previousValue) else "resource_loss"
	return "fact"
