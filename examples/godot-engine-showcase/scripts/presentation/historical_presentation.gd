extends RefCounted
## Adapts the authorized canonical commit. No current-combat lookup or rule replay.
static func frames(commit: Dictionary) -> Array:
	var result: Array = []
	for recorded in commit.get("frames", []):
		if recorded.get("scope", "") != "combat": continue
		result.append({"frameId": recorded.get("resolutionFrameId", ""), "transitionType": recorded.get("kind", ""),
			"applications": recorded.get("applications", []), "effectSteps": recorded.get("effectSteps", []),
			"cardZoneSteps": recorded.get("cardZoneSteps", []), "stateAfter": recorded.get("combatStateAfter")})
	return result
static func state(commit: Dictionary) -> Dictionary:
	var run: Dictionary = commit.get("stateAfter", {})
	var combat: Dictionary = {}
	var resolution: Dictionary = commit.get("combatResolution", {}) if commit.get("combatResolution") is Dictionary else {}
	for encounter in run.get("encounters", []):
		if str(encounter.get("combat", {}).get("combatId", "")) == str(resolution.get("combatId", "")): combat = encounter.combat
	if combat.is_empty():
		for frame in frames(commit):
			if frame.stateAfter is Dictionary: combat = frame.stateAfter
	return {"run": run, "combat": combat, "runSequence": commit.get("sequence", 0)}
