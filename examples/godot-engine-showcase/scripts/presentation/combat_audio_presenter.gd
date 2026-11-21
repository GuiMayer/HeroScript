extends RefCounted
## Published facts to cosmetic cues. Does not calculate effects or assume health.
const ELEMENTS := ["fire", "ice", "blood", "lightning", "water", "void", "shadow", "poison", "arcane", "holy", "wind"]
const STATUS_ELEMENTS := {
	"alisyum_burn": "fire", "prototype_burn": "fire", "alisyum_overheat": "fire",
	"alisyum_cinder_vuln": "fire", "alisyum_pyre_aura": "fire", "alisyum_chilled": "ice",
	"alisyum_frostbite": "ice", "alisyum_glacial_plating": "ice", "alisyum_glacial_monolith": "ice",
	"alisyum_bleed": "blood", "alisyum_blood_surge": "blood", "prototype_wet": "water"
}

static func specialize(cue: String, application: Dictionary, frame: Dictionary) -> String:
	if cue not in ["impact", "status"]: return cue
	var element := ""
	var calculation_id = application.get("calculationId")
	if calculation_id != null and str(calculation_id) != "":
		for calculation in frame.get("calculations", []):
			if calculation.get("calculationId") != calculation_id: continue
			for tag in ELEMENTS:
				if tag in calculation.get("tags", []): element = tag; break
			break
	if element.is_empty():
		var status_id := str(application.get("statusId", ""))
		if application.get("statusId") == null or status_id.is_empty(): status_id = str(application.get("provenance", {}).get("sourceId", ""))
		element = str(STATUS_ELEMENTS.get(status_id, ""))
	if cue == "impact" and not element.is_empty(): return "element_" + element
	if cue == "status":
		if str(application.get("effectType", "")) in ["REMOVE_STATUS", "DISPEL_STATUS", "REMOVE_MODIFIER"]: return "dispel"
		if str(application.get("effectType", "")) == "CONDENSE_STACKS": return "condense"
		if not element.is_empty(): return "status_" + element if element in ["fire", "ice", "blood", "water"] else "element_" + element
	return cue
static func cues(frame: Dictionary, resource_gain_cues: Dictionary = {}) -> Array[String]:
	var result: Array[String] = []
	for application in frame.get("applications", []):
		if preload("res://scripts/presentation/semantic_cues.gd").kind(application, frame) == "payment": continue
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
		cue = specialize(cue, application, frame)
		if not cue.is_empty() and cue not in result: result.append(cue)
	return result.slice(0, 3)
